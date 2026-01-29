using VLCLR.Types;
using VLCLR.Native;

namespace VLCLR;

/// <summary>
/// High-level wrapper for VLC playlist control.
/// Provides methods to control playback (play, pause, stop, next, prev).
/// All operations that require the playlist lock handle locking internally.
/// </summary>
public sealed class VLCPlaylist : IDisposable
{
    private readonly nint _intf;
    private readonly nint _playlist;
    private readonly VLCLogger _logger;
    private bool _disposed;

    /// <summary>
    /// Gets the native playlist pointer.
    /// </summary>
    public nint Handle => _playlist;

    /// <summary>
    /// Gets whether the playlist is valid.
    /// </summary>
    public bool IsValid => _playlist != nint.Zero;

    /// <summary>
    /// Gets the number of items in the playlist.
    /// </summary>
    public long Count
    {
        get
        {
            VLCCore.PlaylistLock(_playlist);
            try
            {
                return (long)VLCCore.PlaylistCount(_playlist);
            }
            finally
            {
                VLCCore.PlaylistUnlock(_playlist);
            }
        }
    }

    /// <summary>
    /// Gets the current item index (-1 if none).
    /// </summary>
    public long CurrentIndex
    {
        get
        {
            VLCCore.PlaylistLock(_playlist);
            try
            {
                return VLCCore.PlaylistGetCurrentIndex(_playlist);
            }
            finally
            {
                VLCCore.PlaylistUnlock(_playlist);
            }
        }
    }

    /// <summary>
    /// Gets whether there is a next item available.
    /// </summary>
    public bool HasNext
    {
        get
        {
            VLCCore.PlaylistLock(_playlist);
            try
            {
                return VLCCore.PlaylistHasNext(_playlist);
            }
            finally
            {
                VLCCore.PlaylistUnlock(_playlist);
            }
        }
    }

    /// <summary>
    /// Gets whether there is a previous item available.
    /// </summary>
    public bool HasPrev
    {
        get
        {
            VLCCore.PlaylistLock(_playlist);
            try
            {
                return VLCCore.PlaylistHasPrev(_playlist);
            }
            finally
            {
                VLCCore.PlaylistUnlock(_playlist);
            }
        }
    }

    private VLCPlaylist(nint intf, nint playlist, VLCLogger logger)
    {
        _intf = intf;
        _playlist = playlist;
        _logger = logger;
    }

    /// <summary>
    /// Creates a VLCPlaylist instance from an interface object.
    /// </summary>
    /// <param name="intf">Pointer to intf_thread_t</param>
    /// <param name="logger">Logger for diagnostic messages</param>
    /// <returns>VLCPlaylist instance, or null if creation failed</returns>
    public static VLCPlaylist? Create(nint intf, VLCLogger logger)
    {
        if (intf == nint.Zero)
        {
            logger.Error(".NET plugin Cannot create VLCPlaylist: invalid interface pointer");
            return null;
        }

        nint playlist = VLCCore.IntfGetMainPlaylist(intf);
        if (playlist == nint.Zero)
        {
            logger.Error(".NET plugin Failed to get playlist from interface");
            return null;
        }

        logger.Debug(".NET plugin VLCPlaylist created successfully");
        return new VLCPlaylist(intf, playlist, logger);
    }

    /// <summary>
    /// Start playback.
    /// </summary>
    /// <returns>True on success, false on failure</returns>
    public bool Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        VLCCore.PlaylistLock(_playlist);
        try
        {
            int result = VLCCore.PlaylistStart(_playlist);
            if (result != 0)
            {
                _logger.Warning($".NET plugin Playlist start failed with code {result}");
                return false;
            }

            _logger.Debug(".NET plugin Playlist started");
            return true;
        }
        finally
        {
            VLCCore.PlaylistUnlock(_playlist);
        }
    }

    /// <summary>
    /// Stop playback.
    /// </summary>
    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        VLCCore.PlaylistLock(_playlist);
        try
        {
            VLCCore.PlaylistStop(_playlist);
            _logger.Debug(".NET plugin Playlist stopped");
        }
        finally
        {
            VLCCore.PlaylistUnlock(_playlist);
        }
    }

    /// <summary>
    /// Pause playback.
    /// </summary>
    public void Pause()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        VLCCore.PlaylistLock(_playlist);
        try
        {
            VLCCore.PlaylistPause(_playlist);
            _logger.Debug(".NET plugin Playlist paused");
        }
        finally
        {
            VLCCore.PlaylistUnlock(_playlist);
        }
    }

    /// <summary>
    /// Resume playback.
    /// </summary>
    public void Resume()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        VLCCore.PlaylistLock(_playlist);
        try
        {
            VLCCore.PlaylistResume(_playlist);
            _logger.Debug(".NET plugin Playlist resumed");
        }
        finally
        {
            VLCCore.PlaylistUnlock(_playlist);
        }
    }

    /// <summary>
    /// Toggle between play and pause.
    /// </summary>
    /// <param name="player">Player instance to check current state</param>
    public void TogglePause(VLCPlayer player)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var state = player.State;
        if (state == VLCPlayerState.Playing)
        {
            Pause();
        }
        else if (state == VLCPlayerState.Paused)
        {
            Resume();
        }
        else if (state == VLCPlayerState.Stopped)
        {
            Start();
        }
    }

    /// <summary>
    /// Go to the next item.
    /// </summary>
    /// <returns>True on success, false on failure</returns>
    public bool Next()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        VLCCore.PlaylistLock(_playlist);
        try
        {
            int result = VLCCore.PlaylistNext(_playlist);
            if (result != 0)
            {
                _logger.Debug($".NET plugin Playlist next failed with code {result}");
                return false;
            }

            _logger.Debug(".NET plugin Playlist moved to next item");
            return true;
        }
        finally
        {
            VLCCore.PlaylistUnlock(_playlist);
        }
    }

    /// <summary>
    /// Go to the previous item.
    /// </summary>
    /// <returns>True on success, false on failure</returns>
    public bool Prev()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        VLCCore.PlaylistLock(_playlist);
        try
        {
            int result = VLCCore.PlaylistPrev(_playlist);
            if (result != 0)
            {
                _logger.Debug($".NET plugin Playlist prev failed with code {result}");
                return false;
            }

            _logger.Debug(".NET plugin Playlist moved to previous item");
            return true;
        }
        finally
        {
            VLCCore.PlaylistUnlock(_playlist);
        }
    }

    /// <summary>
    /// Go to a specific index.
    /// </summary>
    /// <param name="index">Index to go to (-1 for none)</param>
    /// <returns>True on success, false on failure</returns>
    public bool GoTo(long index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        VLCCore.PlaylistLock(_playlist);
        try
        {
            int result = VLCCore.PlaylistGoTo(_playlist, (nint)index);
            if (result != 0)
            {
                _logger.Debug($".NET plugin Playlist goto {index} failed with code {result}");
                return false;
            }

            _logger.Debug($".NET plugin Playlist moved to index {index}");
            return true;
        }
        finally
        {
            VLCCore.PlaylistUnlock(_playlist);
        }
    }

    /// <summary>
    /// Go to a specific index and start playback.
    /// </summary>
    /// <param name="index">Index to play</param>
    /// <returns>True on success, false on failure</returns>
    public bool PlayAt(long index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!GoTo(index))
            return false;

        return Start();
    }

    /// <summary>
    /// Disposes the playlist wrapper.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _logger.Debug(".NET plugin VLCPlaylist disposed");
    }
}
