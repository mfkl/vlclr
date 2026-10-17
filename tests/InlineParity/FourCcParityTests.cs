using InlineParity.Native;
using VLCLR.Native;

namespace InlineParity;

/// <summary>VLCFourCC against vlc_fourcc_to_char and vlc_fourcc_GetChromaBPP (vlc_common.h, vlc_fourcc.h).</summary>
public unsafe class FourCcParityTests
{
    [SkippableFact]
    public void Native_structs_have_the_layout_the_tests_assume()
    {
        ParityLibrary.Require();

        Assert.Equal(ParityNative.parity_log_size(), sizeof(ParityLog));
        Assert.Equal(ParityNative.parity_script_size(), sizeof(ParityScript));
    }

    [SkippableFact]
    public void ToString_matches_vlc_fourcc_to_char()
    {
        ParityLibrary.Require();
        byte* expected = stackalloc byte[4];

        foreach (uint fourcc in Cases.AnyUInt32(salt: 1))
        {
            ParityNative.parity_vlc_fourcc_to_char(fourcc, expected);

            string actual = VLCFourCC.ToString(fourcc);

            ParityAssert.Equal(4, actual.Length, $"length for 0x{fourcc:X8}");
            for (int i = 0; i < 4; i++)
            {
                // vlc_fourcc_to_char memcpy's the 4 bytes; the managed chars are those bytes.
                ParityAssert.Equal(expected[i], (byte)actual[i], $"byte {i} for 0x{fourcc:X8}");
                ParityAssert.Equal(0, actual[i] >> 8, $"char {i} is a single byte for 0x{fourcc:X8}");
            }
        }
    }

    private static readonly (string Name, uint Value)[] ManagedCodecs =
    [
        ("XRGB", VLCFourCC.XRGB), ("RGB24", VLCFourCC.RGB24), ("RGBX", VLCFourCC.RGBX),
        ("BGRX", VLCFourCC.BGRX), ("XBGR", VLCFourCC.XBGR), ("BGR24", VLCFourCC.BGR24),
        ("ARGB", VLCFourCC.ARGB), ("ABGR", VLCFourCC.ABGR), ("RGBA", VLCFourCC.RGBA),
        ("BGRA", VLCFourCC.BGRA), ("I420", VLCFourCC.I420), ("YV12", VLCFourCC.YV12),
        ("D3D11_OPAQUE", VLCFourCC.D3D11Opaque), ("D3D11_OPAQUE_10B", VLCFourCC.D3D11Opaque10Bit),
        ("D3D11_OPAQUE_RGBA", VLCFourCC.D3D11OpaqueRgba), ("D3D11_OPAQUE_BGRA", VLCFourCC.D3D11OpaqueBgra),
        ("D3D11_OPAQUE_ALPHA", VLCFourCC.D3D11OpaqueAlpha), ("F32L", VLCFourCC.F32L), ("S16L", VLCFourCC.S16L),
    ];

    [SkippableFact]
    public void Constants_match_the_VLC_CODEC_macros()
    {
        ParityLibrary.Require();

        Dictionary<string, uint> native = [];
        for (int i = 0; i < ParityNative.parity_codec_count(); i++)
        {
            native[ParityNative.CodecName(i)] = ParityNative.parity_codec_value(i);
        }

        Assert.Equal(ManagedCodecs.Length, native.Count);
        foreach ((string name, uint value) in ManagedCodecs)
        {
            ParityAssert.Equal(native[name], value, $"VLC_CODEC_{name}");
        }

        // The legacy names are aliases of the same codes.
        ParityAssert.Equal(native["XRGB"], VLCFourCC.RV32, "RV32");
        ParityAssert.Equal(native["RGB24"], VLCFourCC.RV24, "RV24");
    }

    private static readonly uint[] PackedAndPlanar =
    [
        VLCFourCC.XRGB, VLCFourCC.RGBX, VLCFourCC.BGRX, VLCFourCC.XBGR, VLCFourCC.RGB24, VLCFourCC.BGR24,
        VLCFourCC.RGBA, VLCFourCC.BGRA, VLCFourCC.ARGB, VLCFourCC.ABGR, VLCFourCC.I420, VLCFourCC.YV12,
    ];

    [SkippableFact]
    public void GetBytesPerPixel_matches_the_chroma_pixel_size_for_known_formats()
    {
        ParityLibrary.Require();

        // The managed value is the bytes per pixel of the first plane, which is
        // vlc_chroma_description_t.pixel_size (1 for the planar YUV formats).
        foreach (uint chroma in PackedAndPlanar)
        {
            ParityAssert.Equal(ParityNative.parity_chroma_pixel_size(chroma),
                VLCFourCC.GetBytesPerPixel(chroma), $"pixel_size of {VLCFourCC.ToString(chroma)}");
        }
    }

    [SkippableFact]
    public void Documented_difference_GetBytesPerPixel_is_not_vlc_fourcc_GetChromaBPP_for_planar_yuv()
    {
        ParityLibrary.Require();

        // vlc_fourcc_GetChromaBPP is the average bits per pixel over all planes
        // (12 for 4:2:0). GetBytesPerPixel deliberately reports the Y plane only: 1 byte.
        foreach (uint chroma in new[] { VLCFourCC.I420, VLCFourCC.YV12 })
        {
            Assert.Equal(12u, ParityNative.parity_vlc_fourcc_GetChromaBPP(chroma));
            Assert.Equal(1, VLCFourCC.GetBytesPerPixel(chroma));
        }

        // For single-plane formats the two views agree: bytes * 8 = bits, when every bit is used.
        foreach (uint chroma in new[] { VLCFourCC.RGBA, VLCFourCC.BGRA, VLCFourCC.ARGB, VLCFourCC.ABGR, VLCFourCC.RGB24, VLCFourCC.BGR24 })
        {
            ParityAssert.Equal((int)ParityNative.parity_vlc_fourcc_GetChromaBPP(chroma),
                VLCFourCC.GetBytesPerPixel(chroma) * 8, $"bits of {VLCFourCC.ToString(chroma)}");
        }
    }

    [SkippableFact]
    public void Documented_difference_GetBytesPerPixel_guesses_for_chromas_VLC_does_not_describe()
    {
        ParityLibrary.Require();

        // VLC has no description for these, so the C helpers report "unknown" (0 bits, no pixel_size).
        uint unknown32 = (uint)('X' | 'Y' << 8 | '3' << 16 | '2' << 24);
        Assert.Equal(-1, ParityNative.parity_chroma_pixel_size(unknown32));
        Assert.Equal(0u, ParityNative.parity_vlc_fourcc_GetChromaBPP(unknown32));

        // The managed helper guesses from the four characters instead.
        Assert.Equal(4, VLCFourCC.GetBytesPerPixel(unknown32));
        Assert.Equal(0, VLCFourCC.GetBytesPerPixel(0));
    }
}
