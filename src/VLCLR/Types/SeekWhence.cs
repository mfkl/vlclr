// Seek reference point options
// Source: vlc/include/vlc_player.h
// VLC Version: 4.0.6

using Interop = VLCLR.Native.Interop;

namespace VLCLR.Types;

/// <summary>
/// Seek reference point options.
/// </summary>
public enum SeekWhence
{
    /// <summary>Seek from beginning of media</summary>
    Absolute = (int)Interop.PlayerWhence.Absolute,

    /// <summary>Seek relative to current position</summary>
    Relative = (int)Interop.PlayerWhence.Relative,
}
