using System;
using System.Runtime.InteropServices;
using VlcPlugin.Types;
using VlcPlugin.Native;

namespace VlcPlugin;

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

/// <summary>
/// Delegate for player state change events.
/// </summary>
/// <param name="newState">The new player state</param>
public delegate void PlayerStateChangedHandler(VlcPlayerState newState);

/// <summary>
/// Delegate for player position change events.
/// </summary>
/// <param name="time">The current time in VLC ticks (microseconds)</param>
/// <param name="position">The current position as a ratio [0.0, 1.0]</param>
public delegate void PlayerPositionChangedHandler(long time, double position);

/// <summary>
/// Delegate for media change events.
/// </summary>
/// <param name="mediaPtr">Pointer to the new input_item_t, or IntPtr.Zero if no media</param>
public delegate void PlayerMediaChangedHandler(nint mediaPtr);

/// <summary>
/// High-level wrapper for VLC player.
/// Provides access to player state and events.
/// All operations that require the player lock handle locking internally.
/// </summary>
public sealed class VlcPlayer : IDisposable
{
    private readonly nint _player;
    private readonly VlcLogger _logger;
    private nint _listenerHandle;
    private bool _disposed;

    // Keep callbacks structure and delegates alive to prevent GC
    private VlcPlayerCbs _callbacks;
    private nint _callbacksPtr;

    /// <summary>
    /// Event raised when the player state changes.
    /// </summary>
    public event PlayerStateChangedHandler? StateChanged;

    /// <summary>
    /// Event raised when the player position changes.
    /// </summary>
    public event PlayerPositionChangedHandler? PositionChanged;

    /// <summary>
    /// Event raised when the current media changes.
    /// </summary>
    public event PlayerMediaChangedHandler? MediaChanged;

    /// <summary>
    /// Creates a player wrapper from an interface object.
    /// </summary>
    /// <param name="intf">Pointer to intf_thread_t</param>
    /// <param name="logger">Logger for diagnostic messages</param>
    /// <returns>VlcPlayer instance, or null if player not available</returns>
    public static VlcPlayer? Create(nint intf, VlcLogger logger)
    {
        // Get playlist first, then get player from playlist
        nint playlist = VlcCore.IntfGetMainPlaylist(intf);
        if (playlist == nint.Zero)
        {
            logger.Warning(".NET plugin Failed to get playlist from interface");
            return null;
        }

        nint player = VlcCore.PlaylistGetPlayer(playlist);
        if (player == nint.Zero)
        {
            logger.Warning(".NET plugin Failed to get player from playlist");
            return null;
        }

        return new VlcPlayer(player, logger);
    }

    private VlcPlayer(nint player, VlcLogger logger)
    {
        _player = player;
        _logger = logger;
    }

    /// <summary>
    /// Gets the current player state.
    /// </summary>
    public VlcPlayerState State
    {
        get
        {
            VlcCore.PlayerLock(_player);
            try
            {
                int state = VlcCore.PlayerGetState(_player);
                return (VlcPlayerState)state;
            }
            finally
            {
                VlcCore.PlayerUnlock(_player);
            }
        }
    }

    /// <summary>
    /// Gets the current playback time in microseconds.
    /// Returns Int64.MinValue if not playing or unavailable.
    /// </summary>
    public long Time
    {
        get
        {
            VlcCore.PlayerLock(_player);
            try
            {
                return VlcCore.PlayerGetTime(_player);
            }
            finally
            {
                VlcCore.PlayerUnlock(_player);
            }
        }
    }

    /// <summary>
    /// Gets the current playback time as a TimeSpan.
    /// Returns TimeSpan.Zero if not playing or unavailable.
    /// </summary>
    public TimeSpan TimeSpan
    {
        get
        {
            long time = Time;
            if (time == long.MinValue)
                return TimeSpan.Zero;
            // VLC ticks are microseconds
            return TimeSpan.FromTicks(time * 10);  // Convert microseconds to 100-nanosecond ticks
        }
    }

    /// <summary>
    /// Gets the total length of the current media in microseconds.
    /// Returns Int64.MinValue if unknown.
    /// </summary>
    public long Length
    {
        get
        {
            VlcCore.PlayerLock(_player);
            try
            {
                return VlcCore.PlayerGetLength(_player);
            }
            finally
            {
                VlcCore.PlayerUnlock(_player);
            }
        }
    }

    /// <summary>
    /// Gets the total length of the current media as a TimeSpan.
    /// Returns TimeSpan.Zero if unknown.
    /// </summary>
    public TimeSpan LengthTimeSpan
    {
        get
        {
            long length = Length;
            if (length == long.MinValue)
                return TimeSpan.Zero;
            // VLC ticks are microseconds
            return TimeSpan.FromTicks(length * 10);  // Convert microseconds to 100-nanosecond ticks
        }
    }

