using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using VLCLR.Native;

namespace VLCLR;

/// <summary>A short-lived, borrowed main-playlist lease supplied by the host.</summary>
/// <remarks>The provider must return <see langword="null"/> before VLC starts destroying the core.
/// VLCLR never holds this pointer between calls and cannot make an unheld VLC playlist survive teardown.</remarks>
public readonly record struct VLCPlaylistLease(nint Pointer, long CoreGeneration)
{
    public bool IsValid => Pointer != nint.Zero;
}

public enum VLCPlaylistResultCode { Success, RequestDispatched, InvalidArgument, StaleSnapshot, NotFound, CoreUnavailable, NativeFailure, UnsupportedPlatform, NestedQueueUnsupported }

public readonly record struct VLCPlaylistResult(VLCPlaylistResultCode Code, int NativeError = 0, int CompletedCount = 0)
{
    public bool Succeeded => Code == VLCPlaylistResultCode.Success;
    /// <summary>True when VLC accepted a synchronous operation or a void asynchronous request was dispatched.</summary>
    public bool Accepted => Code is VLCPlaylistResultCode.Success or VLCPlaylistResultCode.RequestDispatched;
    public static VLCPlaylistResult Success => new(VLCPlaylistResultCode.Success);
}

/// <summary>Opaque held input-item identity. It is not a playlist_item_t pointer.</summary>
public sealed class VLCPlaylistItemIdentity : IDisposable
{
    private readonly object _gate = new();
    private nint _input;
    internal VLCPlaylistItemIdentity(nint input) => _input = input;
    internal bool TryHold(out nint input)
    {
        lock (_gate)
        {
            input = _input == nint.Zero ? nint.Zero : VLCCore.InputItemHold(_input);
            return input != nint.Zero;
        }
    }
    public void Dispose()
    {
        nint input;
        lock (_gate) { input = _input; _input = nint.Zero; }
        if (input != nint.Zero) VLCCore.InputItemRelease(input);
        GC.SuppressFinalize(this);
    }
    ~VLCPlaylistItemIdentity() => Dispose();
}

public sealed record VLCPlaylistItem(int Id, string Name, string Uri, bool IsCurrent, VLCPlaylistItemIdentity Identity);

/// <summary>Copied queue state. Dispose it when it is replaced to release held input identities.</summary>
public sealed class VLCPlaylistSnapshot : IDisposable
{
    private readonly ulong _fingerprint;
    internal VLCPlaylistSnapshot(long version, ulong fingerprint, IReadOnlyList<VLCPlaylistItem> items, int? currentItemId, int currentIndex)
        => (Version, _fingerprint, Items, CurrentItemId, CurrentIndex) = (version, fingerprint, items, currentItemId, currentIndex);
    public long Version { get; }
    public IReadOnlyList<VLCPlaylistItem> Items { get; }
    public int? CurrentItemId { get; }
    public int CurrentIndex { get; }
    /// <summary>True at a nonwrapping normal-order boundary. Product code must combine this with observed repeat/random mode.</summary>
    public bool CanPrevious => CurrentIndex > 0;
    /// <summary>True at a nonwrapping normal-order boundary. Product code must combine this with observed repeat/random mode.</summary>
    public bool CanNext => CurrentIndex >= 0 && CurrentIndex < Items.Count - 1;
    internal ulong Fingerprint => _fingerprint;
    public void Dispose() { foreach (var item in Items) item.Identity.Dispose(); GC.SuppressFinalize(this); }
}

/// <summary>
/// Narrow VLC 3 main-queue API. Snapshots use dispatcher-friendly polling: <see cref="VLCPlaylistSnapshot.Version"/>
/// advances when a polled locked state differs. No VLC callback is installed, avoiding callback lifetime/reentrancy hazards.
/// </summary>
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
public sealed unsafe partial class VLCPlaylistQueue : IVLCPlaylistQueueSmokeAdapter
{
    private const int MaxQueueItems = 100_000;
    private const int PlaylistViewPlayQuery = 1;
    private readonly Func<VLCPlaylistLease?> _leaseProvider;
    private long _version;
    private ulong _lastFingerprint;
    private bool _hasFingerprint;

    public VLCPlaylistQueue(Func<VLCPlaylistLease?> leaseProvider)
        => _leaseProvider = leaseProvider ?? throw new ArgumentNullException(nameof(leaseProvider));

