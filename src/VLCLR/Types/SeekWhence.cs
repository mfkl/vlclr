// Seek reference point options
// Source: vlc/include/vlc_player.h
// VLC Version: 4.0.6

namespace VLCLR.Types;

/// <summary>
/// Seek reference point options.
/// </summary>
public enum SeekWhence
{
    /// <summary>Seek from beginning of media</summary>
    Absolute = 0,

    /// <summary>Seek relative to current position</summary>
    Relative = 1,
}
