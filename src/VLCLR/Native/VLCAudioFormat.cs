// VLC audio format structure
// Source: vlc/include/vlc_es.h
// VLC Version: 4.0.6

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Native <c>audio_format_t</c> embedded in the ES-format union. Member offsets
/// come from <see cref="Interop.AudioFormat.Offsets"/>.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Interop.AudioFormat.MaxSizeOf)]
public struct VLCAudioFormat
{
    /// <summary>Audio format fourcc (i_format)</summary>
    [FieldOffset(Interop.AudioFormat.Offsets.Format)] public uint Format;

    /// <summary>Sample rate (i_rate)</summary>
    [FieldOffset(Interop.AudioFormat.Offsets.Rate)] public uint Rate;

    /// <summary>Physical channels bitmask (i_physical_channels)</summary>
    [FieldOffset(Interop.AudioFormat.Offsets.PhysicalChannels)] public ushort PhysicalChannels;

    /// <summary>Channel mode (i_chan_mode)</summary>
    [FieldOffset(Interop.AudioFormat.Offsets.ChanMode)] public ushort ChannelMode;

    /// <summary>Channel type (channel_type)</summary>
    [FieldOffset(Interop.AudioFormat.Offsets.ChannelType)] public int ChannelType;

    /// <summary>Bytes per frame (i_bytes_per_frame)</summary>
    [FieldOffset(Interop.AudioFormat.Offsets.BytesPerFrame)] public uint BytesPerFrame;

    /// <summary>Frame length in samples (i_frame_length)</summary>
    [FieldOffset(Interop.AudioFormat.Offsets.FrameLength)] public uint FrameLength;

    /// <summary>Bits per sample (i_bitspersample)</summary>
    [FieldOffset(Interop.AudioFormat.Offsets.Bitspersample)] public uint BitsPerSample;

    /// <summary>Block alignment (i_blockalign)</summary>
    [FieldOffset(Interop.AudioFormat.Offsets.Blockalign)] public uint BlockAlign;

    /// <summary>Channel count (i_channels)</summary>
    [FieldOffset(Interop.AudioFormat.Offsets.Channels)] public byte Channels;
}
