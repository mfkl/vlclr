// VLC audio format structure
// Source: vlc/include/vlc_es.h
// VLC Version: 4.0.6

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>Native <c>audio_format_t</c> embedded in the ES-format union.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct VLCAudioFormat
{
    public uint Format;
    public uint Rate;
    public ushort PhysicalChannels;
    public ushort ChannelMode;
    public int ChannelType;
    public uint BytesPerFrame;
    public uint FrameLength;
    public uint BitsPerSample;
    public uint BlockAlign;
    public byte Channels;
}
