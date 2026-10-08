using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Direct P/Invoke declarations for libvlccore functions.
/// These call VLC's native library directly without the C glue layer.
/// VLC 3.x API surface.
/// </summary>
public static partial class VLCCore
{
    private const string LibraryName = "libvlccore";

    /// <summary>Returns VLC's monotonic system clock in microseconds (mdate).</summary>
    [LibraryImport(LibraryName, EntryPoint = "mdate")]
    public static partial long TickNow();

    #region Logging

    /// <summary>
    /// Log a message through VLC's logging system (vlc_Log).
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "vlc_Log", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void Log(
        nint obj,
        int type,
        string module,
        string file,
        uint line,
        string func,
        string format,
        string message);

    #endregion

    #region Internal Core Lifecycle

    [LibraryImport(LibraryName, EntryPoint = "libvlc_InternalCreate")]
    public static partial nint libvlc_InternalCreate();

    [LibraryImport(LibraryName, EntryPoint = "libvlc_InternalInit")]
    public static partial int libvlc_InternalInit(nint lib, int argc, nint argv);

    [LibraryImport(LibraryName, EntryPoint = "libvlc_InternalPlay")]
    public static partial void libvlc_InternalPlay(nint lib);

    [LibraryImport(LibraryName, EntryPoint = "libvlc_InternalCleanup")]
    public static partial void libvlc_InternalCleanup(nint lib);

    [LibraryImport(LibraryName, EntryPoint = "libvlc_InternalDestroy")]
    public static partial void libvlc_InternalDestroy(nint lib);

    #region VLC 3 Core Player Lifecycle

