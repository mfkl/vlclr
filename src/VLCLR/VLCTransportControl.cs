using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using VLCLR.Native;

namespace VLCLR;

/// <summary>
/// Provides ownership-safe transport controls for a VLC 3 playlist.
/// </summary>
/// <remarks>
/// This abstraction targets the VLC 3 ABI on Windows x64 and Linux x64. VLC
/// exposes <c>playlist_Control</c> and <c>input_Control</c> as variadic C
/// functions, which must not be represented as ordinary fixed P/Invokes. Input
/// commands use VLC's fixed-signature <c>input_vaControl</c> export with a
/// <see cref="VLCVaList"/>. <c>playlist_Control</c> has no exported
/// <c>va_list</c> form, so its integer-only commands are dispatched through a
/// fixed function pointer: on both targets those arguments travel in the same
/// registers as a variadic call. On System V the callee only tests whether AL
/// is zero to decide whether to spill vector registers, which no argument uses.
///
/// The playlist pointer supplied to the constructor is borrowed and must stay
/// valid for this object's lifetime. Current-input references are acquired via
/// <see cref="VLCCore.PlaylistCurrentInput"/> and released before every method
/// returns; callers never own those references.
/// </remarks>
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
public sealed unsafe class VLCTransportControl
{
    /// <summary>The nominal VLC input rate value (1.0x playback).</summary>
    public const int NominalPlaybackRate = 1000;

    /// <summary>The slowest playback multiplier supported by VLC 3.</summary>
    public const double MinimumPlaybackRate = 1.0 / 32.0;

    /// <summary>The fastest playback multiplier supported by VLC 3.</summary>
    public const double MaximumPlaybackRate = 31.25;

    private const int PlaylistPlayQuery = 0;
    private const int PlaylistTogglePauseQuery = 2;
    private const int PlaylistStopQuery = 3;
    private const int InputGetPositionQuery = 0;
    private const int InputSetPositionQuery = 1;
    private const int InputGetRateQuery = 5;
    private const int InputSetRateQuery = 6;
    private const int InputRateMinimum = 32;
    private const int InputRateMaximum = 32000;

    private readonly nint _playlist;

    /// <summary>Creates controls for a borrowed, live VLC playlist pointer.</summary>
    /// <exception cref="ArgumentException">The playlist pointer is null.</exception>
    public VLCTransportControl(nint playlist)
    {
        if (playlist == nint.Zero)
            throw new ArgumentException("A VLC playlist pointer is required.", nameof(playlist));

        _playlist = playlist;
    }

    /// <summary>Starts the selected playlist item, if one is available.</summary>
    public void Play() => PlaylistControl(PlaylistPlayQuery);

    /// <summary>Toggles between paused and playing states.</summary>
    public void TogglePause() => PlaylistControl(PlaylistTogglePauseQuery);

    /// <summary>Stops playlist playback.</summary>
    public void Stop() => PlaylistControl(PlaylistStopQuery);

    /// <summary>Gets the current normalized position, from 0.0 through 1.0.</summary>
    /// <returns><see langword="false"/> when VLC has no current input or rejects the query.</returns>
    public bool TryGetNormalizedPosition(out double position)
    {
        // INPUT_GET_POSITION expects a double* vararg: the slot holds &result.
        double result = 0;
        int status = InputControlForCurrentInput(InputGetPositionQuery, (long)&result);
        position = result;
        return status == 0;
    }

    /// <summary>Sets the current input's normalized position, from 0.0 through 1.0.</summary>
    /// <returns><see langword="false"/> when VLC has no current input or rejects the request.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The position is not finite or outside 0.0 through 1.0.</exception>
    public bool TrySetNormalizedPosition(double position)
    {
        if (!double.IsFinite(position) || position < 0 || position > 1)
            throw new ArgumentOutOfRangeException(nameof(position), "Position must be finite and between 0.0 and 1.0.");

        // INPUT_SET_POSITION consumes a double value, not a pointer to one.
        return InputControlForCurrentInput(InputSetPositionQuery, VLCVaList.Double(position)) == 0;
    }

    /// <summary>Gets the current playback multiplier (1.0 is nominal speed).</summary>
    /// <returns><see langword="false"/> when VLC has no current input or rejects the query.</returns>
    public bool TryGetPlaybackRate(out double playbackRate)
    {
        // INPUT_GET_RATE expects an int* vararg: the slot holds &nativeRate.
        int nativeRate = 0;
        int status = InputControlForCurrentInput(InputGetRateQuery, (long)&nativeRate);
        playbackRate = nativeRate == 0 ? 0 : (double)NominalPlaybackRate / nativeRate;
        return status == 0;
    }

    /// <summary>Sets the playback multiplier (1.0 is nominal speed).</summary>
    /// <returns><see langword="false"/> when VLC has no current input or rejects the request.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The rate is not finite or outside VLC 3's supported range.</exception>
    public bool TrySetPlaybackRate(double playbackRate)
    {
        if (!double.IsFinite(playbackRate) || playbackRate < MinimumPlaybackRate || playbackRate > MaximumPlaybackRate)
            throw new ArgumentOutOfRangeException(nameof(playbackRate), $"Playback rate must be finite and between {MinimumPlaybackRate} and {MaximumPlaybackRate}.");

        int nativeRate = Math.Clamp(
            (int)Math.Round(NominalPlaybackRate / playbackRate, MidpointRounding.AwayFromZero),
            InputRateMinimum,
            InputRateMaximum);

        // INPUT_SET_RATE consumes an int value, widened to its 8-byte slot.
        return InputControlForCurrentInput(InputSetRateQuery, nativeRate) == 0;
    }

    private int InputControlForCurrentInput(int query, long argument)
    {
        VLCPlatform.EnsureSupported(nameof(VLCTransportControl));
        nint input = VLCCore.PlaylistCurrentInput(_playlist);
        if (input == nint.Zero)
            return -1;

        try
        {
            return VLCCore.InputControl(input, query, argument);
        }
        finally
        {
            VLCCore.ObjectRelease(input);
        }
    }

    private void PlaylistControl(int query)
    {
        VLCPlatform.EnsureSupported(nameof(VLCTransportControl));
        PlaylistControlExport.Value(_playlist, query, 0);
    }

    private static class PlaylistControlExport
    {
        internal static readonly delegate* unmanaged[Cdecl]<nint, int, int, void> Value =
            (delegate* unmanaged[Cdecl]<nint, int, int, void>)NativeLibrary.GetExport(VLCCore.Library, "playlist_Control");
    }
}
