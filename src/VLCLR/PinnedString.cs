using System.Runtime.InteropServices;
using System.Text;

namespace VLCLR;

/// <summary>
/// Helper class to keep UTF-8 strings pinned in memory for VLC callbacks.
/// For static strings (like API version), use without disposing - they live for process lifetime.
/// For temporary strings, dispose after use.
/// </summary>
public sealed class PinnedString : IDisposable
{
    private readonly byte[] _bytes;
    private readonly GCHandle _handle;

    /// <summary>
    /// Gets the pointer to the null-terminated UTF-8 string.
    /// </summary>
    public nint Pointer { get; }

    /// <summary>
    /// Creates a pinned UTF-8 string from a C# string.
    /// </summary>
    /// <param name="value">The string to pin</param>
    public PinnedString(string value)
    {
        _bytes = Encoding.UTF8.GetBytes(value + '\0');
        _handle = GCHandle.Alloc(_bytes, GCHandleType.Pinned);
        Pointer = _handle.AddrOfPinnedObject();
    }

    /// <summary>
    /// Releases the pinned memory.
    /// </summary>
    public void Dispose()
    {
        if (_handle.IsAllocated)
            _handle.Free();
    }
}
