using System.Runtime.InteropServices;
using VLCLR.Native;
using Xunit;

namespace VLCLR.Tests;

/// <summary>
/// Comprehensive tests for VLC struct layouts.
/// These tests verify that C# struct definitions match VLC 4.x native struct layouts.
/// Critical for correct interop with the VLC video filter API.
/// </summary>
public class VLCFilterTypesTests
{
    #region Struct Size Tests

    [Fact]
    public void VLCPlane_Size_Is32Bytes()
    {
        // plane_t is 32 bytes on 64-bit (28 bytes data + 4 bytes padding)
        Assert.Equal(32, Marshal.SizeOf<VLCPlane>());
    }

    [Fact]
    public void VLCVideoFormat_Size_Is152Bytes()
    {
        // video_format_t is 152 bytes on 64-bit VLC 4.x
        // This was discovered through memory scanning during debugging
        Assert.Equal(152, Marshal.SizeOf<VLCVideoFormat>());
    }

    [Fact]
    public void VLCEsFormat_Size_Is240Bytes()
    {
        // es_format_t = 56 (before union) + 152 (video_format_t) + 32 (after) = 240 bytes
        Assert.Equal(240, Marshal.SizeOf<VLCEsFormat>());
    }

    [Fact]
    public void VLCObjectHeader_Size_Is24Bytes()
    {
        // vlc_object_t header is 24 bytes on 64-bit
        Assert.Equal(24, Marshal.SizeOf<VLCObjectHeader>());
    }

    [Fact]
    public void VLCFilter_Size_Is592Bytes()
    {
        // filter_t is 592 bytes with explicit layout
        Assert.Equal(592, Marshal.SizeOf<VLCFilter>());
    }

    [Fact]
    public void VLCFilterOperations_Size_Is48Bytes()
    {
        // 6 function pointers * 8 bytes = 48 bytes
        Assert.Equal(48, Marshal.SizeOf<VLCFilterOperations>());
    }

    [Fact]
    public void VLCFilterOwner_Size_Is24Bytes()
    {
        // 3 pointers * 8 bytes = 24 bytes
        Assert.Equal(24, Marshal.SizeOf<VLCFilterOwner>());
    }

    [Fact]
    public void VLCFilterVideoCallbacks_Size_Is16Bytes()
    {
        // 2 pointers * 8 bytes = 16 bytes
        Assert.Equal(16, Marshal.SizeOf<VLCFilterVideoCallbacks>());
    }

    [Fact]
    public void VLCPicture_Size_IsAtLeast368Bytes()
    {
        // picture_t: 152 (format) + 160 (5 planes * 32) + other fields
        int size = Marshal.SizeOf<VLCPicture>();
        Assert.True(size >= 368, $"VLCPicture size {size} should be at least 368 bytes");
    }

    #endregion

    #region VLCPlane Field Offset Tests

