using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>Typed VLC 3 control calls using a separately built native bridge.</summary>
/// <remarks>
/// The caller owns the core library and all VLC pointers and must keep them live
/// through each call. GetVouts returns VLC-owned references/array using VLC's
/// usual release rules. ViewPlay requires the caller to hold the playlist lock.
/// This bridge does not select a decoder or qualify a playback backend.
/// </remarks>
public sealed unsafe class VLC3ControlBridge : IDisposable
{
    private readonly object _gate = new();
    private nint _library;
    private readonly nint _inputControl, _playlistControl;
    private readonly delegate* unmanaged[Cdecl]<nint, nint, double*, int> _getPosition;
    private readonly delegate* unmanaged[Cdecl]<nint, nint, double, int> _setPosition;
    private readonly delegate* unmanaged[Cdecl]<nint, nint, int*, int> _getRate;
    private readonly delegate* unmanaged[Cdecl]<nint, nint, int, int> _setRate;
    private readonly delegate* unmanaged[Cdecl]<nint, nint, nint*, nuint*, int> _getVouts;
    private readonly delegate* unmanaged[Cdecl]<nint, nint, void> _play, _pause, _stop;
    private readonly delegate* unmanaged[Cdecl]<nint, nint, int, void> _skip;
    private readonly delegate* unmanaged[Cdecl]<nint, nint, nint, nint, void> _viewPlay;

    /// <param name="bridgePath">Absolute path to the native bridge built for this process.</param>
    /// <param name="coreLibrary">Borrowed NativeLibrary handle for the selected VLC 3 core.</param>
    public VLC3ControlBridge(string bridgePath, nint coreLibrary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bridgePath);
        if (!Path.IsPathFullyQualified(bridgePath)) throw new ArgumentException("An absolute bridge path is required.", nameof(bridgePath));
        if (coreLibrary == 0) throw new ArgumentException("A live core library handle is required.", nameof(coreLibrary));
        if (IntPtr.Size != 8) throw new PlatformNotSupportedException("The control bridge requires a 64-bit process.");
        _inputControl = NativeLibrary.GetExport(coreLibrary, "input_vaControl");
        _playlistControl = NativeLibrary.GetExport(coreLibrary, "playlist_Control");
        _library = NativeLibrary.Load(bridgePath);
        try
        {
            var version = (delegate* unmanaged[Cdecl]<int>)Export("vlclr_control_bridge_version");
            if (version() != 1) throw new NotSupportedException("Unsupported VLC 3 control bridge ABI.");
            _getPosition = (delegate* unmanaged[Cdecl]<nint, nint, double*, int>)Export("vlclr_input_get_position");
            _setPosition = (delegate* unmanaged[Cdecl]<nint, nint, double, int>)Export("vlclr_input_set_position");
            _getRate = (delegate* unmanaged[Cdecl]<nint, nint, int*, int>)Export("vlclr_input_get_rate");
            _setRate = (delegate* unmanaged[Cdecl]<nint, nint, int, int>)Export("vlclr_input_set_rate");
            _getVouts = (delegate* unmanaged[Cdecl]<nint, nint, nint*, nuint*, int>)Export("vlclr_input_get_vouts");
            _play = (delegate* unmanaged[Cdecl]<nint, nint, void>)Export("vlclr_playlist_play");
            _pause = (delegate* unmanaged[Cdecl]<nint, nint, void>)Export("vlclr_playlist_pause");
            _stop = (delegate* unmanaged[Cdecl]<nint, nint, void>)Export("vlclr_playlist_stop");
            _skip = (delegate* unmanaged[Cdecl]<nint, nint, int, void>)Export("vlclr_playlist_skip");
            _viewPlay = (delegate* unmanaged[Cdecl]<nint, nint, nint, nint, void>)Export("vlclr_playlist_viewplay");
        }
        catch { NativeLibrary.Free(_library); _library = 0; throw; }
    }

    private nint Export(string name) => NativeLibrary.GetExport(_library, name);
    private void Check(nint target)
    {
        ObjectDisposedException.ThrowIf(_library == 0, this);
        if (target == 0) throw new ArgumentException("A live VLC target is required.", nameof(target));
    }

    public int GetPosition(nint input, out double position)
    {
        lock (_gate) { Check(input); double value = 0; int result = _getPosition(_inputControl, input, &value); position = value; return result; }
    }
    public int SetPosition(nint input, double position)
    {
        if (!double.IsFinite(position) || position < 0 || position > 1) throw new ArgumentOutOfRangeException(nameof(position));
        lock (_gate) { Check(input); return _setPosition(_inputControl, input, position); }
    }
    public int GetRate(nint input, out int nativeRate)
    {
        lock (_gate) { Check(input); int value = 0; int result = _getRate(_inputControl, input, &value); nativeRate = value; return result; }
    }
    public int SetRate(nint input, int nativeRate)
    {
        if (nativeRate < 32 || nativeRate > 32000) throw new ArgumentOutOfRangeException(nameof(nativeRate));
        lock (_gate) { Check(input); return _setRate(_inputControl, input, nativeRate); }
    }
    public int GetVouts(nint input, out nint outputs, out nuint count)
    {
        lock (_gate) { Check(input); nint value = 0; nuint length = 0; int result = _getVouts(_inputControl, input, &value, &length); outputs = value; count = length; return result; }
    }
    public void Play(nint playlist) { lock (_gate) { Check(playlist); _play(_playlistControl, playlist); } }
    public void TogglePause(nint playlist) { lock (_gate) { Check(playlist); _pause(_playlistControl, playlist); } }
    public void Stop(nint playlist) { lock (_gate) { Check(playlist); _stop(_playlistControl, playlist); } }
    public void Skip(nint playlist, int offset) { lock (_gate) { Check(playlist); _skip(_playlistControl, playlist, offset); } }
    public void ViewPlay(nint playlist, nint node, nint item)
    {
        lock (_gate) { Check(playlist); _viewPlay(_playlistControl, playlist, node, item); }
    }
    public void Dispose()
    {
        lock (_gate) { if (_library == 0) return; NativeLibrary.Free(_library); _library = 0; }
    }
}