    /// <summary>
    /// Gets the current playback position as a ratio [0.0, 1.0].
    /// Returns -1.0 if not playing.
    /// </summary>
    public double Position
    {
        get
        {
            VlcCore.PlayerLock(_player);
            try
            {
                return VlcCore.PlayerGetPosition(_player);
            }
            finally
            {
                VlcCore.PlayerUnlock(_player);
            }
        }
    }

    /// <summary>
    /// Gets whether seeking is supported for the current media.
    /// </summary>
    public bool CanSeek
    {
        get
        {
            VlcCore.PlayerLock(_player);
            try
            {
                int caps = VlcCore.PlayerGetCapabilities(_player);
                return (caps & VlcPlayerCapabilities.Seek) != 0;
            }
            finally
            {
                VlcCore.PlayerUnlock(_player);
            }
        }
    }

    /// <summary>
    /// Gets whether pausing is supported for the current media.
    /// </summary>
    public bool CanPause
    {
        get
        {
            VlcCore.PlayerLock(_player);
            try
            {
                int caps = VlcCore.PlayerGetCapabilities(_player);
                return (caps & VlcPlayerCapabilities.Pause) != 0;
            }
            finally
            {
                VlcCore.PlayerUnlock(_player);
            }
        }
    }

    /// <summary>
    /// Gets or sets the audio volume.
    /// Valid range is [0.0, 2.0] where 1.0 is 100% volume.
    /// Returns -1.0 if no audio output is available.
    /// Note: Audio output functions do NOT require player lock.
    /// </summary>
    public float Volume
    {
        get => VlcCore.PlayerAoutGetVolume(_player);
        set => VlcCore.PlayerAoutSetVolume(_player, value);
    }

    /// <summary>
    /// Gets or sets whether the audio output is muted.
    /// Returns null if no audio output is available.
    /// Note: Audio output functions do NOT require player lock.
    /// </summary>
    public bool? IsMuted
    {
        get
        {
            int result = VlcCore.PlayerAoutIsMuted(_player);
            return result < 0 ? null : result != 0;
        }
        set
        {
            if (value.HasValue)
            {
                VlcCore.PlayerAoutMute(_player, value.Value);
            }
        }
    }

    /// <summary>
    /// Toggles the mute state.
    /// Note: Audio output functions do NOT require player lock.
    /// </summary>
    /// <returns>True if successful, false if no audio output is available</returns>
    public bool ToggleMute()
    {
        bool? currentMuted = IsMuted;
        if (currentMuted == null)
            return false;
        return VlcCore.PlayerAoutMute(_player, !currentMuted.Value) == 0;
    }

    /// <summary>
    /// Seeks to a specific time.
    /// </summary>
    /// <param name="time">Time in microseconds</param>
    /// <param name="speed">Seek precision (default: Precise)</param>
    /// <param name="whence">Seek reference (default: Absolute)</param>
    public void SeekByTime(long time, SeekSpeed speed = SeekSpeed.Precise, SeekWhence whence = SeekWhence.Absolute)
    {
        VlcCore.PlayerLock(_player);
        try
        {
            VlcCore.PlayerSeekByTime(_player, time, (int)speed, (int)whence);
        }
        finally
        {
            VlcCore.PlayerUnlock(_player);
        }
    }

    /// <summary>
    /// Seeks to a specific time.
    /// </summary>
    /// <param name="time">Target time</param>
    /// <param name="speed">Seek precision (default: Precise)</param>
    /// <param name="whence">Seek reference (default: Absolute)</param>
    public void SeekByTime(TimeSpan time, SeekSpeed speed = SeekSpeed.Precise, SeekWhence whence = SeekWhence.Absolute)
    {
        // Convert from 100-nanosecond ticks to microseconds
        long microseconds = time.Ticks / 10;
        VlcCore.PlayerLock(_player);
        try
        {
            VlcCore.PlayerSeekByTime(_player, microseconds, (int)speed, (int)whence);
        }
        finally
        {
            VlcCore.PlayerUnlock(_player);
        }
    }

    /// <summary>
    /// Seeks to a specific position.
    /// </summary>
    /// <param name="position">Position as a ratio [0.0, 1.0]</param>
    /// <param name="speed">Seek precision (default: Precise)</param>
    /// <param name="whence">Seek reference (default: Absolute)</param>
    public void SeekByPosition(double position, SeekSpeed speed = SeekSpeed.Precise, SeekWhence whence = SeekWhence.Absolute)
    {
        VlcCore.PlayerLock(_player);
        try
        {
            VlcCore.PlayerSeekByPos(_player, position, (int)speed, (int)whence);
        }
        finally
        {
            VlcCore.PlayerUnlock(_player);
        }
    }

    /// <summary>
    /// Pauses the player.
    /// </summary>
    public void Pause()
    {
        VlcCore.PlayerLock(_player);
        try
        {
            VlcCore.PlayerPause(_player);
        }
        finally
        {
            VlcCore.PlayerUnlock(_player);
        }
    }

    /// <summary>
    /// Resumes playback.
    /// </summary>
    public void Resume()
    {
        VlcCore.PlayerLock(_player);
        try
        {
            VlcCore.PlayerResume(_player);
        }
        finally
        {
            VlcCore.PlayerUnlock(_player);
        }
    }

