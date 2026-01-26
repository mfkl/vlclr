using System;
using VlcPlugin.Generated;
using VlcPlugin.Native;

namespace VlcPlugin;

/// <summary>
/// Manages plugin lifetime and state.
/// </summary>
public sealed class PluginState : IDisposable
{
    private const string VarPluginVersion = "csharp-plugin-version";
    private const string VarPluginCounter = "csharp-plugin-counter";

    private readonly nint _vlcObject;
    private readonly VlcLogger _logger;
    private readonly VlcVariable _variable;
    private VlcPlayer? _player;
    private VlcPlaylist? _playlist;
    private bool _disposed;

    public PluginState(nint vlcObject)
    {
        _vlcObject = vlcObject;
        _logger = new VlcLogger(vlcObject);
        _variable = new VlcVariable(vlcObject);
    }

    public void Initialize()
    {
        try
        {
            // Plugin initialization logic
            _logger.Info(".NET plugin initializing...");

            // Demonstrate VLC variable creation and usage
            if (_variable.CreateString(VarPluginVersion, "1.0.0"))
            {
                _logger.Info($".NET plugin Created string variable '{VarPluginVersion}'");
                string? version = _variable.GetString(VarPluginVersion);
                _logger.Info($".NET plugin Plugin version: {version ?? "(null)"}");
            }

            if (_variable.CreateInteger(VarPluginCounter, 0))
            {
                _logger.Info($".NET plugin Created integer variable '{VarPluginCounter}'");

                // Demonstrate increment
                long counter = _variable.GetInteger(VarPluginCounter);
                _logger.Info($".NET plugin Counter initial value: {counter}");

                _variable.SetInteger(VarPluginCounter, counter + 1);
                counter = _variable.GetInteger(VarPluginCounter);
                _logger.Info($".NET plugin Counter after increment: {counter}");
            }

            // Initialize player for event handling
            _player = VlcPlayer.Create(_vlcObject, _logger);
            if (_player != null)
            {
                _player.StateChanged += OnPlayerStateChanged;
                _player.MediaChanged += OnPlayerMediaChanged;
                _player.StartListening();
                _logger.Info(".NET plugin Player event listener registered");
            }

            // Initialize playlist for playback control
            _playlist = VlcPlaylist.Create(_vlcObject, _logger);
            if (_playlist != null)
            {
                _logger.Info($".NET plugin Playlist initialized: {_playlist.Count} items, current index: {_playlist.CurrentIndex}");
            }

            _logger.Info(".NET plugin initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.Error($".NET plugin initialization failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnPlayerStateChanged(VlcPlayerState state)
    {
        _logger.Info($".NET plugin Player state changed: {state}");
        if (state == VlcPlayerState.Playing)
        {
            _logger.Info(".NET Message -> Playback started");
        }
    }

    private void OnPlayerMediaChanged(nint media)
    {
        _logger.Info($".NET plugin Media changed: {(media == nint.Zero ? "none" : $"0x{media:X}")}");
    }

    public void Cleanup()
    {
        if (_disposed) return;
        _disposed = true;

        // Cleanup logic
        _logger.Info(".NET plugin cleaning up...");

        // Stop player event listener
        if (_player != null)
        {
            _player.StateChanged -= OnPlayerStateChanged;
            _player.MediaChanged -= OnPlayerMediaChanged;
            _player.StopListening();
            _player.Dispose();
            _player = null;
            _logger.Info(".NET plugin Player event listener unregistered");
        }

        // Dispose playlist wrapper
        if (_playlist != null)
        {
            _playlist.Dispose();
            _playlist = null;
        }

        // Destroy variables we created
        _variable.Destroy(VarPluginVersion);
        _variable.Destroy(VarPluginCounter);

        _logger.Info(".NET plugin cleaned up");
    }

    public void Dispose() => Cleanup();
}
