// Seek precision options
// Source: vlc/include/vlc_player.h
// VLC Version: 4.0.6

using Interop = VLCLR.Native.Interop;

namespace VLCLR.Types;

/// <summary>
/// Seek precision options.
/// </summary>
public enum SeekSpeed
{
    /// <summary>Seek to exact time (may be slower)</summary>
    Precise = (int)Interop.PlayerSeekSpeed.Precise,

    /// <summary>Seek to nearest keyframe (faster)</summary>
    Fast = (int)Interop.PlayerSeekSpeed.Fast,
}
