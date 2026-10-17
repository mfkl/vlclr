using InlineParity.Native;
using VLCLR;
using VLCLR.Native;

namespace InlineParity;

/// <summary>VLCPlayer against vlc_player_CanSeek / vlc_player_CanPause and the vlc_tick.h conversions it relies on.</summary>
public unsafe class PlayerParityTests
{
    private static VLCPlayer NewPlayer()
    {
        ParityNative.Reset();
        VLCPlayer? player = VLCPlayer.Create(ParityNative.parity_fake_intf(), new VLCLogger(0));
        Assert.NotNull(player);
        return player;
    }

    private static IEnumerable<int> CapabilityMasks()
    {
        for (int mask = 0; mask < 32; mask++)
        {
            yield return mask;
        }

        foreach (uint v in Cases.AnyUInt32(60))
        {
            yield return unchecked((int)v);
        }
    }

    [SkippableFact]
    public void Capability_flag_values_match_VLC_PLAYER_CAP()
    {
        ParityLibrary.Require();

        ParityAssert.Equal(ParityNative.parity_VLC_PLAYER_CAP_SEEK(), VLCPlayerCapabilities.Seek, "VLC_PLAYER_CAP_SEEK");
        ParityAssert.Equal(ParityNative.parity_VLC_PLAYER_CAP_PAUSE(), VLCPlayerCapabilities.Pause, "VLC_PLAYER_CAP_PAUSE");
    }

    [SkippableFact]
    public void CanSeek_and_CanPause_match_the_vlc_player_inlines()
    {
        ParityLibrary.Require();

        using VLCPlayer player = NewPlayer();
        foreach (int capabilities in CapabilityMasks())
        {
            ParityNative.ScriptSetup script = s => s->Capabilities = capabilities;
            nint fake = ParityNative.parity_fake_player();

            var (cSeek, cSeekLog) = Harness.Run(script, () => ParityNative.parity_vlc_player_CanSeek(fake) != 0);
            var (seek, seekLog) = Harness.Run(script, () => player.CanSeek);
            var (cPause, cPauseLog) = Harness.Run(script, () => ParityNative.parity_vlc_player_CanPause(fake) != 0);
            var (pause, pauseLog) = Harness.Run(script, () => player.CanPause);

            ParityAssert.Equal(cSeek, seek, $"CanSeek for caps 0x{capabilities:X}");
            ParityAssert.Equal(cPause, pause, $"CanPause for caps 0x{capabilities:X}");

            // Same single stand-in call on the same player; only the lock bracket differs (below).
            ParityAssert.Equal(cSeekLog.CapsCalls, seekLog.CapsCalls, "GetCapabilities calls (CanSeek)");
            ParityAssert.Equal(cSeekLog.CapsArg, seekLog.CapsArg, "GetCapabilities player (CanSeek)");
            ParityAssert.Equal(cPauseLog.CapsCalls, pauseLog.CapsCalls, "GetCapabilities calls (CanPause)");
            ParityAssert.Equal(cPauseLog.CapsArg, pauseLog.CapsArg, "GetCapabilities player (CanPause)");
        }
    }

    [SkippableFact]
    public void Documented_difference_CanSeek_takes_and_releases_the_player_lock_itself()
    {
        ParityLibrary.Require();

        // The C inlines require the caller to hold the lock; the managed properties bracket the call.
        using VLCPlayer player = NewPlayer();
        ParityNative.Reset();
        _ = player.CanSeek;
        Assert.Equal((1, 1), ParityNative.LockCounts());

        ParityNative.Reset();
        _ = player.CanPause;
        Assert.Equal((1, 1), ParityNative.LockCounts());
    }

    // ---- Tick conversions ------------------------------------------------

    [SkippableFact]
    public void VLCTick_constants_match_the_vlc_tick_macros()
    {
        ParityLibrary.Require();

        ParityAssert.Equal(ParityNative.parity_VLC_TICK_INVALID(), VLCTick.Invalid, "VLCTick.Invalid");
        ParityAssert.Equal(ParityNative.parity_VLC_TICK_0(), VLCTick.Zero, "VLCTick.Zero");
        ParityAssert.Equal(ParityNative.parity_CLOCK_FREQ(), VLCTick.Second, "VLCTick.Second");
        ParityAssert.Equal(ParityNative.parity_VLC_TICK_FROM_SEC(1), VLCTick.Second, "VLCTick.Second");
        ParityAssert.Equal(ParityNative.parity_VLC_TICK_FROM_MS(1), VLCTick.Millisecond, "VLCTick.Millisecond");
        Assert.Equal(1L, ParityNative.parity_VLC_TICK_FROM_US(1));
    }

    [SkippableFact]
    public void Ticks_are_microseconds_so_us_conversions_are_the_identity()
    {
        ParityLibrary.Require();

        // The "ticks are microseconds" comments in VLCPlayer rest on this.
        foreach (long value in Cases.AnyInt64(70))
        {
            ParityAssert.Equal(value, ParityNative.parity_VLC_TICK_FROM_US(value), $"VLC_TICK_FROM_US({value})");
            ParityAssert.Equal(value, ParityNative.parity_US_FROM_VLC_TICK(value), $"US_FROM_VLC_TICK({value})");
        }
    }

