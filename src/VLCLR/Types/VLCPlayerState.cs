// VLC player state enumeration
// Source: vlc/include/vlc_player.h
// VLC Version: 4.0.6

namespace VLCLR.Types;

/// <summary>
/// VLC player states.
/// Source: vlc_player.h, enum vlc_player_state
/// </summary>
public enum VLCPlayerState
{
    /// <summary>Player is stopped</summary>
    Stopped = 0,

    /// <summary>Player is starting (loading media)</summary>
    Started = 1,

    /// <summary>Player is actively playing</summary>
    Playing = 2,

    /// <summary>Player is paused</summary>
    Paused = 3,

    /// <summary>Player is stopping</summary>
    Stopping = 4
}
