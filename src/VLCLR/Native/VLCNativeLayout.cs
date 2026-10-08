// VLC native layout constants for the VLC 3.x ABI.
//
// Derived from the pinned VLC 3.0.24-beta1 plugin headers and validated
// by the ABI probe in tests/AbiValidation/vlclr_abi.cpp.

namespace VLCLR.Native;

public static class VLCNativeLayout
{
    /// <summary>True when targeting the MSVC-built win64 VLC ABI.</summary>
    public static readonly bool IsWindowsAbi =
#if VLCLR_WINDOWS_ABI
        true;
#else
        false;
#endif

    /// <summary>sizeof(video_format_t) = 176 bytes (VLC 3, no dovi field).</summary>
    public const int VideoFormatSize = 176;

    /// <summary>sizeof(es_format_t) = 264 bytes.</summary>
    public const int EsFormatSize = 264;

    /// <summary>sizeof(filter_t) = 672 bytes (VLC 3, inline callbacks).</summary>
    public const int FilterSize = 672;

    /// <summary>sizeof(vlc_object_t) = 40 bytes (VLC 3 vlc_common_members).</summary>
    public const int ObjectHeaderSize = 40;

    /// <summary>sizeof(picture_t) = 384 bytes.</summary>
    public const int PictureSize = 384;

    /// <summary>sizeof(picture_sys_t) for the VLC 3 D3D11 Win64 implementation.</summary>
    public const int D3D11PictureSysSize = 112;

    /// <summary>sizeof(picture_resource_t) on Win64.</summary>
    public const int PictureResourceSize = 96;

    /// <summary>sizeof(picture_pool_configuration_t) on Win64.</summary>
    public const int PicturePoolConfigurationSize = 32;

    /// <summary>sizeof(vout_display_t) = 504 bytes on the VLC 3 win64 ABI.</summary>
    public const int VoutDisplaySize = 504;

    /// <summary>offsetof(picture_t, p[0]) = 176 bytes.</summary>
    public const int PicturePlanesOffset = 176;

    /// <summary>offsetof(picture_t, i_planes)</summary>
    public const int PicturePlaneCountOffset = 336;

    /// <summary>offsetof(picture_t, date)</summary>
    public const int PictureDateOffset = 344;

    /// <summary>sizeof(subpicture_region_t) = 240 bytes.</summary>
    public const int SubpictureRegionSize = 240;

    /// <summary>sizeof(subpicture_t) = 104 bytes.</summary>
    public const int SubpictureSize = 104;

    /// <summary>sizeof(text_style_t) = 88 bytes (VLC 3, with karaoke fields).</summary>
    public const int TextStyleSize = 88;

    /// <summary>sizeof(text_segment_t) = 24 bytes (VLC 3, no Ruby).</summary>
    public const int TextSegmentSize = 24;

    /// <summary>sizeof(filter_owner_t) = 16 bytes.</summary>
    public const int FilterOwnerSize = 16;
}