    /// <summary>
    /// Raw binding for VLC's deprecated name lookup. The <paramref name="parent"/>
    /// is borrowed. A non-null result owns one held reference and must be passed
    /// to <see cref="ObjectRelease"/> exactly once.
    /// </summary>
    /// <remarks>
    /// VLC explicitly forbids casting the result to a more specific object type.
    /// In particular, this is not a supported way to acquire the main playlist.
    /// </remarks>
    [LibraryImport(LibraryName, EntryPoint = "vlc_object_find_name", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint ObjectFindName(nint parent, string name);

    /// <summary>
    /// Adds one held reference to the borrowed <paramref name="instance"/> and
    /// returns the same pointer. The added reference must be released with
    /// <see cref="ObjectRelease"/> exactly once.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "vlc_object_hold")]
    public static partial nint ObjectHold(nint instance);

    /// <summary>
    /// Consumes one held VLC object reference. The released reference must not
    /// be used afterwards; the object remains valid only through other holds.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "vlc_object_release")]
    public static partial void ObjectRelease(nint instance);

    /// <summary>
    /// Gets the current input from the borrowed <paramref name="playlist"/>.
    /// A non-null result owns one held reference and must be passed to
    /// <see cref="ObjectRelease"/> exactly once.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "playlist_CurrentInput")]
    public static partial nint PlaylistCurrentInput(nint playlist);

    /// <summary>Gets a held current input while the caller already holds playlist_Lock.</summary>
    [LibraryImport(LibraryName, EntryPoint = "playlist_CurrentInputLocked")]
    internal static partial nint PlaylistCurrentInputLocked(nint playlist);

    /// <summary>
    /// Gets the status of the borrowed <paramref name="playlist"/>. Native VLC
    /// playlist synchronization preconditions remain the caller's responsibility.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "playlist_Status")]
    public static partial int PlaylistStatus(nint playlist);

    /// <summary>Gets the volume of the borrowed <paramref name="playlist"/>.</summary>
    [LibraryImport(LibraryName, EntryPoint = "playlist_VolumeGet")]
    public static partial float PlaylistVolumeGet(nint playlist);

    /// <summary>Sets the volume of the borrowed <paramref name="playlist"/>.</summary>
    [LibraryImport(LibraryName, EntryPoint = "playlist_VolumeSet")]
    public static partial int PlaylistVolumeSet(nint playlist, float volume);

    /// <summary>Gets the mute state of the borrowed <paramref name="playlist"/>.</summary>
    [LibraryImport(LibraryName, EntryPoint = "playlist_MuteGet")]
    public static partial int PlaylistMuteGet(nint playlist);

    /// <summary>Sets the mute state of the borrowed <paramref name="playlist"/>.</summary>
    [LibraryImport(LibraryName, EntryPoint = "playlist_MuteSet")]
    public static partial int PlaylistMuteSet(nint playlist, [MarshalAs(UnmanagedType.U1)] bool muted);

    // VLC 3.0.24-beta1 SDK: vlc_playlist.h. These are fixed-signature exports.
    // The public managed API deliberately keeps playlist_item_t pointers private.
    [LibraryImport(LibraryName, EntryPoint = "playlist_Lock")]
    internal static partial void PlaylistLock(nint playlist);

    [LibraryImport(LibraryName, EntryPoint = "playlist_Unlock")]
    internal static partial void PlaylistUnlock(nint playlist);

    [LibraryImport(LibraryName, EntryPoint = "playlist_Add", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int PlaylistAdd(nint playlist, string uri, [MarshalAs(UnmanagedType.I1)] bool playNow);

    [LibraryImport(LibraryName, EntryPoint = "playlist_Clear")]
    internal static partial void PlaylistClear(nint playlist, [MarshalAs(UnmanagedType.I1)] bool locked);

    [LibraryImport(LibraryName, EntryPoint = "playlist_TreeMove")]
    internal static partial int PlaylistTreeMove(nint playlist, nint item, nint parent, int position);

    [LibraryImport(LibraryName, EntryPoint = "playlist_NodeDelete")]
    internal static partial void PlaylistNodeDelete(nint playlist, nint item);

    [LibraryImport(LibraryName, EntryPoint = "playlist_ItemGetByInput")]
    internal static partial nint PlaylistItemGetByInput(nint playlist, nint input);

    [LibraryImport(LibraryName, EntryPoint = "playlist_CurrentPlayingItem")]
    internal static partial nint PlaylistCurrentPlayingItem(nint playlist);

    [LibraryImport(LibraryName, EntryPoint = "input_item_Hold")]
    internal static partial nint InputItemHold(nint input);

    [LibraryImport(LibraryName, EntryPoint = "input_item_Release")]
    internal static partial void InputItemRelease(nint input);

    /// <summary>Creates a VLC-owned external input slave. On a successful
    /// <see cref="InputItemAddSlave"/> call ownership transfers to the input item;
    /// otherwise the caller must free it with <see cref="Free"/>.</summary>
    [LibraryImport(LibraryName, EntryPoint = "input_item_slave_New", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint InputItemSlaveNew(string uri, int type, int priority);

    /// <summary>Adds an owned slave to an input item. VLC 3 source transfers
    /// ownership only when this function returns success.</summary>
    [LibraryImport(LibraryName, EntryPoint = "input_item_AddSlave")]
    internal static partial int InputItemAddSlave(nint item, nint slave);

    [LibraryImport(LibraryName, EntryPoint = "input_item_GetName")]
    internal static partial nint InputItemGetName(nint input);

    [LibraryImport(LibraryName, EntryPoint = "input_item_GetURI")]
    internal static partial nint InputItemGetUri(nint input);

    /// <summary>Returns an owned UTF-8 strdup (or null), which the caller releases with <see cref="Free"/>.</summary>
    [LibraryImport(LibraryName, EntryPoint = "input_item_GetMeta")]
    internal static partial nint InputItemGetMeta(nint input, int metaType);

    /// <summary>Copies a lock-protected input-item info value. Release the returned strdup with Free.</summary>
    [LibraryImport(LibraryName, EntryPoint = "input_item_GetInfo", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint InputItemGetInfo(nint item, string category, string name);

    /// <summary>Returns borrowed translated text; never free the result.</summary>
    [LibraryImport(LibraryName, EntryPoint = "vlc_gettext")]
    internal static partial nint GetText(nint text);

    /// <summary>Gets the borrowed input_item_t for a held input_thread_t (vlc_input.h).</summary>
    [LibraryImport(LibraryName, EntryPoint = "input_GetItem")]
    internal static partial nint InputGetItem(nint input);

    /// <summary>
    /// Invokes VLC's fixed-signature <c>input_vaControl</c> entry point with a
    /// <see cref="VLCVaList"/>. Prefer <see cref="InputControl"/>.
    /// </summary>
    /// <remarks>
    /// VLC's <c>input_Control</c> is variadic and deliberately has no managed
    /// declaration.
    /// </remarks>
    [LibraryImport(LibraryName, EntryPoint = "input_vaControl")]
    internal static partial int InputVaControl(nint input, int query, nint vaList);

    /// <summary>
    /// Calls <c>input_Control(input, query, ...)</c> with each argument in one
    /// 8-byte slot (see <see cref="VLCVaList"/>). The caller holds <paramref name="input"/>.
    /// </summary>
    internal static unsafe int InputControl(nint input, int query, params ReadOnlySpan<long> arguments)
    {
        long* slots = stackalloc long[Math.Max(arguments.Length, 1)];
        arguments.CopyTo(new Span<long>(slots, arguments.Length));
        VLCVaList.SystemVTag tag;
        return InputVaControl(input, query, VLCVaList.Create(slots, &tag));
    }

    /// <summary>
    /// The core library, for exports that need a custom function-pointer signature.
    /// </summary>
    internal static nint Library => LoadedLibrary.Handle;

    private static class LoadedLibrary
    {
        internal static readonly nint Handle = Load();

        private static nint Load()
        {
            if (NativeLibrary.TryLoad(LibraryName, typeof(VLCCore).Assembly, null, out nint handle))
                return handle;
            // Linux distributions ship only the versioned soname. dlopen matches
            // it against the copy the host already loaded, wherever that lives.
            return NativeLibrary.Load("libvlccore.so.9");
        }
    }

    #endregion

    #endregion

    #region Variables

    [LibraryImport(LibraryName, EntryPoint = "var_Create", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int VarCreate(nint obj, string name, int type);

    [LibraryImport(LibraryName, EntryPoint = "var_Destroy", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void VarDestroy(nint obj, string name);

    [LibraryImport(LibraryName, EntryPoint = "var_SetChecked", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int VarSetChecked(nint obj, string name, int type, VLCValueNative value);

    [LibraryImport(LibraryName, EntryPoint = "var_GetChecked", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int VarGetChecked(nint obj, string name, int type, out VLCValueNative value);

    [LibraryImport(LibraryName, EntryPoint = "var_Inherit", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int VarInherit(nint obj, string name, int type, out VLCValueNative value);

    /// <summary>Fixed five-argument VLC 3 variable change ABI. For
    /// <c>VLC_VAR_GETCHOICES</c>, the returned value/text arrays are released
    /// together with <see cref="VarFreeList"/>.</summary>
    [LibraryImport(LibraryName, EntryPoint = "var_Change", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int VarChange(nint obj, string name, int action, ref VLCValueNative value, ref VLCValueNative value2);

    [LibraryImport(LibraryName, EntryPoint = "var_FreeList")]
    internal static partial void VarFreeList(ref VLCValueNative values, ref VLCValueNative texts);

    #endregion

    #region Memory Management

    /// <summary>
    /// Frees memory VLC allocated with its C runtime's <c>malloc</c>: msvcrt for
    /// the MinGW-built Windows runtime, the process C library on Linux.
    /// </summary>
    public static unsafe void Free(nint ptr) => CFree.Value(ptr);

    private static unsafe class CFree
    {
        internal static readonly delegate* unmanaged[Cdecl]<nint, void> Value =
            (delegate* unmanaged[Cdecl]<nint, void>)(OperatingSystem.IsWindows()
                ? NativeLibrary.GetExport(NativeLibrary.Load("msvcrt.dll"), "free")
                : NativeLibrary.GetExport(NativeLibrary.GetMainProgramHandle(), "free"));
    }

    #endregion

    #region Picture Management

    /// <summary>
    /// Create a new picture from a video format.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "picture_NewFromFormat")]
    public static partial nint PictureNewFromFormat(nint format);

    /// <summary>
    /// Creates a picture that consumes the resource's system allocation and
    /// destroy callback on success. The format and resource pointers are only
    /// borrowed for the duration of the call. On failure resource ownership
    /// remains with the caller.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "picture_NewFromResource")]
    public static unsafe partial VLCPicture* PictureNewFromResource(
        in VLCVideoFormat format,
        in VLCPictureResource resource);

    /// <summary>
    /// Adds one reference to a borrowed picture and returns the same pointer.
    /// The added reference must be consumed by <see cref="PictureRelease"/>.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "picture_Hold")]
    public static unsafe partial VLCPicture* PictureHold(VLCPicture* picture);

    /// <summary>
    /// Copy both picture dynamic properties and pixels.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "picture_Copy")]
    public static partial void PictureCopy(nint dst, nint src);

    /// <summary>
    /// Copy picture dynamic properties without copying pixels.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "picture_CopyProperties")]
    public static partial void PictureCopyProperties(nint dst, nint src);

    /// <summary>
    /// Release a picture reference (picture_Release in VLC 3).
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "picture_Release")]
    public static partial void PictureRelease(nint picture);

    /// <summary>Creates a VLC-owned pool of CPU pictures in the supplied format.</summary>
    [LibraryImport(LibraryName, EntryPoint = "picture_pool_NewFromFormat")]
    public static partial nint PicturePoolNewFromFormat(
        in VLCVideoFormat format,
        uint count);

    /// <summary>
    /// Creates a pool from a borrowed configuration. On success the pool owns
    /// the supplied picture references and retains the callbacks; on failure it
    /// releases neither pictures nor caller storage.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "picture_pool_NewExtended")]
    public static unsafe partial nint PicturePoolNewExtended(
        in VLCPicturePoolConfiguration configuration);

    /// <summary>
    /// Creates a pool from an array borrowed for this call. On success the pool
    /// owns the picture references; on failure none are released.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "picture_pool_New")]
    public static unsafe partial nint PicturePoolNew(uint count, VLCPicture** pictures);

    /// <summary>
    /// Consumes the caller's pool reference. The pool releases its owned source
    /// pictures once all late pictures have returned.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "picture_pool_Release")]
    public static partial void PicturePoolRelease(nint pool);

    /// <summary>
    /// Returns a held picture from the borrowed pool, or null when unavailable.
    /// A non-null result must be consumed by <see cref="PictureRelease"/>.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "picture_pool_Get")]
    public static unsafe partial VLCPicture* PicturePoolGet(nint pool);

    /// <summary>
    /// Waits for and returns a held picture from the borrowed pool. A non-null
    /// result must be consumed by <see cref="PictureRelease"/>.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "picture_pool_Wait")]
    public static unsafe partial VLCPicture* PicturePoolWait(nint pool);

    /// <summary>
    /// Invokes a fixed-signature callback for each pool-owned picture. Both the
    /// pool and picture arguments are borrowed; the callback may run while a
    /// picture is in use and must only read stable picture data.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "picture_pool_Enum")]
    public static unsafe partial void PicturePoolEnum(
        nint pool,
        delegate* unmanaged[Cdecl]<nint, VLCPicture*, void> callback,
        nint userdata);

    /// <summary>
    /// Creates a sub-pool from a borrowed master. A non-null returned pool owns
    /// its reserved references and must be consumed by PicturePoolRelease.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "picture_pool_Reserve")]
    public static partial nint PicturePoolReserve(nint pool, uint count);

    /// <summary>Gets the count from a borrowed pool.</summary>
    [LibraryImport(LibraryName, EntryPoint = "picture_pool_GetSize")]
    public static partial uint PicturePoolGetSize(nint pool);

    // VLC 3 picture_pool_Cancel and picture_pool_OwnsPic are fixed internal
    // functions but are not VLC_API exports, so they intentionally have no
    // LibraryImport. D3D11 helper macros/inlines (is_d3d11_opaque,
    // KNOWN_DXGI_INDEX, Acquire/ReleasePictureSys helpers) are represented by
    // constants/ownership documentation, not fictitious imports. No variadic
    // picture or pool API is exposed here.

    /// <summary>
    /// Get the chroma description for a fourcc code.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "vlc_fourcc_GetChromaDescription")]
    public static partial nint FourccGetChromaDescription(uint fourcc);

    #endregion

    #region Subpicture Region Management

    [LibraryImport(LibraryName, EntryPoint = "subpicture_region_New")]
    public static partial nint SubpictureRegionNew(nint format);

    [LibraryImport(LibraryName, EntryPoint = "subpicture_region_Delete")]
    public static partial void SubpictureRegionDelete(nint region);

    [LibraryImport(LibraryName, EntryPoint = "subpicture_Delete")]
    public static partial void SubpictureDelete(nint subpicture);

    #endregion

    #region Text Style Management

    [LibraryImport(LibraryName, EntryPoint = "text_style_New")]
    public static partial nint TextStyleNew();

    [LibraryImport(LibraryName, EntryPoint = "text_style_Create")]
    public static partial nint TextStyleCreate(int features);

    [LibraryImport(LibraryName, EntryPoint = "text_style_Copy")]
    public static partial nint TextStyleCopy(nint dst, nint src);

    [LibraryImport(LibraryName, EntryPoint = "text_style_Duplicate")]
    public static partial nint TextStyleDuplicate(nint style);

    [LibraryImport(LibraryName, EntryPoint = "text_style_Merge")]
    public static partial void TextStyleMerge(nint dst, nint src, [MarshalAs(UnmanagedType.U1)] bool override_);

    [LibraryImport(LibraryName, EntryPoint = "text_style_Delete")]
    public static partial void TextStyleDelete(nint style);

    #endregion

    #region Text Segment Management

    [LibraryImport(LibraryName, EntryPoint = "text_segment_New", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint TextSegmentNew(string? text);

    [LibraryImport(LibraryName, EntryPoint = "text_segment_NewInheritStyle")]
    public static partial nint TextSegmentNewInheritStyle(nint style);

    [LibraryImport(LibraryName, EntryPoint = "text_segment_Delete")]
    public static partial void TextSegmentDelete(nint segment);

    [LibraryImport(LibraryName, EntryPoint = "text_segment_ChainDelete")]
    public static partial void TextSegmentChainDelete(nint segment);

    [LibraryImport(LibraryName, EntryPoint = "text_segment_Copy")]
    public static partial nint TextSegmentCopy(nint segment);

    #endregion
}

/// <summary>
/// VLC value union for P/Invoke. Matches vlc_value_t in C.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 8)]
public struct VLCValueNative
{
    [FieldOffset(0)]
    public long Integer;
    [FieldOffset(0)]
    public byte Bool;
    [FieldOffset(0)]
    public float Float;
    [FieldOffset(0)]
    public nint String;
    [FieldOffset(0)]
    public nint Address;
    [FieldOffset(0)]
    public int CoordX;
    [FieldOffset(4)]
    public int CoordY;
}
