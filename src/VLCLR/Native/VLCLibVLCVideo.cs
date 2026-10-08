using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>Fixed public libVLC 3 video ABI, corroborated against VLC 3.0.23-2 lib/video.c.</summary>
internal static partial class VLCLibVLCVideo
{
    private const string Library = "libvlc";
    [LibraryImport(Library, EntryPoint = "libvlc_video_set_scale")] internal static partial void SetScale(nint player, float scale);
    [LibraryImport(Library, EntryPoint = "libvlc_video_set_aspect_ratio", StringMarshalling = StringMarshalling.Utf8)] internal static partial void SetAspectRatio(nint player, string aspect);
    [LibraryImport(Library, EntryPoint = "libvlc_video_set_crop_geometry", StringMarshalling = StringMarshalling.Utf8)] internal static partial void SetCrop(nint player, string crop);
    [LibraryImport(Library, EntryPoint = "libvlc_video_set_deinterlace", StringMarshalling = StringMarshalling.Utf8)] internal static partial void SetDeinterlace(nint player, string mode);
    [LibraryImport(Library, EntryPoint = "libvlc_video_take_snapshot", StringMarshalling = StringMarshalling.Utf8)] internal static partial int TakeSnapshot(nint player, uint number, string path, uint width, uint height);
}