    [Fact]
    public unsafe void VLCPlane_Pixels_IsAtOffset0()
    {
        VLCPlane plane = default;
        byte* basePtr = (byte*)&plane;
        byte* fieldPtr = (byte*)&plane.Pixels;
        Assert.Equal(0, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCPlane_Lines_IsAtOffset8()
    {
        VLCPlane plane = default;
        byte* basePtr = (byte*)&plane;
        byte* fieldPtr = (byte*)&plane.Lines;
        Assert.Equal(8, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCPlane_Pitch_IsAtOffset12()
    {
        VLCPlane plane = default;
        byte* basePtr = (byte*)&plane;
        byte* fieldPtr = (byte*)&plane.Pitch;
        Assert.Equal(12, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCPlane_PixelPitch_IsAtOffset16()
    {
        VLCPlane plane = default;
        byte* basePtr = (byte*)&plane;
        byte* fieldPtr = (byte*)&plane.PixelPitch;
        Assert.Equal(16, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCPlane_VisibleLines_IsAtOffset20()
    {
        VLCPlane plane = default;
        byte* basePtr = (byte*)&plane;
        byte* fieldPtr = (byte*)&plane.VisibleLines;
        Assert.Equal(20, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCPlane_VisiblePitch_IsAtOffset24()
    {
        VLCPlane plane = default;
        byte* basePtr = (byte*)&plane;
        byte* fieldPtr = (byte*)&plane.VisiblePitch;
        Assert.Equal(24, (int)(fieldPtr - basePtr));
    }

    #endregion

    #region VLCVideoFormat Field Offset Tests

    [Fact]
    public unsafe void VLCVideoFormat_Chroma_IsAtOffset0()
    {
        VLCVideoFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Chroma;
        Assert.Equal(0, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCVideoFormat_Width_IsAtOffset4()
    {
        VLCVideoFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Width;
        Assert.Equal(4, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCVideoFormat_Height_IsAtOffset8()
    {
        VLCVideoFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Height;
        Assert.Equal(8, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCVideoFormat_Palette_IsAtOffset48()
    {
        VLCVideoFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Palette;
        Assert.Equal(48, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCVideoFormat_Orientation_IsAtOffset56()
    {
        VLCVideoFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Orientation;
        Assert.Equal(56, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCVideoFormat_ProjectionMode_IsAtOffset88()
    {
        VLCVideoFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.ProjectionMode;
        Assert.Equal(88, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCVideoFormat_PoseYaw_IsAtOffset92()
    {
        VLCVideoFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.PoseYaw;
        Assert.Equal(92, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCVideoFormat_CubemapPadding_IsAtOffset140()
    {
        VLCVideoFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.CubemapPadding;
        Assert.Equal(140, (int)(fieldPtr - basePtr));
    }

    #endregion

    #region VLCEsFormat Field Offset Tests

    [Fact]
    public unsafe void VLCEsFormat_Category_IsAtOffset0()
    {
        VLCEsFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Category;
        Assert.Equal(0, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCEsFormat_Codec_IsAtOffset4()
    {
        VLCEsFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Codec;
        Assert.Equal(4, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCEsFormat_Language_IsAtOffset24()
    {
        VLCEsFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Language;
        Assert.Equal(24, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCEsFormat_Description_IsAtOffset32()
    {
        VLCEsFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Description;
        Assert.Equal(32, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCEsFormat_ExtraLanguages_IsAtOffset48()
    {
        VLCEsFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.ExtraLanguages;
        Assert.Equal(48, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCEsFormat_Video_IsAtOffset56()
    {
        VLCEsFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Video;
        Assert.Equal(56, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCEsFormat_VideoChroma_IsAtOffset56()
    {
        // Video.Chroma should be at offset 56 (start of Video struct)
        VLCEsFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Video.Chroma;
        Assert.Equal(56, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCEsFormat_VideoWidth_IsAtOffset60()
    {
        VLCEsFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Video.Width;
        Assert.Equal(60, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCEsFormat_Bitrate_IsAtOffset208()
    {
        // After Video (56 + 152 = 208)
        VLCEsFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Bitrate;
        Assert.Equal(208, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCEsFormat_Profile_IsAtOffset212()
    {
        VLCEsFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Profile;
        Assert.Equal(212, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCEsFormat_Level_IsAtOffset216()
    {
        VLCEsFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Level;
        Assert.Equal(216, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCEsFormat_Packetized_IsAtOffset220()
    {
        VLCEsFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Packetized;
        Assert.Equal(220, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCEsFormat_ExtraSize_IsAtOffset224()
    {
        VLCEsFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.ExtraSize;
        Assert.Equal(224, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCEsFormat_Extra_IsAtOffset232()
    {
        VLCEsFormat fmt = default;
        byte* basePtr = (byte*)&fmt;
        byte* fieldPtr = (byte*)&fmt.Extra;
        Assert.Equal(232, (int)(fieldPtr - basePtr));
    }

    #endregion

    #region VLCObjectHeader Field Offset Tests

    [Fact]
    public unsafe void VLCObjectHeader_Logger_IsAtOffset0()
    {
        VLCObjectHeader header = default;
        byte* basePtr = (byte*)&header;
        byte* fieldPtr = (byte*)&header.Logger;
        Assert.Equal(0, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCObjectHeader_PrivOrObj_IsAtOffset8()
    {
        VLCObjectHeader header = default;
        byte* basePtr = (byte*)&header;
        byte* fieldPtr = (byte*)&header.PrivOrObj;
        Assert.Equal(8, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCObjectHeader_NoInteract_IsAtOffset16()
    {
        VLCObjectHeader header = default;
        byte* basePtr = (byte*)&header;
        byte* fieldPtr = (byte*)&header.NoInteract;
        Assert.Equal(16, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCObjectHeader_Force_IsAtOffset17()
    {
        VLCObjectHeader header = default;
        byte* basePtr = (byte*)&header;
        byte* fieldPtr = (byte*)&header.Force;
        Assert.Equal(17, (int)(fieldPtr - basePtr));
    }

    #endregion

    #region VLCFilter Explicit Layout Field Offset Tests

    [Fact]
    public unsafe void VLCFilter_Obj_IsAtOffset0()
    {
        VLCFilter filter = default;
        byte* basePtr = (byte*)&filter;
        byte* fieldPtr = (byte*)&filter.Obj;
        Assert.Equal(0, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCFilter_Module_IsAtOffset24()
    {
        VLCFilter filter = default;
        byte* basePtr = (byte*)&filter;
        byte* fieldPtr = (byte*)&filter.Module;
        Assert.Equal(24, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCFilter_Sys_IsAtOffset32()
    {
        VLCFilter filter = default;
        byte* basePtr = (byte*)&filter;
        byte* fieldPtr = (byte*)&filter.Sys;
        Assert.Equal(32, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCFilter_FormatIn_IsAtOffset40()
    {
        VLCFilter filter = default;
        byte* basePtr = (byte*)&filter;
        byte* fieldPtr = (byte*)&filter.FormatIn;
        Assert.Equal(40, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCFilter_FormatIn_VideoChroma_IsAtOffset96()
    {
        // FormatIn (40) + Video offset within EsFormat (56) = 96
        VLCFilter filter = default;
        byte* basePtr = (byte*)&filter;
        byte* fieldPtr = (byte*)&filter.FormatIn.Video.Chroma;
        Assert.Equal(96, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCFilter_FormatIn_VideoWidth_IsAtOffset100()
    {
        VLCFilter filter = default;
        byte* basePtr = (byte*)&filter;
        byte* fieldPtr = (byte*)&filter.FormatIn.Video.Width;
        Assert.Equal(100, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCFilter_VideoContextIn_IsAtOffset280()
    {
        // FormatIn (40) + VLCEsFormat size (240) = 280
        VLCFilter filter = default;
        byte* basePtr = (byte*)&filter;
        byte* fieldPtr = (byte*)&filter.VideoContextIn;
        Assert.Equal(280, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCFilter_FormatOut_IsAtOffset288()
    {
        VLCFilter filter = default;
        byte* basePtr = (byte*)&filter;
        byte* fieldPtr = (byte*)&filter.FormatOut;
        Assert.Equal(288, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCFilter_VideoContextOut_IsAtOffset528()
    {
        // FormatOut (288) + VLCEsFormat size (240) = 528
        VLCFilter filter = default;
        byte* basePtr = (byte*)&filter;
        byte* fieldPtr = (byte*)&filter.VideoContextOut;
        Assert.Equal(528, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCFilter_AllowFormatOutChange_IsAtOffset536()
    {
        VLCFilter filter = default;
        byte* basePtr = (byte*)&filter;
        byte* fieldPtr = (byte*)&filter.AllowFormatOutChange;
        Assert.Equal(536, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCFilter_Name_IsAtOffset544()
    {
        VLCFilter filter = default;
        byte* basePtr = (byte*)&filter;
        byte* fieldPtr = (byte*)&filter.Name;
        Assert.Equal(544, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCFilter_Config_IsAtOffset552()
    {
        VLCFilter filter = default;
        byte* basePtr = (byte*)&filter;
        byte* fieldPtr = (byte*)&filter.Config;
        Assert.Equal(552, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCFilter_Operations_IsAtOffset560()
    {
        // This is the critical offset that caused the assertion failure
        VLCFilter filter = default;
        byte* basePtr = (byte*)&filter;
        byte* fieldPtr = (byte*)&filter.Operations;
        Assert.Equal(560, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCFilter_Owner_IsAtOffset568()
    {
        VLCFilter filter = default;
        byte* basePtr = (byte*)&filter;
        byte* fieldPtr = (byte*)&filter.Owner;
        Assert.Equal(568, (int)(fieldPtr - basePtr));
    }

    #endregion

    #region VLCFilterOperations Field Offset Tests

    [Fact]
    public unsafe void VLCFilterOperations_FilterVideo_IsAtOffset0()
    {
        VLCFilterOperations ops = default;
        byte* basePtr = (byte*)&ops;
        byte* fieldPtr = (byte*)&ops.FilterVideo;
        Assert.Equal(0, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCFilterOperations_Drain_IsAtOffset8()
    {
        VLCFilterOperations ops = default;
        byte* basePtr = (byte*)&ops;
        byte* fieldPtr = (byte*)&ops.Drain;
        Assert.Equal(8, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCFilterOperations_Flush_IsAtOffset16()
    {
        VLCFilterOperations ops = default;
        byte* basePtr = (byte*)&ops;
        byte* fieldPtr = (byte*)&ops.Flush;
        Assert.Equal(16, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCFilterOperations_ChangeViewpoint_IsAtOffset24()
    {
        VLCFilterOperations ops = default;
        byte* basePtr = (byte*)&ops;
        byte* fieldPtr = (byte*)&ops.ChangeViewpoint;
        Assert.Equal(24, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCFilterOperations_VideoMouse_IsAtOffset32()
    {
        VLCFilterOperations ops = default;
        byte* basePtr = (byte*)&ops;
        byte* fieldPtr = (byte*)&ops.VideoMouse;
        Assert.Equal(32, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCFilterOperations_Close_IsAtOffset40()
    {
        VLCFilterOperations ops = default;
        byte* basePtr = (byte*)&ops;
        byte* fieldPtr = (byte*)&ops.Close;
        Assert.Equal(40, (int)(fieldPtr - basePtr));
    }

    #endregion

    #region VLCPicture Layout Tests

    [Fact]
    public unsafe void VLCPicture_Format_IsAtOffset0()
    {
        VLCPicture pic = default;
        byte* basePtr = (byte*)&pic;
        byte* fieldPtr = (byte*)&pic.Format;
        Assert.Equal(0, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCPicture_Plane0_IsAtOffset152()
    {
        // After VLCVideoFormat (152 bytes)
        VLCPicture pic = default;
        byte* basePtr = (byte*)&pic;
        byte* fieldPtr = (byte*)&pic.Plane0;
        Assert.Equal(152, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCPicture_Plane1_IsAtOffset184()
    {
        // Plane0 (152) + VLCPlane size (32) = 184
        VLCPicture pic = default;
        byte* basePtr = (byte*)&pic;
        byte* fieldPtr = (byte*)&pic.Plane1;
        Assert.Equal(184, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCPicture_Plane2_IsAtOffset216()
    {
        VLCPicture pic = default;
        byte* basePtr = (byte*)&pic;
        byte* fieldPtr = (byte*)&pic.Plane2;
        Assert.Equal(216, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCPicture_PlaneCount_IsAtOffset312()
    {
        // After 5 planes: 152 + (5 * 32) = 312
        VLCPicture pic = default;
        byte* basePtr = (byte*)&pic;
        byte* fieldPtr = (byte*)&pic.PlaneCount;
        Assert.Equal(312, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCPicture_Date_IsAtOffset320()
    {
        // After PlaneCount (4) + padding (4)
        VLCPicture pic = default;
        byte* basePtr = (byte*)&pic;
        byte* fieldPtr = (byte*)&pic.Date;
        Assert.Equal(320, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public void VLCPicture_GetPlane_ReturnsCorrectPlane()
    {
        VLCPicture pic = default;
        pic.Plane0 = new VLCPlane { Pixels = (nint)0x1000, Pitch = 100 };
        pic.Plane1 = new VLCPlane { Pixels = (nint)0x2000, Pitch = 200 };
        pic.Plane2 = new VLCPlane { Pixels = (nint)0x3000, Pitch = 300 };

        Assert.Equal((nint)0x1000, pic.GetPlane(0).Pixels);
        Assert.Equal(100, pic.GetPlane(0).Pitch);
        Assert.Equal((nint)0x2000, pic.GetPlane(1).Pixels);
        Assert.Equal(200, pic.GetPlane(1).Pitch);
        Assert.Equal((nint)0x3000, pic.GetPlane(2).Pixels);
        Assert.Equal(300, pic.GetPlane(2).Pitch);
    }

    [Fact]
    public void VLCPicture_GetPlane_ThrowsOnInvalidIndex()
    {
        VLCPicture pic = default;
        Assert.Throws<ArgumentOutOfRangeException>(() => pic.GetPlane(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => pic.GetPlane(5));
        Assert.Throws<ArgumentOutOfRangeException>(() => pic.GetPlane(100));
    }

    #endregion

    #region Memory Marshaling Tests

    [Fact]
    public unsafe void VLCVideoFormat_CanMarshalToNativeMemory()
    {
        VLCVideoFormat fmt = new()
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

        nint ptr = Marshal.AllocHGlobal(Marshal.SizeOf<VLCVideoFormat>());
        try
        {
            Marshal.StructureToPtr(fmt, ptr, false);
            VLCVideoFormat readBack = Marshal.PtrToStructure<VLCVideoFormat>(ptr);

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
    public unsafe void VLCFilter_OperationsOffset_CanBeWrittenAndRead()
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
            ref VLCFilter filter = ref *(VLCFilter*)filterMemory;
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
    public unsafe void VLCFilter_FormatInVideoChroma_CanBeReadFromNativeMemory()
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
            ref VLCFilter filter = ref *(VLCFilter*)filterMemory;
            Assert.Equal(testChroma, filter.FormatIn.Video.Chroma);
        }
        finally
        {
            Marshal.FreeHGlobal(filterMemory);
        }
    }

    [Fact]
    public unsafe void VLCPicture_PlanesCanBeReadFromNativeMemory()
    {
        // Simulate reading picture plane data
        int pictureSize = Marshal.SizeOf<VLCPicture>();
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
            ref VLCPicture pic = ref *(VLCPicture*)pictureMemory;

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
    public void VLCVideoFormat_Chroma_I420_EqualsExpectedValue()
    {
        // I420 = 0x30323449 = 'I' | '4' << 8 | '2' << 16 | '0' << 24
        uint i420 = (uint)('I' | ('4' << 8) | ('2' << 16) | ('0' << 24));
        Assert.Equal(0x30323449u, i420);
    }

    [Fact]
    public void VLCVideoFormat_Chroma_RV32_EqualsExpectedValue()
    {
        // RV32 = 'R' | 'V' << 8 | '3' << 16 | '2' << 24
        uint rv32 = (uint)('R' | ('V' << 8) | ('3' << 16) | ('2' << 24));
        Assert.Equal(0x32335652u, rv32);
    }

    [Fact]
    public void VLCVideoFormat_Chroma_RGBA_EqualsExpectedValue()
    {
        // RGBA = 'R' | 'G' << 8 | 'B' << 16 | 'A' << 24
        uint rgba = (uint)('R' | ('G' << 8) | ('B' << 16) | ('A' << 24));
        Assert.Equal(0x41424752u, rgba);
    }

    #endregion

    #region Struct Layout Consistency Tests

    [Fact]
    public void VLCEsFormat_VideoOffset_Plus_VideoSize_Equals_BitrateOffset()
    {
        // Video starts at offset 56, video_format_t is 152 bytes
        // So Bitrate should be at 56 + 152 = 208
        int videoOffsetInEsFormat = 56;
        int videoFormatSize = Marshal.SizeOf<VLCVideoFormat>();
        int expectedBitrateOffset = videoOffsetInEsFormat + videoFormatSize;

        Assert.Equal(152, videoFormatSize);
        Assert.Equal(208, expectedBitrateOffset);
    }

    [Fact]
    public void VLCFilter_FormatInOffset_Plus_EsFormatSize_Equals_VideoContextInOffset()
    {
        // FormatIn starts at offset 40, es_format_t is 240 bytes
        // So VideoContextIn should be at 40 + 240 = 280
        int formatInOffset = 40;
        int esFormatSize = Marshal.SizeOf<VLCEsFormat>();
        int expectedVideoContextInOffset = formatInOffset + esFormatSize;

        Assert.Equal(240, esFormatSize);
        Assert.Equal(280, expectedVideoContextInOffset);
    }

    [Fact]
    public void VLCFilter_LayoutIsConsistent()
    {
        // Verify the entire filter layout is consistent with calculated offsets
        int objSize = Marshal.SizeOf<VLCObjectHeader>();
        int esFormatSize = Marshal.SizeOf<VLCEsFormat>();
        int ownerSize = Marshal.SizeOf<VLCFilterOwner>();

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

    #region VLCFilterOwner Tests

    [Fact]
    public unsafe void VLCFilterOwner_Callbacks_IsAtOffset0()
    {
        VLCFilterOwner owner = default;
        byte* basePtr = (byte*)&owner;
        byte* fieldPtr = (byte*)&owner.Callbacks;
        Assert.Equal(0, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCFilterOwner_GetAttachments_IsAtOffset8()
    {
        VLCFilterOwner owner = default;
        byte* basePtr = (byte*)&owner;
        byte* fieldPtr = (byte*)&owner.GetAttachments;
        Assert.Equal(8, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCFilterOwner_Sys_IsAtOffset16()
    {
        VLCFilterOwner owner = default;
        byte* basePtr = (byte*)&owner;
        byte* fieldPtr = (byte*)&owner.Sys;
        Assert.Equal(16, (int)(fieldPtr - basePtr));
    }

    #endregion

    #region VLCFilterVideoCallbacks Tests

    [Fact]
    public unsafe void VLCFilterVideoCallbacks_BufferNew_IsAtOffset0()
    {
        VLCFilterVideoCallbacks callbacks = default;
        byte* basePtr = (byte*)&callbacks;
        byte* fieldPtr = (byte*)&callbacks.BufferNew;
        Assert.Equal(0, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCFilterVideoCallbacks_HoldDevice_IsAtOffset8()
    {
        VLCFilterVideoCallbacks callbacks = default;
        byte* basePtr = (byte*)&callbacks;
        byte* fieldPtr = (byte*)&callbacks.HoldDevice;
        Assert.Equal(8, (int)(fieldPtr - basePtr));
    }

    #endregion

    #region Constants Tests

    [Fact]
    public void VLCFilterConstants_PicturePlaneMax_Is5()
    {
        Assert.Equal(5, VLCFilterConstants.PICTURE_PLANE_MAX);
    }

    #endregion
}
