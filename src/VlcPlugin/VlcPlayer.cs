using System;
using System.Runtime.InteropServices;
using VlcPlugin.Generated;
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
/// </summary>
public sealed class VlcPlayer : IDisposable
{
    private readonly nint _player;
    private readonly VlcLogger _logger;
    private nint _listenerHandle;
    private bool _disposed;

    // Keep delegates alive to prevent GC
    private OnStateChangedDelegate? _onStateChangedDelegate;
    private OnPositionChangedDelegate? _onPositionChangedDelegate;
    private OnMediaChangedDelegate? _onMediaChangedDelegate;

    // Native callback delegates
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void OnStateChangedDelegate(int newState, nint userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void OnPositionChangedDelegate(long newTime, double newPos, nint userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void OnMediaChangedDelegate(nint newMedia, nint userData);

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
        nint player = VlcBridge.GetPlayer(intf);
        if (player == nint.Zero)
        {
            logger.Warning("Failed to get player from interface");
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
            int state = VlcBridge.PlayerGetState(_player);
            return (VlcPlayerState)state;
        }
    }

    /// <summary>
    /// Gets the current playback time in microseconds.
    /// Returns Int64.MinValue if not playing or unavailable.
    /// </summary>
    public long Time => VlcBridge.PlayerGetTime(_player);

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
    public long Length => VlcBridge.PlayerGetLength(_player);

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
    public double Position => VlcBridge.PlayerGetPosition(_player);

    /// <summary>
    /// Gets whether seeking is supported for the current media.
    /// </summary>
    public bool CanSeek => VlcBridge.PlayerCanSeek(_player) != 0;

    /// <summary>
    /// Gets whether pausing is supported for the current media.
    /// </summary>
    public bool CanPause => VlcBridge.PlayerCanPause(_player) != 0;

    /// <summary>
    /// Seeks to a specific time.
    /// </summary>
    /// <param name="time">Time in microseconds</param>
    /// <param name="speed">Seek precision (default: Precise)</param>
    /// <param name="whence">Seek reference (default: Absolute)</param>
    public void SeekByTime(long time, SeekSpeed speed = SeekSpeed.Precise, SeekWhence whence = SeekWhence.Absolute)
    {
        VlcBridge.PlayerSeekByTime(_player, time, (int)speed, (int)whence);
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
        VlcBridge.PlayerSeekByTime(_player, microseconds, (int)speed, (int)whence);
    }

    /// <summary>
    /// Seeks to a specific position.
    /// </summary>
    /// <param name="position">Position as a ratio [0.0, 1.0]</param>
    /// <param name="speed">Seek precision (default: Precise)</param>
    /// <param name="whence">Seek reference (default: Absolute)</param>
    public void SeekByPosition(double position, SeekSpeed speed = SeekSpeed.Precise, SeekWhence whence = SeekWhence.Absolute)
    {
        VlcBridge.PlayerSeekByPos(_player, position, (int)speed, (int)whence);
    }

    /// <summary>
    /// Pauses the player.
    /// </summary>
    public void Pause()
    {
        VlcBridge.PlayerPause(_player);
    }

    /// <summary>
    /// Resumes playback.
    /// </summary>
    public void Resume()
    {
        VlcBridge.PlayerResume(_player);
    }

    /// <summary>
    /// Starts listening for player events.
    /// </summary>
    /// <returns>True if listener was added successfully</returns>
    public bool StartListening()
    {
        if (_listenerHandle != nint.Zero)
        {
            _logger.Warning("Already listening to player events");
            return false;
        }

        // Create native callback delegates
        _onStateChangedDelegate = OnStateChangedNative;
        _onPositionChangedDelegate = OnPositionChangedNative;
        _onMediaChangedDelegate = OnMediaChangedNative;

        // Set up the callbacks structure
        var callbacks = new PlayerCallbacksNative
        {
            OnStateChanged = Marshal.GetFunctionPointerForDelegate(_onStateChangedDelegate),
            OnPositionChanged = Marshal.GetFunctionPointerForDelegate(_onPositionChangedDelegate),
            OnMediaChanged = Marshal.GetFunctionPointerForDelegate(_onMediaChangedDelegate),
            UserData = nint.Zero
        };

        _listenerHandle = VlcBridge.PlayerAddListener(_player, ref callbacks);
        if (_listenerHandle == nint.Zero)
        {
            _logger.Error("Failed to add player listener");
            _onStateChangedDelegate = null;
            _onPositionChangedDelegate = null;
            _onMediaChangedDelegate = null;
            return false;
        }

        _logger.Info("Started listening to player events");
        return true;
    }

    /// <summary>
    /// Stops listening for player events.
    /// </summary>
    public void StopListening()
    {
        if (_listenerHandle == nint.Zero)
            return;

        VlcBridge.PlayerRemoveListener(_player, _listenerHandle);
        _listenerHandle = nint.Zero;

        // Clear delegates
        _onStateChangedDelegate = null;
        _onPositionChangedDelegate = null;
        _onMediaChangedDelegate = null;

        _logger.Info("Stopped listening to player events");
    }

    private void OnStateChangedNative(int newState, nint userData)
    {
        try
        {
            var state = (VlcPlayerState)newState;
            _logger.Debug($"Player state changed: {state}");
            StateChanged?.Invoke(state);
        }
        catch (Exception ex)
        {
            _logger.Error($"Error in StateChanged handler: {ex.Message}");
        }
    }

    private void OnPositionChangedNative(long newTime, double newPos, nint userData)
    {
        try
        {
            // Only log occasionally to avoid spam
            PositionChanged?.Invoke(newTime, newPos);
        }
        catch (Exception ex)
        {
            _logger.Error($"Error in PositionChanged handler: {ex.Message}");
        }
    }

    private void OnMediaChangedNative(nint newMedia, nint userData)
    {
        try
        {
            _logger.Debug($"Media changed: {(newMedia != nint.Zero ? "new media" : "no media")}");
            MediaChanged?.Invoke(newMedia);
        }
        catch (Exception ex)
        {
            _logger.Error($"Error in MediaChanged handler: {ex.Message}");
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
    }
}
