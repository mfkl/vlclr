using System.Text;

namespace VLCLR;

/// <summary>
/// Helper methods for VLC type marshalling.
/// </summary>
public static class VLCInterop
{
    /// <summary>
    /// Marshal a UTF-8 string from native memory.
    /// </summary>
    public static string? PtrToStringUtf8(nint ptr)
    {
        if (ptr == nint.Zero)
            return null;

        // Find string length
        int length = 0;
        unsafe
        {
            byte* p = (byte*)ptr;
            while (p[length] != 0) length++;
        }

        if (length == 0)
            return string.Empty;

        unsafe
        {
            return Encoding.UTF8.GetString((byte*)ptr, length);
        }
    }

    /// <summary>
    /// Allocate a UTF-8 string in native memory.
    /// Caller must free with Marshal.FreeHGlobal.
    /// </summary>
    public static nint StringToUtf8Ptr(string? str)
    {
        if (str == null)
            return nint.Zero;

        byte[] bytes = Encoding.UTF8.GetBytes(str + '\0');
        nint ptr = System.Runtime.InteropServices.Marshal.AllocHGlobal(bytes.Length);
        System.Runtime.InteropServices.Marshal.Copy(bytes, 0, ptr, bytes.Length);
        return ptr;
    }
}
