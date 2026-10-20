using InlineParity.Native;
using VLCLR.Native;
using VLCLR.Plugin;

namespace InlineParity;

/// <summary>VLCCore.PictureRelease and VLCFilterContext.NewPicture against picture_Release and filter_NewPicture.</summary>
public unsafe class PictureParityTests
{
    private static IEnumerable<nuint> RefCounts()
    {
        foreach (ulong v in new ulong[] { 1, 2, 3, 4, 7, 100, 0x7FFFFFFF, 0x80000000, 0x80000001, 0xFFFFFFFF })
        {
            yield return (nuint)v;
        }

        if (nint.Size == 8)
        {
            // vlc_atomic_rc_t is a uintptr_t: a zero low half borrows from the high half.
            foreach (ulong v in new ulong[] { 0x1_0000_0000, 0x1_0000_0001, 0xFFFF_FFFF_0000_0000, 0xFFFF_FFFF_FFFF_FFFF })
            {
                yield return (nuint)v;
            }
        }

        Random random = Cases.NewRandom(30);
        for (int i = 0; i < Cases.RandomCount; i++)
        {
            yield return (nuint)random.NextInt64(1, 1L << random.Next(2, nint.Size == 8 ? 63 : 33));
        }
    }

    [SkippableFact]
    public void PictureHold_increments_and_returns_the_picture_like_picture_Hold()
    {
        ParityLibrary.Require();

        nint picture = ParityNative.parity_picture_new(1);
        try
        {
            foreach (nuint refs in RefCounts())
            {
                ParityNative.parity_picture_set_refs(picture, refs);
                var (cHeld, cLog) = Harness.Run(null, () => ParityNative.parity_picture_Hold(picture));
                nuint cRefs = ParityNative.parity_picture_refs(picture);

                ParityNative.parity_picture_set_refs(picture, refs);
                var (managedHeld, managedLog) = Harness.Run(null, () => VLCCore.PictureHold(picture));
                nuint managedRefs = ParityNative.parity_picture_refs(picture);

                ParityAssert.Equal(cHeld, managedHeld, $"returned picture from {refs}");
                ParityAssert.Equal(cRefs, managedRefs, $"refs after hold from {refs}");
                ParityAssert.Same(cLog, managedLog, $"hold from {refs}");
                ParityAssert.Equal(refs + 1, managedRefs, $"refs after hold from {refs}");
            }
        }
        finally
        {
            ParityNative.parity_picture_free(picture);
        }
    }

    [SkippableFact]
    public void Documented_difference_PictureHold_ignores_a_null_picture()
    {
        ParityLibrary.Require();

        // picture_Hold(NULL) dereferences NULL; the managed version returns NULL.
        var (held, log) = Harness.Run(null, () => VLCCore.PictureHold(0));

        Assert.Equal(0, held);
        Assert.Equal(0, log.DestroyCalls);
    }

    [SkippableFact]
    public void PictureRelease_decrements_and_destroys_at_zero_like_picture_Release()
    {
        ParityLibrary.Require();

        nint picture = ParityNative.parity_picture_new(1);
        try
        {
            foreach (nuint refs in RefCounts())
            {
                ParityNative.parity_picture_set_refs(picture, refs);
                var (_, cLog) = Harness.Run<int>(null, () => { ParityNative.parity_picture_Release(picture); return 0; });
                nuint cRefs = ParityNative.parity_picture_refs(picture);

                ParityNative.parity_picture_set_refs(picture, refs);
                var (_, managedLog) = Harness.Run<int>(null, () => { VLCCore.PictureRelease(picture); return 0; });
                nuint managedRefs = ParityNative.parity_picture_refs(picture);

                ParityAssert.Equal(cRefs, managedRefs, $"refs after release from {refs}");
                ParityAssert.Same(cLog, managedLog, $"release from {refs}");
                ParityAssert.Equal(refs == 1 ? 1 : 0, managedLog.DestroyCalls, $"destroy calls from {refs}");
            }
        }
        finally
        {
            ParityNative.parity_picture_free(picture);
        }
    }

