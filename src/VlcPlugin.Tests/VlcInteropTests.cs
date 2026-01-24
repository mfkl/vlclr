using System.Runtime.InteropServices;
using Xunit;

namespace VlcPlugin.Tests;

/// <summary>
/// Tests for VlcInterop UTF-8 string marshalling helpers.
/// </summary>
public class VlcInteropTests
{
    [Fact]
    public void PtrToStringUtf8_WithNullPointer_ReturnsNull()
    {
        string? result = VlcInterop.PtrToStringUtf8(nint.Zero);
        Assert.Null(result);
    }

    [Fact]
    public void PtrToStringUtf8_WithEmptyString_ReturnsEmptyString()
    {
        // Allocate a single null terminator
        nint ptr = Marshal.AllocHGlobal(1);
        try
        {
            Marshal.WriteByte(ptr, 0);
            string? result = VlcInterop.PtrToStringUtf8(ptr);
            Assert.Equal(string.Empty, result);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [Fact]
    public void PtrToStringUtf8_WithAsciiString_ReturnsCorrectString()
    {
        byte[] bytes = "Hello"u8.ToArray();
        nint ptr = Marshal.AllocHGlobal(bytes.Length + 1);
        try
        {
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            Marshal.WriteByte(ptr + bytes.Length, 0); // Null terminator

            string? result = VlcInterop.PtrToStringUtf8(ptr);
            Assert.Equal("Hello", result);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [Fact]
    public void PtrToStringUtf8_WithUnicodeString_ReturnsCorrectString()
    {
        // UTF-8 encoding of "Héllo Wörld"
        byte[] bytes = "Héllo Wörld"u8.ToArray();
        nint ptr = Marshal.AllocHGlobal(bytes.Length + 1);
        try
        {
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            Marshal.WriteByte(ptr + bytes.Length, 0); // Null terminator

            string? result = VlcInterop.PtrToStringUtf8(ptr);
            Assert.Equal("Héllo Wörld", result);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [Fact]
    public void StringToUtf8Ptr_WithNullString_ReturnsZero()
    {
        nint result = VlcInterop.StringToUtf8Ptr(null);
        Assert.Equal(nint.Zero, result);
    }

    [Fact]
    public void StringToUtf8Ptr_WithEmptyString_ReturnsValidPointer()
    {
        nint ptr = VlcInterop.StringToUtf8Ptr(string.Empty);
        try
        {
            Assert.NotEqual(nint.Zero, ptr);
            // Should contain just the null terminator
            byte b = Marshal.ReadByte(ptr);
            Assert.Equal(0, b);
        }
        finally
        {
            if (ptr != nint.Zero)
                Marshal.FreeHGlobal(ptr);
        }
    }

    [Fact]
    public void StringToUtf8Ptr_WithAsciiString_ReturnsCorrectBytes()
    {
        nint ptr = VlcInterop.StringToUtf8Ptr("Test");
        try
        {
            Assert.NotEqual(nint.Zero, ptr);

            // Verify the bytes
            Assert.Equal((byte)'T', Marshal.ReadByte(ptr, 0));
            Assert.Equal((byte)'e', Marshal.ReadByte(ptr, 1));
            Assert.Equal((byte)'s', Marshal.ReadByte(ptr, 2));
            Assert.Equal((byte)'t', Marshal.ReadByte(ptr, 3));
            Assert.Equal(0, Marshal.ReadByte(ptr, 4)); // Null terminator
        }
        finally
        {
            if (ptr != nint.Zero)
                Marshal.FreeHGlobal(ptr);
        }
    }

    [Fact]
    public void StringToUtf8Ptr_Roundtrip_PreservesString()
    {
        const string original = "Hello, World! 你好世界";

        nint ptr = VlcInterop.StringToUtf8Ptr(original);
        try
        {
            Assert.NotEqual(nint.Zero, ptr);
            string? result = VlcInterop.PtrToStringUtf8(ptr);
            Assert.Equal(original, result);
        }
        finally
        {
            if (ptr != nint.Zero)
                Marshal.FreeHGlobal(ptr);
        }
    }

    [Fact]
    public void StringToUtf8Ptr_WithSpecialCharacters_RoundtripsCorrectly()
    {
        // Test various Unicode categories
        string[] testStrings =
        [
            "café",           // Latin with diacritics
            "日本語",         // Japanese
            "😀🎉",           // Emoji
            "Привет",         // Cyrillic
            "مرحبا",          // Arabic
            "שלום",           // Hebrew
        ];

        foreach (string original in testStrings)
        {
            nint ptr = VlcInterop.StringToUtf8Ptr(original);
            try
            {
                string? result = VlcInterop.PtrToStringUtf8(ptr);
                Assert.Equal(original, result);
            }
            finally
            {
                if (ptr != nint.Zero)
                    Marshal.FreeHGlobal(ptr);
            }
        }
    }
}
