using System.Runtime.InteropServices;
using VLCLR.Native;
using Xunit;

namespace VLCLR.Tests;

/// <summary>
/// Tests for VLCSubpicture struct layout.
/// Verifies that C# struct definition matches VLC 4.x subpicture_t layout.
/// </summary>
public class VLCSubpictureTests
{
    #region Struct Size Tests

    [Fact]
    public void VLCSubpicture_Size_Is104Bytes()
    {
        // subpicture_t is 104 bytes on 64-bit
        // i_channel (8) + i_order (8) + p_next (8) + regions (16) + i_start (8) + i_stop (8)
        // + 3 bytes flags + 1 padding + width (4) + height (4) + alpha (4) + 4 padding
        // + updater sys/ops (16) + p_private (8) = 104 bytes
        Assert.Equal(104, Marshal.SizeOf<VLCSubpicture>());
    }

    [Fact]
    public void VLCSubpictureUpdater_Size_Is16Bytes()
    {
        // subpicture_updater_t is 16 bytes on 64-bit (2 pointers)
        Assert.Equal(16, Marshal.SizeOf<VLCSubpictureUpdater>());
    }

    [Fact]
    public void VLCSubpictureUpdaterOps_Size_Is16Bytes()
    {
        // vlc_spu_updater_ops is 16 bytes on 64-bit (2 function pointers)
        Assert.Equal(16, Marshal.SizeOf<VLCSubpictureUpdaterOps>());
    }

    #endregion

    #region VLCSubpicture Field Offset Tests