    [SkippableFact]
    public void PictureRelease_destroys_exactly_once_after_the_last_of_several_releases()
    {
        ParityLibrary.Require();

        nint picture = ParityNative.parity_picture_new(1);
        try
        {
            for (uint refs = 1; refs <= 5; refs++)
            {
                ParityNative.parity_picture_set_refs(picture, refs);
                ParityNative.parity_reset();
                for (uint i = 0; i < refs; i++)
                {
                    VLCCore.PictureRelease(picture);
                    ParityAssert.Equal(i + 1 == refs ? 1 : 0, ParityNative.Snapshot().DestroyCalls, $"destroy calls after release {i + 1} of {refs}");
                }

                ParityAssert.Equal(picture, ParityNative.Snapshot().DestroyArg, $"destroyed picture for {refs} refs");
                ParityAssert.Equal((nuint)0, ParityNative.parity_picture_refs(picture), $"refs left for {refs}");
            }
        }
        finally
        {
            ParityNative.parity_picture_free(picture);
        }
    }

    [SkippableFact]
    public void Documented_difference_PictureRelease_ignores_a_null_picture()
    {
        ParityLibrary.Require();

        // picture_Release(NULL) dereferences NULL; the managed version is a safe no-op.
        var (_, log) = Harness.Run<int>(null, () => { VLCCore.PictureRelease(0); return 0; });

        Assert.Equal(0, log.DestroyCalls);
    }

    // mode: 0 owner.video NULL, 1 buffer_new NULL, 2 buffer_new returns a picture, 3 buffer_new returns NULL.
    public static IEnumerable<object[]> FilterModes()
    {
        for (int mode = 0; mode <= 3; mode++)
        {
            foreach (bool allocatorFails in new[] { false, true })
            {
                yield return [mode, allocatorFails];
            }
        }
    }

    [SkippableTheory]
    [MemberData(nameof(FilterModes))]
    public void NewPicture_matches_filter_NewPicture(int mode, bool formatAllocatorFails)
    {
        ParityLibrary.Require();

        nint filter = ParityNative.parity_filter_new(mode);
        try
        {
            ParityNative.ScriptSetup script = s => s->PnffNull = formatAllocatorFails ? 1 : 0;

            var (cPicture, cLog) = Harness.Run(script, () => ParityNative.parity_filter_NewPicture(filter));
            var (managedPicture, managedLog) = Harness.Run(script, () => new VLCFilterContext(filter).NewPicture());

            ParityAssert.Equal(cPicture, managedPicture, "returned picture");
            ParityAssert.Same(cLog, managedLog, "recorded calls");

            // And the behaviour itself, so that a shared bug in both runs cannot hide.
            bool callbackProvides = mode == 2;
            nint expected = callbackProvides ? ParityNative.parity_fake_buffer_picture()
                : formatAllocatorFails ? 0
                : ParityNative.parity_fake_pnff_picture();
            Assert.Equal(expected, managedPicture);
            Assert.Equal(mode >= 2 ? 1 : 0, managedLog.BufferNewCalls);
            Assert.Equal(callbackProvides ? 0 : 1, managedLog.NewFmtCalls);
            if (!callbackProvides)
            {
                Assert.Equal(ParityNative.parity_filter_fmt_out_video(filter), managedLog.NewFmtArg);
            }

            if (managedPicture == 0)
            {
                Assert.Equal(1, managedLog.LogCalls);
                Assert.Equal(ParityNative.parity_VLC_MSG_WARN(), managedLog.LogType);
                Assert.Equal("can't get output picture", managedLog.LogMessage);
                Assert.Equal(filter, managedLog.LogObj);
            }
            else
            {
                Assert.Equal(0, managedLog.LogCalls);
            }
        }
        finally
        {
            ParityNative.parity_filter_free(filter);
        }
    }

    [SkippableFact]
    public void Documented_difference_NewPicture_returns_null_for_a_null_filter()
    {
        ParityLibrary.Require();

        // filter_NewPicture(NULL) dereferences NULL; the managed context reports "no picture" without logging.
        var (picture, log) = Harness.Run(null, () => new VLCFilterContext(0).NewPicture());

        Assert.Equal(0, picture);
        Assert.Equal(0, log.LogCalls);
        Assert.Equal(0, log.NewFmtCalls);
    }
}