    public VLCPlaylistSnapshot? TryGetSnapshot(out VLCPlaylistResult result)
    {
        if (!TryGetLease(out VLCPlaylistLease lease, out result)) return null;
        VLCCore.PlaylistLock(lease.Pointer);
        try
        {
            if (!TryReadQueueLocked(lease.Pointer, out var root, out _, out int count, out result)) return null;
            nint current = VLCCore.PlaylistCurrentPlayingItem(lease.Pointer); // documented locked precondition
            var items = new List<VLCPlaylistItem>(count);
            ulong fingerprint = (ulong)lease.CoreGeneration;
            for (int i = 0; i < count; i++)
            {
                nint itemPointer = Marshal.ReadIntPtr(root.Children, checked(i * IntPtr.Size));
                if (itemPointer == nint.Zero) { result = new(VLCPlaylistResultCode.NativeFailure); Dispose(items); return null; }
                VLCPlaylistItemNative item = Unsafe.Read<VLCPlaylistItemNative>((void*)itemPointer);
                if (item.ChildCount != -1) { result = new(VLCPlaylistResultCode.NestedQueueUnsupported); Dispose(items); return null; }
                if (item.Input == nint.Zero) { result = new(VLCPlaylistResultCode.NativeFailure); Dispose(items); return null; }
                nint held = VLCCore.InputItemHold(item.Input);
                if (held == nint.Zero) { result = new(VLCPlaylistResultCode.NativeFailure); Dispose(items); return null; }
                string name;
                string uri;
                try { name = CopyAndFree(VLCCore.InputItemGetName(item.Input)); uri = CopyAndFree(VLCCore.InputItemGetUri(item.Input)); }
                catch { VLCCore.InputItemRelease(held); Dispose(items); result = new(VLCPlaylistResultCode.NativeFailure); return null; }
                items.Add(new VLCPlaylistItem(item.Id, name, uri, itemPointer == current, new VLCPlaylistItemIdentity(held)));
                fingerprint = MixString(Mix(Mix(fingerprint, (ulong)(uint)item.Id), (ulong)item.Input), name);
                fingerprint = MixString(fingerprint, uri);
            }
            fingerprint = Mix(fingerprint, (ulong)current);
            if (!_hasFingerprint || _lastFingerprint != fingerprint) { _lastFingerprint = fingerprint; _hasFingerprint = true; _version++; }
            result = VLCPlaylistResult.Success;
            int currentIndex = items.FindIndex(x => x.IsCurrent);
            return new VLCPlaylistSnapshot(_version, fingerprint, items, currentIndex < 0 ? null : items[currentIndex].Id, currentIndex);
        }
        finally { VLCCore.PlaylistUnlock(lease.Pointer); }
    }

    /// <summary>
    /// Gets the current playlist item only when the playlist's held active input resolves to that same item.
    /// This is stronger than <see cref="VLCPlaylistSnapshot.CurrentItemId"/>, which VLC sets before input startup.
    /// </summary>
    public bool TryGetActiveCurrentItemId(out int? itemId, out VLCPlaylistResult result)
    {
        itemId = null;
        if (!TryGetLease(out var lease, out result)) return false;
        VLCCore.PlaylistLock(lease.Pointer);
        try
        {
            nint current = VLCCore.PlaylistCurrentPlayingItem(lease.Pointer);
            nint input = VLCCore.PlaylistCurrentInputLocked(lease.Pointer);
            if (current == nint.Zero || input == nint.Zero) { result = VLCPlaylistResult.Success; return true; }
            try
            {
                nint inputItem = VLCCore.InputGetItem(input);
                nint resolved = inputItem == nint.Zero ? nint.Zero : VLCCore.PlaylistItemGetByInput(lease.Pointer, inputItem);
                if (resolved == current) itemId = Unsafe.Read<VLCPlaylistItemNative>((void*)current).Id;
                result = VLCPlaylistResult.Success;
                return true;
            }
            finally { VLCCore.ObjectRelease(input); }
        }
        finally { VLCCore.PlaylistUnlock(lease.Pointer); }
    }

