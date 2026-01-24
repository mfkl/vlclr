using System;

namespace VlcPlugin;

/// <summary>
/// Manages plugin lifetime and state.
/// </summary>
public sealed class PluginState : IDisposable
{
    private readonly nint _vlcObject;
    private bool _disposed;

    public PluginState(nint vlcObject)
    {
        _vlcObject = vlcObject;
    }

    public void Initialize()
    {
        // Plugin initialization logic
        // - Register variables with VLC
        // - Set up event handlers
        // - Initialize any managed resources

        Log("C# Plugin initialized");
    }

    public void Cleanup()
    {
        if (_disposed) return;
        _disposed = true;

        // Cleanup logic
        // - Unregister variables
        // - Remove event handlers
        // - Release managed resources

        Log("C# Plugin cleaned up");
    }

    public void Dispose() => Cleanup();

    private void Log(string message)
    {
        // TODO: Use VLC logging via libvlccore bindings
        Console.WriteLine($"[VlcPlugin] {message}");
    }
}