    // Static instance reference for callbacks (callbacks are called without user data context in some cases)
    private static VlcPlayer? s_currentInstance;

    /// <summary>
    /// Starts listening for player events.
    /// Uses the VLC player callbacks structure with 35 callback slots.
    /// </summary>
    /// <returns>True if listener was added successfully</returns>
    public bool StartListening()
    {
        if (_listenerHandle != nint.Zero)
        {
            _logger.Warning(".NET plugin Already listening to player events");
            return false;
        }

        // Keep static reference for callbacks
        s_currentInstance = this;

        // Set up the callbacks structure - all 35 slots must be present
        // We only populate the ones we need
        _callbacks = new VlcPlayerCbs
        {
            // Callback 1: on_current_media_changed(player, new_media, data)
            OnCurrentMediaChanged = Marshal.GetFunctionPointerForDelegate(
                (OnCurrentMediaChangedDelegate)OnCurrentMediaChangedStatic),
            // Callback 2: on_state_changed(player, new_state, data)
            OnStateChanged = Marshal.GetFunctionPointerForDelegate(
                (OnStateChangedDelegate)OnStateChangedStatic),
            // Callback 7: on_position_changed(player, new_time, new_pos, data)
            OnPositionChanged = Marshal.GetFunctionPointerForDelegate(
                (OnPositionChangedDelegate)OnPositionChangedStatic),
            // All other callbacks remain null (nint.Zero)
        };

        // Allocate and pin the callbacks structure
        _callbacksPtr = Marshal.AllocHGlobal(Marshal.SizeOf<VlcPlayerCbs>());
        Marshal.StructureToPtr(_callbacks, _callbacksPtr, false);

        VlcCore.PlayerLock(_player);
        try
        {
            _listenerHandle = VlcCore.PlayerAddListener(_player, _callbacksPtr, nint.Zero);
        }
        finally
        {
            VlcCore.PlayerUnlock(_player);
        }

        if (_listenerHandle == nint.Zero)
        {
            _logger.Error(".NET plugin Failed to add player listener");
            Marshal.FreeHGlobal(_callbacksPtr);
            _callbacksPtr = nint.Zero;
            s_currentInstance = null;
            return false;
        }

        _logger.Info(".NET plugin Started listening to player events");
        return true;
    }

    /// <summary>
    /// Stops listening for player events.
    /// </summary>
    public void StopListening()
    {
        if (_listenerHandle == nint.Zero)
            return;

        VlcCore.PlayerLock(_player);
        try
        {
            VlcCore.PlayerRemoveListener(_player, _listenerHandle);
        }
        finally
        {
            VlcCore.PlayerUnlock(_player);
        }

        _listenerHandle = nint.Zero;

        // Free the callbacks structure
        if (_callbacksPtr != nint.Zero)
        {
            Marshal.FreeHGlobal(_callbacksPtr);
            _callbacksPtr = nint.Zero;
        }

        s_currentInstance = null;
        _logger.Info(".NET plugin Stopped listening to player events");
    }

    // Delegate types matching VLC's callback signatures
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void OnCurrentMediaChangedDelegate(nint player, nint newMedia, nint userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void OnStateChangedDelegate(nint player, int newState, nint userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void OnPositionChangedDelegate(nint player, long newTime, double newPos, nint userData);

    // Static callback methods that forward to instance methods
    private static void OnCurrentMediaChangedStatic(nint player, nint newMedia, nint userData)
    {
        try
        {
            var instance = s_currentInstance;
            if (instance != null)
            {
                instance._logger.Debug($".NET plugin Media changed: {(newMedia != nint.Zero ? "new media" : "no media")}");
                instance.MediaChanged?.Invoke(newMedia);
            }
        }
        catch
        {
            // Swallow exceptions in callbacks to avoid crashing VLC
        }
    }

    private static void OnStateChangedStatic(nint player, int newState, nint userData)
    {
        try
        {
            var instance = s_currentInstance;
            if (instance != null)
            {
                var state = (VlcPlayerState)newState;
                instance._logger.Debug($".NET plugin Player state changed: {state}");
                instance.StateChanged?.Invoke(state);
            }
        }
        catch
        {
            // Swallow exceptions in callbacks to avoid crashing VLC
        }
    }

    private static void OnPositionChangedStatic(nint player, long newTime, double newPos, nint userData)
    {
        try
        {
            var instance = s_currentInstance;
            instance?.PositionChanged?.Invoke(newTime, newPos);
        }
        catch
        {
            // Swallow exceptions in callbacks to avoid crashing VLC
        }
    }

    /// <summary>
    /// Disposes the player wrapper and stops listening.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        StopListening();

        // Free any remaining unmanaged resources
        if (_callbacksPtr != nint.Zero)
        {
            Marshal.FreeHGlobal(_callbacksPtr);
            _callbacksPtr = nint.Zero;
        }
    }
}