    public VLCPlaylistResult Add(string uri, bool playNow = false)
    {
        if (string.IsNullOrWhiteSpace(uri)) return new(VLCPlaylistResultCode.InvalidArgument);
        if (!TryGetLease(out var lease, out var result)) return result;
        int error = VLCCore.PlaylistAdd(lease.Pointer, uri, playNow);
        return error == 0 ? VLCPlaylistResult.Success : new(VLCPlaylistResultCode.NativeFailure, error);
    }

    /// <summary>Validates and materializes every URI before changing VLC. Enumeration exceptions are propagated before mutation.</summary>
    public VLCPlaylistResult Add(IEnumerable<string> uris, bool playFirst = false)
    {
        if (uris is null) return new(VLCPlaylistResultCode.InvalidArgument);
        List<string> values;
        try { values = uris.ToList(); }
        catch { throw; }
        if (values.Count == 0 || values.Count > MaxQueueItems || values.Any(string.IsNullOrWhiteSpace)) return new(VLCPlaylistResultCode.InvalidArgument);
        bool first = true;
        int completed = 0;
        foreach (string uri in values) { var result = Add(uri, playFirst && first); if (!result.Succeeded) return result with { CompletedCount = completed }; first = false; completed++; }
        return new(VLCPlaylistResultCode.Success, CompletedCount: completed);
    }

    public VLCPlaylistResult Remove(VLCPlaylistSnapshot snapshot, VLCPlaylistItem item) => Mutate(snapshot, item, static (p, i, r, _) => { VLCCore.PlaylistNodeDelete(p, i); return VLCPlaylistResult.Success; });
    public VLCPlaylistResult SelectAndPlay(VLCPlaylistSnapshot snapshot, VLCPlaylistItem item) => Mutate(snapshot, item, static (p, i, r, _) => ViewPlay(p, r, i));

    public VLCPlaylistResult Move(VLCPlaylistSnapshot snapshot, VLCPlaylistItem item, int destinationIndex)
    {
        if (snapshot is null || item is null || destinationIndex < 0 || destinationIndex >= snapshot.Items.Count) return new(VLCPlaylistResultCode.InvalidArgument);
        return Mutate(snapshot, item, static (p, i, r, index) => { int source = IndexOfRoot(r, i); if (source < 0) return new(VLCPlaylistResultCode.NotFound); int e = VLCCore.PlaylistTreeMove(p, i, r, NativeInsertionIndex(source, index)); return e == 0 ? VLCPlaylistResult.Success : new(VLCPlaylistResultCode.NativeFailure, e); }, destinationIndex);
    }

    public VLCPlaylistResult Clear(VLCPlaylistSnapshot snapshot)
    {
        if (snapshot is null) return new(VLCPlaylistResultCode.InvalidArgument);
        if (!TryGetLease(out var lease, out var result)) return result;
        VLCCore.PlaylistLock(lease.Pointer);
        try { return IsCurrentLocked(lease.Pointer, lease.CoreGeneration, snapshot) ? ClearLocked(lease.Pointer) : new(VLCPlaylistResultCode.StaleSnapshot); }
        finally { VLCCore.PlaylistUnlock(lease.Pointer); }
    }

    /// <summary>
    /// Dispatches the previous item in the observed flat queue.
    /// This deliberately uses VLC 3's explicit <c>PLAYLIST_VIEWPLAY</c> request instead of
    /// <c>PLAYLIST_SKIP</c>: the latter is an integer-vararg request whose target is resolved
    /// later by VLC's playlist thread.  The explicit target preserves the documented
    /// nonwrapping <see cref="VLCPlaylistSnapshot.CanPrevious"/> contract in normal mode.
    /// Repeat-all wraps. Random selects a different current root child and intentionally has no
    /// managed previous-history guarantee.
    /// </summary>
    public VLCPlaylistResult Previous() => Navigate(-1);

    /// <summary>
    /// Dispatches the next item in the observed flat queue. Repeat-all wraps; random selects a
    /// different current root child. Completion remains asynchronous; observe
    /// <see cref="TryGetActiveCurrentItemId"/>.
    /// </summary>
    public VLCPlaylistResult Next() => Navigate(1);

