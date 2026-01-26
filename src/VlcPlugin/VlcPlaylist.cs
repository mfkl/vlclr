using VlcPlugin.Generated;
using VlcPlugin.Native;

namespace VlcPlugin;

/// <summary>
/// High-level wrapper for VLC playlist control.
/// Provides methods to control playback (play, pause, stop, next, prev).
/// </summary>
public sealed class VlcPlaylist : IDisposable
{
    private readonly nint _intf;
    private readonly nint _playlist;
    private readonly VlcLogger _logger;
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
    public long Count => VlcBridge.PlaylistCount(_playlist);

    /// <summary>
    /// Gets the current item index (-1 if none).
    /// </summary>
    public long CurrentIndex => VlcBridge.PlaylistGetCurrentIndex(_playlist);

    /// <summary>
    /// Gets whether there is a next item available.
    /// </summary>
    public bool HasNext => VlcBridge.PlaylistHasNext(_playlist) != 0;

    /// <summary>
    /// Gets whether there is a previous item available.
    /// </summary>
    public bool HasPrev => VlcBridge.PlaylistHasPrev(_playlist) != 0;

    private VlcPlaylist(nint intf, nint playlist, VlcLogger logger)
    {
        _intf = intf;
        _playlist = playlist;
        _logger = logger;
    }

    /// <summary>
    /// Creates a VlcPlaylist instance from an interface object.
    /// </summary>
    /// <param name="intf">Pointer to intf_thread_t</param>
    /// <param name="logger">Logger for diagnostic messages</param>
    /// <returns>VlcPlaylist instance, or null if creation failed</returns>
    public static VlcPlaylist? Create(nint intf, VlcLogger logger)
    {
        if (intf == nint.Zero)
        {
            logger.Error(".NET plugin Cannot create VlcPlaylist: invalid interface pointer");
            return null;
        }

        nint playlist = VlcBridge.GetPlaylist(intf);
        if (playlist == nint.Zero)
        {
            logger.Error(".NET plugin Failed to get playlist from interface");
            return null;
        }

        logger.Debug(".NET plugin VlcPlaylist created successfully");
        return new VlcPlaylist(intf, playlist, logger);
    }

    /// <summary>
    /// Start playback.
    /// </summary>
    /// <returns>True on success, false on failure</returns>
    public bool Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int result = VlcBridge.PlaylistStart(_playlist);
        if (result != 0)
        {
            _logger.Warning($".NET plugin Playlist start failed with code {result}");
            return false;
        }

        _logger.Debug(".NET plugin Playlist started");
        return true;
    }

    /// <summary>
    /// Stop playback.
    /// </summary>
    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        VlcBridge.PlaylistStop(_playlist);
        _logger.Debug(".NET plugin Playlist stopped");
    }

    /// <summary>
    /// Pause playback.
    /// </summary>
    public void Pause()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        VlcBridge.PlaylistPause(_playlist);
        _logger.Debug(".NET plugin Playlist paused");
    }

    /// <summary>
    /// Resume playback.
    /// </summary>
    public void Resume()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        VlcBridge.PlaylistResume(_playlist);
        _logger.Debug(".NET plugin Playlist resumed");
    }

    /// <summary>
    /// Toggle between play and pause.
    /// </summary>
    /// <param name="player">Player instance to check current state</param>
    public void TogglePause(VlcPlayer player)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var state = player.State;
        if (state == VlcPlayerState.Playing)
        {
            Pause();
        }
        else if (state == VlcPlayerState.Paused)
        {
            Resume();
        }
        else if (state == VlcPlayerState.Stopped)
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

        int result = VlcBridge.PlaylistNext(_playlist);
        if (result != 0)
        {
            _logger.Debug($".NET plugin Playlist next failed with code {result}");
            return false;
        }

        _logger.Debug(".NET plugin Playlist moved to next item");
        return true;
    }

    /// <summary>
    /// Go to the previous item.
    /// </summary>
    /// <returns>True on success, false on failure</returns>
    public bool Prev()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int result = VlcBridge.PlaylistPrev(_playlist);
        if (result != 0)
        {
            _logger.Debug($".NET plugin Playlist prev failed with code {result}");
            return false;
        }

        _logger.Debug(".NET plugin Playlist moved to previous item");
        return true;
    }

    /// <summary>
    /// Go to a specific index.
    /// </summary>
    /// <param name="index">Index to go to (-1 for none)</param>
    /// <returns>True on success, false on failure</returns>
    public bool GoTo(long index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int result = VlcBridge.PlaylistGoTo(_playlist, index);
        if (result != 0)
        {
            _logger.Debug($".NET plugin Playlist goto {index} failed with code {result}");
            return false;
        }

        _logger.Debug($".NET plugin Playlist moved to index {index}");
        return true;
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
        _logger.Debug(".NET plugin VlcPlaylist disposed");
    }
}
