using System;
using System.Runtime.InteropServices;
using VlcPlugin.Generated;
using VlcPlugin.Native;

namespace VlcPlugin;

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
