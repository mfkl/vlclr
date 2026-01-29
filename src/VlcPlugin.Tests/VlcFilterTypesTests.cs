using System.Runtime.InteropServices;
using VlcPlugin.Native;
using Xunit;

namespace VlcPlugin.Tests;

/// <summary>
/// Comprehensive tests for VLC struct layouts.
/// These tests verify that C# struct definitions match VLC 4.x native struct layouts.
/// Critical for correct interop with the VLC video filter API.
/// </summary>
public class VlcFilterTypesTests
{
    #region Struct Size Tests

    [Fact]
    public void VlcPlane_Size_Is32Bytes()
    {
        // plane_t is 32 bytes on 64-bit (28 bytes data + 4 bytes padding)
        Assert.Equal(32, Marshal.SizeOf<VlcPlane>());
    }

    [Fact]
    public void VlcVideoFormat_Size_Is152Bytes()
    {
        // video_format_t is 152 bytes on 64-bit VLC 4.x
        // This was discovered through memory scanning during debugging
        Assert.Equal(152, Marshal.SizeOf<VlcVideoFormat>());
    }

    [Fact]
    public void VlcEsFormat_Size_Is240Bytes()
    {
        // es_format_t = 56 (before union) + 152 (video_format_t) + 32 (after) = 240 bytes
        Assert.Equal(240, Marshal.SizeOf<VlcEsFormat>());
    }

    [Fact]
    public void VlcObjectHeader_Size_Is24Bytes()
    {
        // vlc_object_t header is 24 bytes on 64-bit
        Assert.Equal(24, Marshal.SizeOf<VlcObjectHeader>());
    }

    [Fact]
    public void VlcFilter_Size_Is592Bytes()
    {
        // filter_t is 592 bytes with explicit layout
        Assert.Equal(592, Marshal.SizeOf<VlcFilter>());
    }

    [Fact]
    public void VlcFilterOperations_Size_Is48Bytes()
    {
        // 6 function pointers * 8 bytes = 48 bytes
        Assert.Equal(48, Marshal.SizeOf<VlcFilterOperations>());
    }

    [Fact]
    public void VlcFilterOwner_Size_Is24Bytes()
    {
        // 3 pointers * 8 bytes = 24 bytes
        Assert.Equal(24, Marshal.SizeOf<VlcFilterOwner>());
    }

    [Fact]
    public void VlcFilterVideoCallbacks_Size_Is16Bytes()
    {
        // 2 pointers * 8 bytes = 16 bytes
        Assert.Equal(16, Marshal.SizeOf<VlcFilterVideoCallbacks>());
    }

    [Fact]
    public void VlcPicture_Size_IsAtLeast368Bytes()
    {
        // picture_t: 152 (format) + 160 (5 planes * 32) + other fields
        int size = Marshal.SizeOf<VlcPicture>();
        Assert.True(size >= 368, $"VlcPicture size {size} should be at least 368 bytes");
    }

    #endregion

    #region VlcPlane Field Offset Tests