    [SkippableFact]
    public void VLCTick_arithmetic_matches_the_ms_and_sec_macros()
    {
        ParityLibrary.Require();

        // ms -> ticks and sec -> ticks multiply, so stay clear of signed overflow (undefined in C).
        foreach (long ms in Cases.Int64Within(long.MaxValue / VLCTick.Millisecond, 71))
        {
            ParityAssert.Equal(ParityNative.parity_VLC_TICK_FROM_MS(ms), ms * VLCTick.Millisecond, $"VLC_TICK_FROM_MS({ms})");
        }

        foreach (long sec in Cases.Int64Within(long.MaxValue / VLCTick.Second, 72))
        {
            ParityAssert.Equal(ParityNative.parity_VLC_TICK_FROM_SEC(sec), sec * VLCTick.Second, $"VLC_TICK_FROM_SEC({sec})");
        }

        // ticks -> ms and ticks -> sec divide (truncating toward zero) and have no overflow.
        foreach (long ticks in Cases.AnyInt64(73))
        {
            ParityAssert.Equal(ParityNative.parity_MS_FROM_VLC_TICK(ticks), ticks / VLCTick.Millisecond, $"MS_FROM_VLC_TICK({ticks})");
            ParityAssert.Equal(ParityNative.parity_SEC_FROM_VLC_TICK(ticks), ticks / VLCTick.Second, $"SEC_FROM_VLC_TICK({ticks})");
        }
    }

    // vlc_tick_t -> TimeSpan ticks (100 ns): C spells it NS_FROM_VLC_TICK(t) / 100.
    private static readonly long TickLimitForNanoseconds = long.MaxValue / 1000;

    [SkippableFact]
    public void TimeSpan_and_LengthTimeSpan_match_the_tick_to_nanosecond_conversion()
    {
        ParityLibrary.Require();

        using VLCPlayer player = NewPlayer();
        foreach (long ticks in Cases.Int64Within(TickLimitForNanoseconds, 80))
        {
            ParityNative.ScriptSetup script = s => { s->PlayerTime = ticks; s->PlayerLength = ticks; };
            long expected = ticks == VLCTick.Invalid ? 0 : ParityNative.parity_NS_FROM_VLC_TICK(ticks) / 100;

            ParityAssert.Equal(expected, Harness.Run(script, () => player.TimeSpan.Ticks).Result, $"TimeSpan for {ticks} ticks");
            ParityAssert.Equal(expected, Harness.Run(script, () => player.LengthTimeSpan.Ticks).Result, $"LengthTimeSpan for {ticks} ticks");
        }
    }

    [SkippableFact]
    public void TimeSpan_of_VLC_TICK_INVALID_is_zero_and_time_is_read_under_the_player_lock()
    {
        ParityLibrary.Require();

        using VLCPlayer player = NewPlayer();
        ParityNative.ScriptSetup script = s => s->PlayerTime = ParityNative.parity_VLC_TICK_INVALID();

        var (value, _) = Harness.Run(script, () => player.TimeSpan);
        Assert.Equal(TimeSpan.Zero, value);
        Assert.Equal((1, 1), ParityNative.LockCounts());
    }

    [SkippableFact]
    public void SeekByTime_TimeSpan_matches_the_nanosecond_to_tick_conversion()
    {
        ParityLibrary.Require();

        using VLCPlayer player = NewPlayer();
        // TimeSpan ticks are 100 ns; C spells the conversion VLC_TICK_FROM_NS(ticks * 100).
        foreach (long spanTicks in Cases.Int64Within(long.MaxValue / 100, 81))
        {
            long expected = ParityNative.parity_VLC_TICK_FROM_NS(spanTicks * 100);

            var (_, log) = Harness.Run<int>(null, () => { player.SeekByTime(TimeSpan.FromTicks(spanTicks)); return 0; });

            ParityAssert.Equal(expected, log.SeekTime, $"seek time for {spanTicks} TimeSpan ticks");
            ParityAssert.Equal(1, log.SeekTimeCalls, "seek calls");
        }
    }

    [SkippableFact]
    public void SeekByTime_TimeSpan_extremes_equal_exact_division()
    {
        ParityLibrary.Require();

        // Beyond long.MaxValue / 100 the C macro would overflow, so compare with exact math:
        // 100 ns ticks to microseconds is a division by 10, truncating toward zero.
        using VLCPlayer player = NewPlayer();
        foreach (TimeSpan span in new[] { TimeSpan.MaxValue, TimeSpan.MinValue, TimeSpan.FromTicks(long.MaxValue - 5), TimeSpan.FromTicks(long.MinValue + 5) })
        {
            var (_, log) = Harness.Run<int>(null, () => { player.SeekByTime(span); return 0; });

            Assert.Equal((long)(((System.Int128)span.Ticks * 100) / 1000), log.SeekTime);
        }
    }

    [SkippableFact]
    public void SeekByTime_long_passes_ticks_through_unchanged()
    {
        ParityLibrary.Require();

        using VLCPlayer player = NewPlayer();
        foreach (long ticks in Cases.AnyInt64(82))
        {
            var (_, log) = Harness.Run<int>(null, () => { player.SeekByTime(ticks); return 0; });

            ParityAssert.Equal(ParityNative.parity_VLC_TICK_FROM_US(ticks), log.SeekTime, $"seek time for {ticks}");
        }
    }
}
