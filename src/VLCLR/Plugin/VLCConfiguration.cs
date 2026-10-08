using System.Runtime.InteropServices;
using VLCLR.Native;
using VLCLR.Types;

namespace VLCLR.Plugin;

/// <summary>
/// Reads typed plugin options through VLC's variable inheritance system.
/// </summary>
/// <remarks>
/// Values are resolved from the current VLC object, its parents, and finally
/// the module configuration. Generated plugin configuration properties use
/// this type and retain the attribute default as a fallback.
/// </remarks>
public readonly struct VLCConfiguration
{
    private readonly nint _objectPtr;

    /// <summary>
    /// Creates a configuration reader bound to a VLC object.
    /// </summary>
    public VLCConfiguration(nint objectPtr)
    {
        _objectPtr = objectPtr;
    }

    /// <summary>Gets an inherited boolean value.</summary>
    public bool GetBool(string name, bool fallback = false)
    {
        if (_objectPtr == 0)
            return fallback;

        int result = VLCCore.VarInherit(_objectPtr, name, VLCVarType.Bool, out VLCValueNative value);
        return result == 0 ? value.Bool != 0 : fallback;
    }

    /// <summary>Gets an inherited 64-bit integer value.</summary>
    public long GetInteger(string name, long fallback = 0)
    {
        if (_objectPtr == 0)
            return fallback;

        int result = VLCCore.VarInherit(_objectPtr, name, VLCVarType.Integer, out VLCValueNative value);
        return result == 0 ? value.Integer : fallback;
    }

    /// <summary>Gets an inherited single-precision floating-point value.</summary>
    public float GetFloat(string name, float fallback = 0)
    {
        if (_objectPtr == 0)
            return fallback;

        int result = VLCCore.VarInherit(_objectPtr, name, VLCVarType.Float, out VLCValueNative value);
        return result == 0 ? value.Float : fallback;
    }

    /// <summary>
    /// Gets an inherited UTF-8 string and releases the VLC-owned copy.
    /// </summary>
    public string? GetString(string name, string? fallback = null)
    {
        if (_objectPtr == 0)
            return fallback;

        int result = VLCCore.VarInherit(_objectPtr, name, VLCVarType.String, out VLCValueNative value);
        if (result != 0 || value.String == 0)
            return fallback;

        try
        {
            return Marshal.PtrToStringUTF8(value.String) ?? fallback;
        }
        finally
        {
            VLCCore.Free(value.String);
        }
    }
}
