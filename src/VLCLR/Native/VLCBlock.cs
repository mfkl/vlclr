// VLC timed data block
// Source: vlc/include/vlc_frame.h and vlc_block.h
// VLC Version: 4.0.6

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Prefix of VLC's <c>block_t</c>/<c>vlc_frame_t</c> (<see cref="Interop.Frame"/>) used by
/// audio filters: every member before <c>ancillaries</c>. It is sufficient for
/// observing decoded PCM without taking ownership of the block.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Interop.Frame.Offsets.Ancillaries)]
public struct VLCBlock
{
    [FieldOffset(Interop.Frame.Offsets.Next)] public nint Next;
    [FieldOffset(Interop.Frame.Offsets.Buffer)] public nint Buffer;
    [FieldOffset(Interop.Frame.Offsets.BufferSize)] public nuint BufferLength;
    [FieldOffset(Interop.Frame.Offsets.Start)] public nint AllocationStart;
    [FieldOffset(Interop.Frame.Offsets.Size)] public nuint AllocationSize;
    [FieldOffset(Interop.Frame.Offsets.Flags)] public uint Flags;
    [FieldOffset(Interop.Frame.Offsets.NbSamples)] public uint SampleCount;
    [FieldOffset(Interop.Frame.Offsets.Pts)] public long PresentationTimestamp;
    [FieldOffset(Interop.Frame.Offsets.Dts)] public long DecodeTimestamp;
    [FieldOffset(Interop.Frame.Offsets.Length)] public long Length;
}

public static class VLCBlockFlags
{
    /// <summary>VLC_FRAME_FLAG_DISCONTINUITY</summary>
    public const uint Discontinuity = (uint)Interop.VlcFrame.FrameFlagDiscontinuity;

    /// <summary>VLC_FRAME_FLAG_CORRUPTED</summary>
    public const uint Corrupted = (uint)Interop.VlcFrame.FrameFlagCorrupted;
}
