// VLC player state enumeration
// Source: vlc/include/vlc_player.h
// VLC Version: 4.0.6

using Interop = VLCLR.Native.Interop;

namespace VLCLR.Types;

/// <summary>
/// VLC player states.
/// Source: vlc_player.h, enum vlc_player_state
/// </summary>
public enum VLCPlayerState
{
    /// <summary>Player is stopped</summary>
    Stopped = (int)Interop.PlayerState.Stopped,

    /// <summary>Player is starting (loading media)</summary>
    Started = (int)Interop.PlayerState.Started,

    /// <summary>Player is actively playing</summary>
    Playing = (int)Interop.PlayerState.Playing,

    /// <summary>Player is paused</summary>
    Paused = (int)Interop.PlayerState.Paused,

    /// <summary>Player is stopping</summary>
    Stopping = (int)Interop.PlayerState.Stopping
}