    private VLCPlaylistResult Mutate(VLCPlaylistSnapshot snapshot, VLCPlaylistItem item, Func<nint, nint, nint, int, VLCPlaylistResult> action, int value = 0)
    {
        if (snapshot is null || item is null || !item.Identity.TryHold(out nint heldInput)) return new(VLCPlaylistResultCode.InvalidArgument);
        if (!TryGetLease(out var lease, out var result)) { VLCCore.InputItemRelease(heldInput); return result; }
        VLCCore.PlaylistLock(lease.Pointer);
        try
        {
            if (!IsCurrentLocked(lease.Pointer, lease.CoreGeneration, snapshot)) return new(VLCPlaylistResultCode.StaleSnapshot);
            if (!TryReadQueueLocked(lease.Pointer, out var root, out nint rootPointer, out _, out result)) return result;
            nint nativeItem = VLCCore.PlaylistItemGetByInput(lease.Pointer, heldInput);
            if (nativeItem == nint.Zero || !IsChildOfRoot(root, nativeItem)) return new(VLCPlaylistResultCode.NotFound);
            return action(lease.Pointer, nativeItem, rootPointer, value);
        }
        finally { VLCCore.PlaylistUnlock(lease.Pointer); VLCCore.InputItemRelease(heldInput); }
    }

    private VLCPlaylistResult Navigate(int delta)
    {
        if (!TryGetLease(out var lease, out var result)) return result;
        // Variable callbacks may execute, so read effective mode before taking playlist_Lock.
        if (!TryGetBoolean(lease.Pointer, "loop", out bool loop, out int error) ||
            !TryGetBoolean(lease.Pointer, "random", out bool random, out error))
            return new(VLCPlaylistResultCode.NativeFailure, error);
        VLCCore.PlaylistLock(lease.Pointer);
        try
        {
            if (!TryReadQueueLocked(lease.Pointer, out var root, out nint rootPointer, out int count, out result)) return result;
            nint current = VLCCore.PlaylistCurrentPlayingItem(lease.Pointer);
            int currentIndex = IndexOfRoot(rootPointer, current);
            int randomOffset = random && count > 1 ? Random.Shared.Next(count - 1) : 0;
            if (!TryGetNavigationIndex(currentIndex, count, delta, loop, random, randomOffset, out int targetIndex)) return new(VLCPlaylistResultCode.NotFound);
            nint target = Marshal.ReadIntPtr(root.Children, checked(targetIndex * IntPtr.Size));
            if (target == nint.Zero) return new(VLCPlaylistResultCode.NativeFailure);
            // Header's playlist_ViewPlay inline helper calls playlist_Control(pl,
            // PLAYLIST_VIEWPLAY, pl_Locked, node, item).  It is the exact fixed pointer
            // shape already used by SelectAndPlay, unlike PLAYLIST_SKIP's int vararg.
            return ViewPlay(lease.Pointer, rootPointer, target);
        }
        finally { VLCCore.PlaylistUnlock(lease.Pointer); }
    }