    [Fact]
    public unsafe void VLCSubpicture_Channel_IsAtOffset0()
    {
        VLCSubpicture subpic = default;
        byte* basePtr = (byte*)&subpic;
        byte* fieldPtr = (byte*)&subpic.Channel;
        Assert.Equal(0, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCSubpicture_Order_IsAtOffset8()
    {
        VLCSubpicture subpic = default;
        byte* basePtr = (byte*)&subpic;
        byte* fieldPtr = (byte*)&subpic.Order;
        Assert.Equal(8, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCSubpicture_Next_IsAtOffset16()
    {
        VLCSubpicture subpic = default;
        byte* basePtr = (byte*)&subpic;
        byte* fieldPtr = (byte*)&subpic.Next;
        Assert.Equal(16, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCSubpicture_RegionsPrev_IsAtOffset24()
    {
        VLCSubpicture subpic = default;
        byte* basePtr = (byte*)&subpic;
        byte* fieldPtr = (byte*)&subpic.RegionsPrev;
        Assert.Equal(24, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCSubpicture_RegionsNext_IsAtOffset32()
    {
        VLCSubpicture subpic = default;
        byte* basePtr = (byte*)&subpic;
        byte* fieldPtr = (byte*)&subpic.RegionsNext;
        Assert.Equal(32, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCSubpicture_Start_IsAtOffset40()
    {
        VLCSubpicture subpic = default;
        byte* basePtr = (byte*)&subpic;
        byte* fieldPtr = (byte*)&subpic.Start;
        Assert.Equal(40, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCSubpicture_Stop_IsAtOffset48()
    {
        VLCSubpicture subpic = default;
        byte* basePtr = (byte*)&subpic;
        byte* fieldPtr = (byte*)&subpic.Stop;
        Assert.Equal(48, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCSubpicture_IsEphemer_IsAtOffset56()
    {
        VLCSubpicture subpic = default;
        byte* basePtr = (byte*)&subpic;
        byte* fieldPtr = (byte*)&subpic.IsEphemer;
        Assert.Equal(56, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCSubpicture_IsFade_IsAtOffset57()
    {
        VLCSubpicture subpic = default;
        byte* basePtr = (byte*)&subpic;
        byte* fieldPtr = (byte*)&subpic.IsFade;
        Assert.Equal(57, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCSubpicture_IsSubtitle_IsAtOffset58()
    {
        VLCSubpicture subpic = default;
        byte* basePtr = (byte*)&subpic;
        byte* fieldPtr = (byte*)&subpic.IsSubtitle;
        Assert.Equal(58, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCSubpicture_OriginalPictureWidth_IsAtOffset60()
    {
        // After 3 bytes flags + 1 byte padding = offset 60
        VLCSubpicture subpic = default;
        byte* basePtr = (byte*)&subpic;
        byte* fieldPtr = (byte*)&subpic.OriginalPictureWidth;
        Assert.Equal(60, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCSubpicture_OriginalPictureHeight_IsAtOffset64()
    {
        VLCSubpicture subpic = default;
        byte* basePtr = (byte*)&subpic;
        byte* fieldPtr = (byte*)&subpic.OriginalPictureHeight;
        Assert.Equal(64, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCSubpicture_Alpha_IsAtOffset68()
    {
        VLCSubpicture subpic = default;
        byte* basePtr = (byte*)&subpic;
        byte* fieldPtr = (byte*)&subpic.Alpha;
        Assert.Equal(68, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCSubpicture_UpdaterSys_IsAtOffset80()
    {
        // After Alpha (68 + 4 = 72), padded to 8-byte alignment = 80 (or 76 + 4 padding)
        VLCSubpicture subpic = default;
        byte* basePtr = (byte*)&subpic;
        byte* fieldPtr = (byte*)&subpic.UpdaterSys;
        Assert.Equal(80, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCSubpicture_UpdaterOps_IsAtOffset88()
    {
        VLCSubpicture subpic = default;
        byte* basePtr = (byte*)&subpic;
        byte* fieldPtr = (byte*)&subpic.UpdaterOps;
        Assert.Equal(88, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCSubpicture_Private_IsAtOffset96()
    {
        VLCSubpicture subpic = default;
        byte* basePtr = (byte*)&subpic;
        byte* fieldPtr = (byte*)&subpic.Private;
        Assert.Equal(96, (int)(fieldPtr - basePtr));
    }

    #endregion

    #region VLCTick Constants Tests

    [Fact]
    public void VLCTick_Invalid_IsLongMinValue()
    {
        Assert.Equal(long.MinValue, VLCTick.Invalid);
    }

    [Fact]
    public void VLCTick_Second_IsOneMillion()
    {
        Assert.Equal(1_000_000L, VLCTick.Second);
    }

    [Fact]
    public void VLCTick_Millisecond_IsOneThousand()
    {
        Assert.Equal(1_000L, VLCTick.Millisecond);
    }

    #endregion

    #region Memory Marshaling Tests

    [Fact]
    public unsafe void VLCSubpicture_CanMarshalToNativeMemory()
    {
        VLCSubpicture subpic = new()
        {
            Channel = 1,
            Order = 100,
            Next = unchecked((nint)0x1000),
            Start = 5 * VLCTick.Second,
            Stop = 10 * VLCTick.Second,
            IsEphemer = 0,
            IsFade = 1,
            IsSubtitle = 1,
            OriginalPictureWidth = 1920,
            OriginalPictureHeight = 1080,
            Alpha = 255,
            UpdaterSys = unchecked((nint)0x2000),
            UpdaterOps = unchecked((nint)0x3000),
            Private = unchecked((nint)0x4000)
        };

        nint ptr = Marshal.AllocHGlobal(Marshal.SizeOf<VLCSubpicture>());
        try
        {
            Marshal.StructureToPtr(subpic, ptr, false);
            VLCSubpicture readBack = Marshal.PtrToStructure<VLCSubpicture>(ptr);

            Assert.Equal(1, readBack.Channel);
            Assert.Equal(100, readBack.Order);
            Assert.Equal(unchecked((nint)0x1000), readBack.Next);
            Assert.Equal(5 * VLCTick.Second, readBack.Start);
            Assert.Equal(10 * VLCTick.Second, readBack.Stop);
            Assert.Equal(0, readBack.IsEphemer);
            Assert.Equal(1, readBack.IsFade);
            Assert.Equal(1, readBack.IsSubtitle);
            Assert.Equal(1920u, readBack.OriginalPictureWidth);
            Assert.Equal(1080u, readBack.OriginalPictureHeight);
            Assert.Equal(255, readBack.Alpha);
            Assert.Equal(unchecked((nint)0x2000), readBack.UpdaterSys);
            Assert.Equal(unchecked((nint)0x3000), readBack.UpdaterOps);
            Assert.Equal(unchecked((nint)0x4000), readBack.Private);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [Fact]
    public unsafe void VLCSubpicture_FieldsCanBeWrittenAndReadFromNativeMemory()
    {
        nint subpicMemory = Marshal.AllocHGlobal(104);
        try
        {
            // Zero the memory
            for (int i = 0; i < 104; i++)
            {
                Marshal.WriteByte(subpicMemory, i, 0);
            }

            byte* subpicBase = (byte*)subpicMemory;

            // Write Channel at offset 0
            *(long*)(subpicBase + 0) = 42;

            // Write Order at offset 8
            *(long*)(subpicBase + 8) = 1234;

            // Write Start at offset 40
            *(long*)(subpicBase + 40) = 3 * VLCTick.Second;

            // Write Stop at offset 48
            *(long*)(subpicBase + 48) = 6 * VLCTick.Second;

            // Write IsSubtitle at offset 58
            *(byte*)(subpicBase + 58) = 1;

            // Write OriginalPictureWidth at offset 60
            *(uint*)(subpicBase + 60) = 1280;

            // Write OriginalPictureHeight at offset 64
            *(uint*)(subpicBase + 64) = 720;

            // Read back using struct access
            ref VLCSubpicture subpic = ref *(VLCSubpicture*)subpicMemory;

            Assert.Equal(42, subpic.Channel);
            Assert.Equal(1234, subpic.Order);
            Assert.Equal(3 * VLCTick.Second, subpic.Start);
            Assert.Equal(6 * VLCTick.Second, subpic.Stop);
            Assert.Equal(1, subpic.IsSubtitle);
            Assert.Equal(1280u, subpic.OriginalPictureWidth);
            Assert.Equal(720u, subpic.OriginalPictureHeight);
        }
        finally
        {
            Marshal.FreeHGlobal(subpicMemory);
        }
    }

    [Fact]
    public unsafe void VLCSubpicture_TimingFieldsWorkCorrectly()
    {
        VLCSubpicture subpic = new()
        {
            Start = 0,
            Stop = VLCTick.Invalid
        };

        // Ephemeral subtitle (display until next one)
        Assert.Equal(0, subpic.Start);
        Assert.Equal(VLCTick.Invalid, subpic.Stop);

        // Timed subtitle
        subpic.Start = 5 * VLCTick.Second + 500 * VLCTick.Millisecond;  // 5.5 seconds
        subpic.Stop = 8 * VLCTick.Second;  // 8 seconds

        Assert.Equal(5_500_000L, subpic.Start);
        Assert.Equal(8_000_000L, subpic.Stop);
    }

    #endregion

    #region VLCSubpictureUpdater Tests

    [Fact]
    public unsafe void VLCSubpictureUpdater_Sys_IsAtOffset0()
    {
        VLCSubpictureUpdater updater = default;
        byte* basePtr = (byte*)&updater;
        byte* fieldPtr = (byte*)&updater.Sys;
        Assert.Equal(0, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCSubpictureUpdater_Ops_IsAtOffset8()
    {
        VLCSubpictureUpdater updater = default;
        byte* basePtr = (byte*)&updater;
        byte* fieldPtr = (byte*)&updater.Ops;
        Assert.Equal(8, (int)(fieldPtr - basePtr));
    }

    #endregion

    #region VLCSubpictureUpdaterOps Tests

    [Fact]
    public unsafe void VLCSubpictureUpdaterOps_Update_IsAtOffset0()
    {
        VLCSubpictureUpdaterOps ops = default;
        byte* basePtr = (byte*)&ops;
        byte* fieldPtr = (byte*)&ops.Update;
        Assert.Equal(0, (int)(fieldPtr - basePtr));
    }

    [Fact]
    public unsafe void VLCSubpictureUpdaterOps_Destroy_IsAtOffset8()
    {
        VLCSubpictureUpdaterOps ops = default;
        byte* basePtr = (byte*)&ops;
        byte* fieldPtr = (byte*)&ops.Destroy;
        Assert.Equal(8, (int)(fieldPtr - basePtr));
    }

    #endregion
}
