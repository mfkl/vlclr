using System.Runtime.InteropServices;

namespace LiveAudioTranslator.VisualTest;

internal static partial class NativeVlcTestConfiguration
{
    public static void SetMediaPlayerAudioFilter(nint mediaPlayer, string value)
    {
        // libvlc_media_player_t starts with vlc_object_t (24 bytes on win-x64)
        // and vlc_atomic_rc_t (8 bytes), followed by vlc_player_t*. LibVLC 4
        // creates its audio-filter variable without inheritance, so command
        // line/config values cannot attach an arbitrary filter to this aout.
        // The stock core helper is the same path VLC uses for runtime filter
        // changes and requires no VLC patch.
        const int VlcPlayerOffset = 32;
        nint vlcPlayer = Marshal.ReadIntPtr(mediaPlayer, VlcPlayerOffset);
        if (vlcPlayer == 0)
            throw new InvalidOperationException("LibVLC media player has no core player.");
        int result = PlayerAudioOutputEnableFilter(vlcPlayer, value, true);
        if (result != 0)
            throw new InvalidOperationException($"VLC rejected audio filter ({result}).");
    }

    [LibraryImport(
        "libvlccore",
        EntryPoint = "vlc_player_aout_EnableFilter",
        StringMarshalling = StringMarshalling.Utf8)]
    private static partial int PlayerAudioOutputEnableFilter(
        nint player,
        string name,
        [MarshalAs(UnmanagedType.I1)] bool add);
}