    [Fact]
    public unsafe void VlcPlane_Pixels_IsAtOffset0()
    {
        VlcPlane plane = default;
        byte* basePtr = (byte*)&plane;
        byte* fieldPtr = (byte*)&plane.Pixels;
        Assert.Equal(0, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcPlane_Lines_IsAtOffset8()
    {
        VlcPlane plane = default;
        byte* basePtr = (byte*)&plane;
        byte* fieldPtr = (byte*)&plane.Lines;
        Assert.Equal(8, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcPlane_Pitch_IsAtOffset12()
    {
        VlcPlane plane = default;
        byte* basePtr = (byte*)&plane;
        byte* fieldPtr = (byte*)&plane.Pitch;
        Assert.Equal(12, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcPlane_PixelPitch_IsAtOffset16()
    {
        VlcPlane plane = default;
        byte* basePtr = (byte*)&plane;
        byte* fieldPtr = (byte*)&plane.PixelPitch;
        Assert.Equal(16, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcPlane_VisibleLines_IsAtOffset20()
    {
        VlcPlane plane = default;
        byte* basePtr = (byte*)&plane;
        byte* fieldPtr = (byte*)&plane.VisibleLines;
        Assert.Equal(20, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcPlane_VisiblePitch_IsAtOffset24()
    {
        VlcPlane plane = default;
        byte* basePtr = (byte*)&plane;
        byte* fieldPtr = (byte*)&plane.VisiblePitch;
        Assert.Equal(24, (int)(fieldPtr - basePtr));
    }

    #endregion

    #region VlcVideoFormat Field Offset Tests

    [Fact]
    public unsafe void VlcVideoFormat_Chroma_IsAtOffset0()
    {
        VlcVideoFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Chroma;
        Assert.Equal(0, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcVideoFormat_Width_IsAtOffset4()
    {
        VlcVideoFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Width;
        Assert.Equal(4, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcVideoFormat_Height_IsAtOffset8()
    {
        VlcVideoFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Height;
        Assert.Equal(8, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcVideoFormat_Palette_IsAtOffset48()
    {
        VlcVideoFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Palette;
        Assert.Equal(48, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcVideoFormat_Orientation_IsAtOffset56()
    {
        VlcVideoFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Orientation;
        Assert.Equal(56, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcVideoFormat_ProjectionMode_IsAtOffset88()
    {
        VlcVideoFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.ProjectionMode;
        Assert.Equal(88, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcVideoFormat_PoseYaw_IsAtOffset92()
    {
        VlcVideoFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.PoseYaw;
        Assert.Equal(92, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcVideoFormat_CubemapPadding_IsAtOffset140()
    {
        VlcVideoFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.CubemapPadding;
        Assert.Equal(140, (int)(fieldPtr - basePtr));
    }

    #endregion

    #region VlcEsFormat Field Offset Tests

    [Fact]
    public unsafe void VlcEsFormat_Category_IsAtOffset0()
    {
        VlcEsFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Category;
        Assert.Equal(0, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcEsFormat_Codec_IsAtOffset4()
    {
        VlcEsFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Codec;
        Assert.Equal(4, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcEsFormat_Language_IsAtOffset24()
    {
        VlcEsFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Language;
        Assert.Equal(24, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcEsFormat_Description_IsAtOffset32()
    {
        VlcEsFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Description;
        Assert.Equal(32, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcEsFormat_ExtraLanguages_IsAtOffset48()
    {
        VlcEsFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.ExtraLanguages;
        Assert.Equal(48, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcEsFormat_Video_IsAtOffset56()
    {
        VlcEsFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Video;
        Assert.Equal(56, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcEsFormat_VideoChroma_IsAtOffset56()
    {
        // Video.Chroma should be at offset 56 (start of Video struct)
        VlcEsFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Video.Chroma;
        Assert.Equal(56, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcEsFormat_VideoWidth_IsAtOffset60()
    {
        VlcEsFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Video.Width;
        Assert.Equal(60, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcEsFormat_Bitrate_IsAtOffset208()
    {
        // After Video (56 + 152 = 208)
        VlcEsFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Bitrate;
        Assert.Equal(208, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcEsFormat_Profile_IsAtOffset212()
    {
        VlcEsFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Profile;
        Assert.Equal(212, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcEsFormat_Level_IsAtOffset216()
    {
        VlcEsFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Level;
        Assert.Equal(216, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcEsFormat_Packetized_IsAtOffset220()
    {
        VlcEsFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Packetized;
        Assert.Equal(220, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcEsFormat_ExtraSize_IsAtOffset224()
    {
        VlcEsFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.ExtraSize;
        Assert.Equal(224, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcEsFormat_Extra_IsAtOffset232()
    {
        VlcEsFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Extra;
        Assert.Equal(232, (int)(fieldPtr - basePtr));
    }

    #endregion

    #region VlcObjectHeader Field Offset Tests

    [Fact]
    public unsafe void VlcObjectHeader_Logger_IsAtOffset0()
    {
        VlcObjectHeader header = default;
        byte* basePtr = (byte*)&header;
        byte* fieldPtr = (byte*)&header.Logger;
        Assert.Equal(0, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcObjectHeader_PrivOrObj_IsAtOffset8()
    {
        VlcObjectHeader header = default;
        byte* basePtr = (byte*)&header;
        byte* fieldPtr = (byte*)&header.PrivOrObj;
        Assert.Equal(8, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcObjectHeader_NoInteract_IsAtOffset16()
    {
        VlcObjectHeader header = default;
        byte* basePtr = (byte*)&header;
        byte* fieldPtr = (byte*)&header.NoInteract;
        Assert.Equal(16, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcObjectHeader_Force_IsAtOffset17()
    {
        VlcObjectHeader header = default;
        byte* basePtr = (byte*)&header;
        byte* fieldPtr = (byte*)&header.Force;
        Assert.Equal(17, (int)(fieldPtr - basePtr));
    }

    #endregion

    #region VlcFilter Explicit Layout Field Offset Tests

    [Fact]
    public unsafe void VlcFilter_Obj_IsAtOffset0()
    {
        VlcFilter filter = default;
        byte* basePtr = (byte*)&filter;
        byte* fieldPtr = (byte*)&filter.Obj;
        Assert.Equal(0, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcFilter_Module_IsAtOffset24()
    {
        VlcFilter filter = default;
        byte* basePtr = (byte*)&filter;
        byte* fieldPtr = (byte*)&filter.Module;
        Assert.Equal(24, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcFilter_Sys_IsAtOffset32()
    {
        VlcFilter filter = default;
        byte* basePtr = (byte*)&filter;
        byte* fieldPtr = (byte*)&filter.Sys;
        Assert.Equal(32, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcFilter_FormatIn_IsAtOffset40()
    {
        VlcFilter filter = default;
        byte* basePtr = (byte*)&filter;
        byte* fieldPtr = (byte*)&filter.FormatIn;
        Assert.Equal(40, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcFilter_FormatIn_VideoChroma_IsAtOffset96()
    {
        // FormatIn (40) + Video offset within EsFormat (56) = 96
        VlcFilter filter = default;
        byte* basePtr = (byte*)&filter;
        byte* fieldPtr = (byte*)&filter.FormatIn.Video.Chroma;
        Assert.Equal(96, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcFilter_FormatIn_VideoWidth_IsAtOffset100()
    {
        VlcFilter filter = default;
        byte* basePtr = (byte*)&filter;
        byte* fieldPtr = (byte*)&filter.FormatIn.Video.Width;
        Assert.Equal(100, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcFilter_VideoContextIn_IsAtOffset280()
    {
        // FormatIn (40) + VlcEsFormat size (240) = 280
        VlcFilter filter = default;
        byte* basePtr = (byte*)&filter;
        byte* fieldPtr = (byte*)&filter.VideoContextIn;
        Assert.Equal(280, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcFilter_FormatOut_IsAtOffset288()
    {
        VlcFilter filter = default;
        byte* basePtr = (byte*)&filter;
        byte* fieldPtr = (byte*)&filter.FormatOut;
        Assert.Equal(288, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcFilter_VideoContextOut_IsAtOffset528()
    {
        // FormatOut (288) + VlcEsFormat size (240) = 528
        VlcFilter filter = default;
        byte* basePtr = (byte*)&filter;
        byte* fieldPtr = (byte*)&filter.VideoContextOut;
        Assert.Equal(528, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcFilter_AllowFormatOutChange_IsAtOffset536()
    {
        VlcFilter filter = default;
        byte* basePtr = (byte*)&filter;
        byte* fieldPtr = (byte*)&filter.AllowFormatOutChange;
        Assert.Equal(536, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcFilter_Name_IsAtOffset544()
    {
        VlcFilter filter = default;
        byte* basePtr = (byte*)&filter;
        byte* fieldPtr = (byte*)&filter.Name;
        Assert.Equal(544, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcFilter_Config_IsAtOffset552()
    {
        VlcFilter filter = default;
        byte* basePtr = (byte*)&filter;
        byte* fieldPtr = (byte*)&filter.Config;
        Assert.Equal(552, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcFilter_Operations_IsAtOffset560()
    {
        // This is the critical offset that caused the assertion failure
        VlcFilter filter = default;
        byte* basePtr = (byte*)&filter;
        byte* fieldPtr = (byte*)&filter.Operations;
        Assert.Equal(560, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcFilter_Owner_IsAtOffset568()
    {
        VlcFilter filter = default;
        byte* basePtr = (byte*)&filter;
        byte* fieldPtr = (byte*)&filter.Owner;
        Assert.Equal(568, (int)(fieldPtr - basePtr));
    }

    #endregion

    #region VlcFilterOperations Field Offset Tests

    [Fact]
    public unsafe void VlcFilterOperations_FilterVideo_IsAtOffset0()
    {
        VlcFilterOperations ops = default;
        byte* basePtr = (byte*)&ops;
        byte* fieldPtr = (byte*)&ops.FilterVideo;
        Assert.Equal(0, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcFilterOperations_Drain_IsAtOffset8()
    {
        VlcFilterOperations ops = default;
        byte* basePtr = (byte*)&ops;
        byte* fieldPtr = (byte*)&ops.Drain;
        Assert.Equal(8, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcFilterOperations_Flush_IsAtOffset16()
    {
        VlcFilterOperations ops = default;
        byte* basePtr = (byte*)&ops;
        byte* fieldPtr = (byte*)&ops.Flush;
        Assert.Equal(16, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcFilterOperations_ChangeViewpoint_IsAtOffset24()
    {
        VlcFilterOperations ops = default;
        byte* basePtr = (byte*)&ops;
        byte* fieldPtr = (byte*)&ops.ChangeViewpoint;
        Assert.Equal(24, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcFilterOperations_VideoMouse_IsAtOffset32()
    {
        VlcFilterOperations ops = default;
        byte* basePtr = (byte*)&ops;
        byte* fieldPtr = (byte*)&ops.VideoMouse;
        Assert.Equal(32, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcFilterOperations_Close_IsAtOffset40()
    {
        VlcFilterOperations ops = default;
        byte* basePtr = (byte*)&ops;
        byte* fieldPtr = (byte*)&ops.Close;
        Assert.Equal(40, (int)(fieldPtr - basePtr));
    }

    #endregion

    #region VlcPicture Layout Tests

    [Fact]
    public unsafe void VlcPicture_Format_IsAtOffset0()
    {
        VlcPicture pic = default;
        byte* basePtr = (byte*)&pic;
        byte* fieldPtr = (byte*)&pic.Format;
        Assert.Equal(0, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcPicture_Plane0_IsAtOffset152()
    {
        // After VlcVideoFormat (152 bytes)
        VlcPicture pic = default;
        byte* basePtr = (byte*)&pic;
        byte* fieldPtr = (byte*)&pic.Plane0;
        Assert.Equal(152, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcPicture_Plane1_IsAtOffset184()
    {
        // Plane0 (152) + VlcPlane size (32) = 184
        VlcPicture pic = default;
        byte* basePtr = (byte*)&pic;
        byte* fieldPtr = (byte*)&pic.Plane1;
        Assert.Equal(184, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcPicture_Plane2_IsAtOffset216()
    {
        VlcPicture pic = default;
        byte* basePtr = (byte*)&pic;
        byte* fieldPtr = (byte*)&pic.Plane2;
        Assert.Equal(216, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcPicture_PlaneCount_IsAtOffset312()
    {
        // After 5 planes: 152 + (5 * 32) = 312
        VlcPicture pic = default;
        byte* basePtr = (byte*)&pic;
        byte* fieldPtr = (byte*)&pic.PlaneCount;
        Assert.Equal(312, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcPicture_Date_IsAtOffset320()
    {
        // After PlaneCount (4) + padding (4)
        VlcPicture pic = default;
        byte* basePtr = (byte*)&pic;
        byte* fieldPtr = (byte*)&pic.Date;
        Assert.Equal(320, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public void VlcPicture_GetPlane_ReturnsCorrectPlane()
    {
        VlcPicture pic = default;
        pic.Plane0 = new VlcPlane { Pixels = (nint)0x1000, Pitch = 100 };
        pic.Plane1 = new VlcPlane { Pixels = (nint)0x2000, Pitch = 200 };
        pic.Plane2 = new VlcPlane { Pixels = (nint)0x3000, Pitch = 300 };

        Assert.Equal((nint)0x1000, pic.GetPlane(0).Pixels);
        Assert.Equal(100, pic.GetPlane(0).Pitch);
        Assert.Equal((nint)0x2000, pic.GetPlane(1).Pixels);
        Assert.Equal(200, pic.GetPlane(1).Pitch);
        Assert.Equal((nint)0x3000, pic.GetPlane(2).Pixels);
        Assert.Equal(300, pic.GetPlane(2).Pitch);
    }

    [Fact]
    public void VlcPicture_GetPlane_ThrowsOnInvalidIndex()
    {
        VlcPicture pic = default;
        Assert.Throws<ArgumentOutOfRangeException>(() => pic.GetPlane(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => pic.GetPlane(5));
        Assert.Throws<ArgumentOutOfRangeException>(() => pic.GetPlane(100));
    }

    #endregion

    #region Memory Marshaling Tests

    [Fact]
    public unsafe void VlcVideoFormat_CanMarshalToNativeMemory()
    {
        VlcVideoFormat fmt = new()
        {
            Chroma = 0x30323449, // "I420"
            Width = 1920,
            Height = 1080,
            VisibleWidth = 1920,
            VisibleHeight = 1080,
            SarNum = 1,
            SarDen = 1,
            FrameRate = 30,
            FrameRateBase = 1
        };

        nint ptr = Marshal.AllocHGlobal(Marshal.SizeOf<VlcVideoFormat>());
        try
        {
            Marshal.StructureToPtr(fmt, ptr, false);
            VlcVideoFormat readBack = Marshal.PtrToStructure<VlcVideoFormat>(ptr);

            Assert.Equal(0x30323449u, readBack.Chroma);
            Assert.Equal(1920u, readBack.Width);
            Assert.Equal(1080u, readBack.Height);
            Assert.Equal(1920u, readBack.VisibleWidth);
            Assert.Equal(1080u, readBack.VisibleHeight);
            Assert.Equal(1u, readBack.SarNum);
            Assert.Equal(1u, readBack.SarDen);
            Assert.Equal(30u, readBack.FrameRate);
            Assert.Equal(1u, readBack.FrameRateBase);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [Fact]
    public unsafe void VlcFilter_OperationsOffset_CanBeWrittenAndRead()
    {
        // This test simulates what the filter does: writing ops pointer at offset 560
        nint filterMemory = Marshal.AllocHGlobal(592);
        try
        {
            // Zero the memory
            for (int i = 0; i < 592; i++)
            {
                Marshal.WriteByte(filterMemory, i, 0);
            }

            // Write a test pointer value at offset 560
            nint testOpsPtr = unchecked((nint)0x12345678ABCDEF00);
            byte* filterBase = (byte*)filterMemory;
            *(nint*)(filterBase + 560) = testOpsPtr;

            // Read it back using struct access
            ref VlcFilter filter = ref *(VlcFilter*)filterMemory;
            Assert.Equal(testOpsPtr, filter.Operations);

            // Also verify via direct memory read
            nint readBack = *(nint*)(filterBase + 560);
            Assert.Equal(testOpsPtr, readBack);
        }
        finally
        {
            Marshal.FreeHGlobal(filterMemory);
        }
    }

    [Fact]
    public unsafe void VlcFilter_FormatInVideoChroma_CanBeReadFromNativeMemory()
    {
        // Simulate reading filter->fmt_in.video.i_chroma
        nint filterMemory = Marshal.AllocHGlobal(592);
        try
        {
            // Zero the memory
            for (int i = 0; i < 592; i++)
            {
                Marshal.WriteByte(filterMemory, i, 0);
            }

            // Write chroma at offset 96 (FormatIn.Video.Chroma)
            // FormatIn is at offset 40, Video is at offset 56 within EsFormat
            // So chroma is at 40 + 56 = 96
            uint testChroma = 0x30323449; // "I420"
            byte* filterBase = (byte*)filterMemory;
            *(uint*)(filterBase + 96) = testChroma;

            // Read it back using struct access
            ref VlcFilter filter = ref *(VlcFilter*)filterMemory;
            Assert.Equal(testChroma, filter.FormatIn.Video.Chroma);
        }
        finally
        {
            Marshal.FreeHGlobal(filterMemory);
        }
    }

    [Fact]
    public unsafe void VlcPicture_PlanesCanBeReadFromNativeMemory()
    {
        // Simulate reading picture plane data
        int pictureSize = Marshal.SizeOf<VlcPicture>();
        nint pictureMemory = Marshal.AllocHGlobal(pictureSize);
        try
        {
            // Zero the memory
            for (int i = 0; i < pictureSize; i++)
            {
                Marshal.WriteByte(pictureMemory, i, 0);
            }

            byte* picBase = (byte*)pictureMemory;

            // Write chroma at offset 0
            *(uint*)(picBase + 0) = 0x30323449; // "I420"

            // Write width at offset 4
            *(uint*)(picBase + 4) = 1280;

            // Write plane0.Pixels at offset 152
            *(nint*)(picBase + 152) = unchecked((nint)0xDEADBEEF);

            // Write plane0.Pitch at offset 164 (Plane0 at 152 + Pitch offset 12)
            *(int*)(picBase + 164) = 1280;

            // Write PlaneCount at offset 312
            *(int*)(picBase + 312) = 3;

            // Read it back using struct access
            ref VlcPicture pic = ref *(VlcPicture*)pictureMemory;

            Assert.Equal(0x30323449u, pic.Format.Chroma);
            Assert.Equal(1280u, pic.Format.Width);
            Assert.Equal(unchecked((nint)0xDEADBEEF), pic.Plane0.Pixels);
            Assert.Equal(1280, pic.Plane0.Pitch);
            Assert.Equal(3, pic.PlaneCount);
        }
        finally
        {
            Marshal.FreeHGlobal(pictureMemory);
        }
    }

    #endregion

    #region Chroma Fourcc Tests

    [Fact]
    public void VlcVideoFormat_Chroma_I420_EqualsExpectedValue()
    {
        // I420 = 0x30323449 = 'I' | '4' << 8 | '2' << 16 | '0' << 24
        uint i420 = (uint)('I' | ('4' << 8) | ('2' << 16) | ('0' << 24));
        Assert.Equal(0x30323449u, i420);
    }

    [Fact]
    public void VlcVideoFormat_Chroma_RV32_EqualsExpectedValue()
    {
        // RV32 = 'R' | 'V' << 8 | '3' << 16 | '2' << 24
        uint rv32 = (uint)('R' | ('V' << 8) | ('3' << 16) | ('2' << 24));
        Assert.Equal(0x32335652u, rv32);
    }

    [Fact]
    public void VlcVideoFormat_Chroma_RGBA_EqualsExpectedValue()
    {
        // RGBA = 'R' | 'G' << 8 | 'B' << 16 | 'A' << 24
        uint rgba = (uint)('R' | ('G' << 8) | ('B' << 16) | ('A' << 24));
        Assert.Equal(0x41424752u, rgba);
    }

    #endregion

    #region Struct Layout Consistency Tests

    [Fact]
    public void VlcEsFormat_VideoOffset_Plus_VideoSize_Equals_BitrateOffset()
    {
        // Video starts at offset 56, video_format_t is 152 bytes
        // So Bitrate should be at 56 + 152 = 208
        int videoOffsetInEsFormat = 56;
        int videoFormatSize = Marshal.SizeOf<VlcVideoFormat>();
        int expectedBitrateOffset = videoOffsetInEsFormat + videoFormatSize;

        Assert.Equal(152, videoFormatSize);
        Assert.Equal(208, expectedBitrateOffset);
    }

    [Fact]
    public void VlcFilter_FormatInOffset_Plus_EsFormatSize_Equals_VideoContextInOffset()
    {
        // FormatIn starts at offset 40, es_format_t is 240 bytes
        // So VideoContextIn should be at 40 + 240 = 280
        int formatInOffset = 40;
        int esFormatSize = Marshal.SizeOf<VlcEsFormat>();
        int expectedVideoContextInOffset = formatInOffset + esFormatSize;

        Assert.Equal(240, esFormatSize);
        Assert.Equal(280, expectedVideoContextInOffset);
    }

    [Fact]
    public void VlcFilter_LayoutIsConsistent()
    {
        // Verify the entire filter layout is consistent with calculated offsets
        int objSize = Marshal.SizeOf<VlcObjectHeader>();
        int esFormatSize = Marshal.SizeOf<VlcEsFormat>();
        int ownerSize = Marshal.SizeOf<VlcFilterOwner>();

        Assert.Equal(24, objSize);
        Assert.Equal(240, esFormatSize);
        Assert.Equal(24, ownerSize);

        // Calculate expected offsets
        int expectedModule = objSize; // 24
        int expectedSys = expectedModule + 8; // 32
        int expectedFormatIn = expectedSys + 8; // 40
        int expectedVideoContextIn = expectedFormatIn + esFormatSize; // 280
        int expectedFormatOut = expectedVideoContextIn + 8; // 288
        int expectedVideoContextOut = expectedFormatOut + esFormatSize; // 528
        int expectedAllowFmtOutChange = expectedVideoContextOut + 8; // 536
        int expectedName = 544; // After 1 byte + 7 padding
        int expectedConfig = expectedName + 8; // 552
        int expectedOperations = expectedConfig + 8; // 560
        int expectedOwner = expectedOperations + 8; // 568

        Assert.Equal(24, expectedModule);
        Assert.Equal(32, expectedSys);
        Assert.Equal(40, expectedFormatIn);
        Assert.Equal(280, expectedVideoContextIn);
        Assert.Equal(288, expectedFormatOut);
        Assert.Equal(528, expectedVideoContextOut);
        Assert.Equal(536, expectedAllowFmtOutChange);
        Assert.Equal(544, expectedName);
        Assert.Equal(552, expectedConfig);
        Assert.Equal(560, expectedOperations);
        Assert.Equal(568, expectedOwner);
    }

    #endregion

    #region VlcFilterOwner Tests

    [Fact]
    public unsafe void VlcFilterOwner_Callbacks_IsAtOffset0()
    {
        VlcFilterOwner owner = default;
        byte* basePtr = (byte*)&owner;
        byte* fieldPtr = (byte*)&owner.Callbacks;
        Assert.Equal(0, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcFilterOwner_GetAttachments_IsAtOffset8()
    {
        VlcFilterOwner owner = default;
        byte* basePtr = (byte*)&owner;
        byte* fieldPtr = (byte*)&owner.GetAttachments;
        Assert.Equal(8, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcFilterOwner_Sys_IsAtOffset16()
    {
        VlcFilterOwner owner = default;
        byte* basePtr = (byte*)&owner;
        byte* fieldPtr = (byte*)&owner.Sys;
        Assert.Equal(16, (int)(fieldPtr - basePtr));
    }

    #endregion

    #region VlcFilterVideoCallbacks Tests

    [Fact]
    public unsafe void VlcFilterVideoCallbacks_BufferNew_IsAtOffset0()
    {
        VlcFilterVideoCallbacks callbacks = default;
        byte* basePtr = (byte*)&callbacks;
        byte* fieldPtr = (byte*)&callbacks.BufferNew;
        Assert.Equal(0, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VlcFilterVideoCallbacks_HoldDevice_IsAtOffset8()
    {
        VlcFilterVideoCallbacks callbacks = default;
        byte* basePtr = (byte*)&callbacks;
        byte* fieldPtr = (byte*)&callbacks.HoldDevice;
        Assert.Equal(8, (int)(fieldPtr - basePtr));
    }

    #endregion

    #region Constants Tests

    [Fact]
    public void VlcFilterConstants_PicturePlaneMax_Is5()
    {
        Assert.Equal(5, VlcFilterConstants.PICTURE_PLANE_MAX);
    }

    #endregion
}
