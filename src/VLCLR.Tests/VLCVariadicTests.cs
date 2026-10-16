using System.Runtime.InteropServices;
using System.Text;
using VLCLR.Module;
using Xunit;

namespace VLCLR.Tests;

/// <summary>
/// Calls the C library's snprintf through <see cref="VLCVariadic"/>: like
/// VLC's vlc_set it is variadic after three named arguments, so it reads its
/// variable arguments the way vlc_set does on the ABI the tests run on.
/// </summary>
public unsafe class VLCVariadicTests
{
    [Fact]
    public void IntegerAndPointerArguments_ReachTheCallee()
    {
        fixed (byte* text = "abc\0"u8)
        {
            nint textPtr = (nint)text;
            Assert.Equal("abc -7", Format("%s %d", (fn, buffer, size, format) =>
                VLCVariadic.Call(fn, buffer, size, format, textPtr, -7)));
        }
    }

    [Fact]
    public void TwoIntegerArguments_ReachTheCallee()
    {
        Assert.Equal("42 -1", Format("%d %d", (fn, buffer, size, format) =>
            VLCVariadic.Call(fn, buffer, size, format, 42, -1)));
    }

    [Fact]
    public void DoubleArguments_ReachTheCallee()
    {
        Assert.Equal("1.50 -2.25", Format("%.2f %.2f", (fn, buffer, size, format) =>
            VLCVariadic.CallDouble(fn, buffer, size, format, 1.5, -2.25)));
    }

    [Fact]
    public void OneDoubleArgument_ReachesTheCallee()
    {
        Assert.Equal("0.75", Format("%.2f", (fn, buffer, size, format) =>
            VLCVariadic.CallDouble(fn, buffer, size, format, 0.75)));
    }

    private delegate int SnprintfCall(nint function, nint buffer, nint size, nint format);

    private static string Format(string format, SnprintfCall call)
    {
        byte* buffer = stackalloc byte[64];
        byte[] formatBytes = Encoding.ASCII.GetBytes(format + "\0");
        fixed (byte* formatPtr = formatBytes)
        {
            int length = call(Snprintf(), (nint)buffer, 64, (nint)formatPtr);
            Assert.InRange(length, 0, 63);
            return Encoding.ASCII.GetString(buffer, length);
        }
    }

    private static nint Snprintf()
    {
        if (OperatingSystem.IsWindows())
            return NativeLibrary.GetExport(NativeLibrary.Load("msvcrt.dll"), "_snprintf");
        if (OperatingSystem.IsMacOS())
            return NativeLibrary.GetExport(NativeLibrary.Load("/usr/lib/libSystem.B.dylib"), "snprintf");
        return NativeLibrary.GetExport(NativeLibrary.Load("libc.so.6"), "snprintf");
    }
}