    private static VLCPlaylistResult ClearLocked(nint playlist) { VLCCore.PlaylistClear(playlist, true); return VLCPlaylistResult.Success; }
    private static VLCPlaylistResult ViewPlay(nint playlist, nint root, nint item) { VLCPlatform.EnsureSupported(nameof(VLCPlaylistQueue)); VLCVariadic.Call(PlaylistControlExport.Value, playlist, PlaylistViewPlayQuery, 1, root, item); return new(VLCPlaylistResultCode.RequestDispatched); }
    private bool IsCurrentLocked(nint playlist, long coreGeneration, VLCPlaylistSnapshot snapshot) => TryFingerprintLocked(playlist, coreGeneration, out ulong fingerprint) && fingerprint == snapshot.Fingerprint;
    private static bool IsChildOfRoot(VLCPlaylistItemNative root, nint item) { if (item == nint.Zero || root.Children == nint.Zero || root.ChildCount < 0 || root.ChildCount > MaxQueueItems) return false; for (int i = 0; i < root.ChildCount; i++) if (Marshal.ReadIntPtr(root.Children, checked(i * IntPtr.Size)) == item) return true; return false; }
    private static int IndexOfRoot(nint rootPointer, nint item) { var root = Unsafe.Read<VLCPlaylistItemNative>((void*)rootPointer); if (!IsValidQueueShape(rootPointer, root.ChildCount, root.Children)) return -1; for (int i = 0; i < root.ChildCount; i++) if (Marshal.ReadIntPtr(root.Children, checked(i * IntPtr.Size)) == item) return i; return -1; }
    internal static int NativeInsertionIndex(int sourceIndex, int finalDestinationIndex) => sourceIndex < finalDestinationIndex ? checked(finalDestinationIndex + 1) : finalDestinationIndex;
    internal static bool TryGetNavigationIndex(int currentIndex, int count, int delta, bool loop, bool random, int randomOffset, out int targetIndex)
    {
        targetIndex = -1;
        if (count <= 0 || (delta is not -1 and not 1) || currentIndex < 0 || currentIndex >= count) return false;
        if (random)
        {
            if (count == 1) return false;
            int offset = Math.Clamp(randomOffset, 0, count - 2);
            targetIndex = offset >= currentIndex ? offset + 1 : offset;
            return true;
        }
        int candidate = checked(currentIndex + delta);
        if (candidate < 0 || candidate >= count)
        {
            if (!loop) return false;
            candidate = candidate < 0 ? count - 1 : 0;
        }
        targetIndex = candidate;
        return true;
    }
    private static bool TryReadQueueLocked(nint playlist, out VLCPlaylistItemNative root, out nint rootPointer, out int count, out VLCPlaylistResult result)
    {
        VLCPlaylistNative native = Unsafe.Read<VLCPlaylistNative>((void*)playlist); rootPointer = native.PlayingRoot;
        if (rootPointer == nint.Zero) { root = default; count = 0; result = new(VLCPlaylistResultCode.NativeFailure); return false; }
        root = Unsafe.Read<VLCPlaylistItemNative>((void*)rootPointer); count = root.ChildCount;
        if (!IsValidQueueShape(rootPointer, count, root.Children)) { result = new(VLCPlaylistResultCode.NativeFailure); return false; }
        result = VLCPlaylistResult.Success; return true;
    }
    private static bool TryFingerprintLocked(nint playlist, long coreGeneration, out ulong fingerprint)
    {
        fingerprint = (ulong)coreGeneration;
        if (!TryReadQueueLocked(playlist, out var root, out _, out int count, out _)) return false;
        nint current = VLCCore.PlaylistCurrentPlayingItem(playlist);
        for (int i = 0; i < count; i++) { nint ptr = Marshal.ReadIntPtr(root.Children, checked(i * IntPtr.Size)); if (ptr == nint.Zero) return false; var item = Unsafe.Read<VLCPlaylistItemNative>((void*)ptr); if (item.ChildCount != -1 || item.Input == nint.Zero) return false; try { fingerprint = MixString(Mix(Mix(fingerprint, (ulong)(uint)item.Id), (ulong)item.Input), CopyAndFree(VLCCore.InputItemGetName(item.Input))); fingerprint = MixString(fingerprint, CopyAndFree(VLCCore.InputItemGetUri(item.Input))); } catch { return false; } }
        fingerprint = Mix(fingerprint, (ulong)current); return true;
    }
    private bool TryGetLease(out VLCPlaylistLease lease, out VLCPlaylistResult result) { var candidate = _leaseProvider(); if (candidate is not { IsValid: true }) { lease = default; result = new(VLCPlaylistResultCode.CoreUnavailable); return false; } lease = candidate.Value; result = VLCPlaylistResult.Success; return true; }
    private static string CopyAndFree(nint value) { if (value == nint.Zero) return string.Empty; try { return Marshal.PtrToStringUTF8(value) ?? string.Empty; } finally { VLCCore.Free(value); } }
    private static void Dispose(IEnumerable<VLCPlaylistItem> items) { foreach (var item in items) item.Identity.Dispose(); }
    private static ulong Mix(ulong x, ulong y) => (x ^ y) * 1099511628211UL;
    internal static ulong MixString(ulong value, string text) { foreach (char c in text) value = Mix(value, c); return Mix(value, (ulong)text.Length); }
    internal static bool IsValidQueueShape(nint rootPointer, int count, nint children) => rootPointer != nint.Zero && count >= 0 && count <= MaxQueueItems && (count == 0 || children != nint.Zero);
    // playlist_Control is variadic with no exported va_list form (see VLCVariadic).
    private static class PlaylistControlExport { internal static readonly nint Value = NativeLibrary.GetExport(VLCCore.Library, "playlist_Control"); }
}
