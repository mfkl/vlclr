using VLCLR.Native;
using VLCLR.Types;

namespace VLCLR;

public enum VLCPlaylistRepeatMode { None, CurrentItem, All, Inconsistent }
public enum VLCPlaylistOrder { Normal, Random }

/// <summary>Observed VLC 3 legacy playlist mode variables, never a requested/cached value.</summary>
public readonly record struct VLCPlaylistModeSnapshot(VLCPlaylistRepeatMode Repeat, VLCPlaylistOrder Order, bool RepeatVariable, bool LoopVariable);

public sealed partial class VLCPlaylistQueue
{
    /// <summary>Reads the effective legacy <c>repeat</c>, <c>loop</c>, and <c>random</c> variables.</summary>
    public VLCPlaylistModeSnapshot? TryGetModes(out VLCPlaylistResult result)
    {
        if (!TryGetLease(out var lease, out result)) return null;
        if (!TryGetBoolean(lease.Pointer, "repeat", out bool repeat, out int error) ||
            !TryGetBoolean(lease.Pointer, "loop", out bool loop, out error) ||
            !TryGetBoolean(lease.Pointer, "random", out bool random, out error))
        {
            result = new(VLCPlaylistResultCode.NativeFailure, error); return null;
        }
        result = VLCPlaylistResult.Success;
        return new(repeat && loop ? VLCPlaylistRepeatMode.Inconsistent : repeat ? VLCPlaylistRepeatMode.CurrentItem : loop ? VLCPlaylistRepeatMode.All : VLCPlaylistRepeatMode.None,
            random ? VLCPlaylistOrder.Random : VLCPlaylistOrder.Normal, repeat, loop);
    }

    /// <summary>
    /// Sets a mutually-consistent repeat mode. VLC 3 exposes two independent variables, so this cannot be atomic.
    /// A failed second write can leave an observed inconsistent state; callers must use <see cref="TryGetModes"/>
    /// after any failure (and after external changes) rather than treating this request as the effective state.
    /// </summary>
    public VLCPlaylistResult SetRepeatMode(VLCPlaylistRepeatMode mode)
    {
        if (mode is not VLCPlaylistRepeatMode.None and not VLCPlaylistRepeatMode.CurrentItem and not VLCPlaylistRepeatMode.All) return new(VLCPlaylistResultCode.InvalidArgument);
        if (!TryGetLease(out var lease, out var result)) return result;
        // Clear the conflicting flag before setting the requested flag. Do not hold playlist_Lock: VLC forbids
        // var_Set/callback execution while it is held.
        int error = 0;
        foreach (var write in GetRepeatWrites(mode)) { error = SetBoolean(lease.Pointer, write.Name, write.Value); if (error != 0) break; }
        return error == 0 ? VLCPlaylistResult.Success : new(VLCPlaylistResultCode.NativeFailure, error);
    }

    public VLCPlaylistResult SetOrder(VLCPlaylistOrder order)
    {
        if (order is not VLCPlaylistOrder.Normal and not VLCPlaylistOrder.Random) return new(VLCPlaylistResultCode.InvalidArgument);
        if (!TryGetLease(out var lease, out var result)) return result;
        int error = SetBoolean(lease.Pointer, "random", order == VLCPlaylistOrder.Random);
        return error == 0 ? VLCPlaylistResult.Success : new(VLCPlaylistResultCode.NativeFailure, error);
    }

    private static bool TryGetBoolean(nint playlist, string name, out bool value, out int error)
    {
        error = VLCCore.VarGetChecked(playlist, name, VLCVarType.Bool, out VLCValueNative native);
        value = error == 0 && native.Bool != 0;
        return error == 0;
    }

    private static int SetBoolean(nint playlist, string name, bool value) =>
        VLCCore.VarSetChecked(playlist, name, VLCVarType.Bool, new VLCValueNative { Bool = value ? (byte)1 : (byte)0 });

    internal static (string Name, bool Value)[] GetRepeatWrites(VLCPlaylistRepeatMode mode) => mode switch
    {
        VLCPlaylistRepeatMode.None => [("repeat", false), ("loop", false)],
        VLCPlaylistRepeatMode.CurrentItem => [("loop", false), ("repeat", true)],
        VLCPlaylistRepeatMode.All => [("repeat", false), ("loop", true)],
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };
}
