using System;

namespace VlcPlugin;

/// <summary>
/// Manages plugin lifetime and state.
/// </summary>
public sealed class PluginState : IDisposable
{
    private readonly nint _vlcObject;
    private readonly VlcLogger _logger;
    private bool _disposed;

    public PluginState(nint vlcObject)
    {
        _vlcObject = vlcObject;
        _logger = new VlcLogger(vlcObject);
    }

    public void Initialize()
    {
        // Plugin initialization logic
        // - Register variables with VLC
        // - Set up event handlers
        // - Initialize any managed resources

        _logger.Info("C# Plugin initialized");
    }

    public void Cleanup()
    {
        if (_disposed) return;
        _disposed = true;

        // Cleanup logic
        // - Unregister variables
        // - Remove event handlers
        // - Release managed resources

        _logger.Info("C# Plugin cleaned up");
    }

    public void Dispose() => Cleanup();
}
