// Seek precision options
// Source: vlc/include/vlc_player.h
// VLC Version: 4.0.6

namespace VLCLR.Types;

/// <summary>
/// Seek precision options.
/// </summary>
public enum SeekSpeed
{
    /// <summary>Seek to exact time (may be slower)</summary>
    Precise = 0,

    /// <summary>Seek to nearest keyframe (faster)</summary>
    Fast = 1,
}
