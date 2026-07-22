// VLC timed data block
// Source: vlc/include/vlc_frame.h and vlc_block.h
// VLC Version: 4.0.6

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Prefix of VLC's <c>block_t</c>/<c>vlc_frame_t</c> used by audio filters.
/// The fields through <see cref="Length"/> are ABI-stable and sufficient for
/// observing decoded PCM without taking ownership of the block.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 72)]
public struct VLCBlock
{
    [FieldOffset(0)] public nint Next;
    [FieldOffset(8)] public nint Buffer;
    [FieldOffset(16)] public nuint BufferLength;
    [FieldOffset(24)] public nint AllocationStart;
    [FieldOffset(32)] public nuint AllocationSize;
    [FieldOffset(40)] public uint Flags;
    [FieldOffset(44)] public uint SampleCount;
    [FieldOffset(48)] public long PresentationTimestamp;
    [FieldOffset(56)] public long DecodeTimestamp;
    [FieldOffset(64)] public long Length;
}

public static class VLCBlockFlags
{
    public const uint Discontinuity = 0x0001;
    public const uint Corrupted = 0x0400;
}
