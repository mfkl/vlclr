// VLC Interface Base Class
// Provides a base class for interface plugins with lifecycle management
// VLC Version: 4.0.6

namespace VLCLR.Plugin;

/// <summary>
/// Base class for interface plugins: modules that control VLC rather than
/// process media, like VLC's hotkeys, HTTP and Qt interfaces. VLC opens one
/// when started with <c>--extraintf=name</c> (or <c>-I name</c>) and closes it
/// when it exits. Subclass and override OnOpen().
/// </summary>
/// <remarks>
/// VLC opens an interface from its own thread and waits for OnOpen to return:
/// start what the interface runs (a server, a thread) and return. VLC does not
/// call the interface again until it closes it.
/// </remarks>
public abstract class VLCInterfaceBase : IDisposable
{
    private VLCInterfaceContext _context;
    private VLCPlaylist? _playlist;
    private VLCPlayer? _player;
    private bool _disposed;

    /// <summary>
    /// Access to interface context (logger, etc.).
    /// </summary>
    protected VLCInterfaceContext Context => _context;

    /// <summary>
    /// VLC's main playlist. Valid from OnOpen until OnClose returns.
    /// </summary>
    protected VLCPlaylist Playlist => _playlist ?? throw new InvalidOperationException("The interface is not open.");

    /// <summary>
    /// The player of VLC's main playlist. Valid from OnOpen until OnClose returns.
    /// </summary>
    protected VLCPlayer Player => _player ?? throw new InvalidOperationException("The interface is not open.");

    /// <summary>
    /// Called when VLC opens the interface. Start what the interface runs here.
    /// Return false to fail open.
    /// </summary>
    protected abstract bool OnOpen();

    /// <summary>
    /// Called when VLC closes the interface. Stop what OnOpen started.
    /// </summary>
    protected virtual void OnClose() { }

    /// <summary>
    /// Internal: Called by the Open callback to initialize the interface.
    /// </summary>
    /// <param name="intfPtr">Pointer to intf_thread_t</param>
    /// <returns>0 on success, non-zero on failure</returns>
    public int InternalOpen(nint intfPtr)
    {
        _context = new VLCInterfaceContext(intfPtr);
        try
        {
            _context.Logger.Info($"[VLCLR] Opening interface: {GetType().Name}");

            _playlist = VLCPlaylist.Create(intfPtr, _context.Logger);
            _player = VLCPlayer.Create(intfPtr, _context.Logger);
            if (_playlist is null || _player is null)
                return -1;

            if (!OnOpen())
            {
                _context.Logger.Error($"[VLCLR] Interface {GetType().Name} OnOpen() returned false");
                return -1;
            }

            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                _context.Logger.Error($"[VLCLR] Exception in interface Open: {ex.Message}");
            }
            catch
            {
                // Ignore logging failures
            }
            return -1;
        }
    }

    /// <summary>
    /// Internal: Called by the Close callback to clean up.
    /// </summary>
    /// <param name="intfPtr">Pointer to intf_thread_t</param>
    public void InternalClose(nint intfPtr)
    {
        try
        {
            _context.Logger.Info($"[VLCLR] Closing interface: {GetType().Name}");
            OnClose();
        }
        catch (Exception ex)
        {
            try
            {
                _context.Logger.Error($"[VLCLR] Exception in interface Close: {ex.Message}");
            }
            catch
            {
                // Ignore logging failures
            }
        }
    }

    /// <summary>
    /// Disposes the interface instance.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes managed resources.
    /// </summary>
    /// <param name="disposing">True if called from Dispose()</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _player?.Dispose();
            _playlist?.Dispose();
        }

        _disposed = true;
    }
}
