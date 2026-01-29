using System.Runtime.InteropServices;
using VLCLR.Types;
using VLCLR.Native;

namespace VLCLR;

/// <summary>
/// High-level wrapper for VLC variable system.
/// Provides a clean C# API for creating and managing VLC variables.
/// Uses direct P/Invoke to libvlccore.
/// </summary>
public sealed class VLCVariable
{
    private readonly nint _vlcObject;

    /// <summary>
    /// Creates a variable manager bound to a specific VLC object.
    /// </summary>
    /// <param name="vlcObject">Pointer to vlc_object_t</param>
    public VLCVariable(nint vlcObject)
    {
        _vlcObject = vlcObject;
    }

    /// <summary>
    /// Create a new integer variable.
    /// </summary>
    /// <param name="name">Variable name</param>
    /// <param name="initialValue">Initial value (default: 0)</param>
    /// <returns>True if created successfully</returns>
    public bool CreateInteger(string name, long initialValue = 0)
    {
        int result = VLCCore.VarCreate(_vlcObject, name, VLCVarType.Integer);
        if (result == 0)
        {
            var value = new VLCValueNative { Integer = initialValue };
            VLCCore.VarSetChecked(_vlcObject, name, VLCVarType.Integer, value);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Create a new string variable.
    /// </summary>
    /// <param name="name">Variable name</param>
    /// <param name="initialValue">Initial value (default: null)</param>
    /// <returns>True if created successfully</returns>
    public bool CreateString(string name, string? initialValue = null)
    {
        int result = VLCCore.VarCreate(_vlcObject, name, VLCVarType.String);
        if (result == 0 && initialValue != null)
        {
            nint strPtr = Marshal.StringToCoTaskMemUTF8(initialValue);
            try
            {
                var value = new VLCValueNative { String = strPtr };
                VLCCore.VarSetChecked(_vlcObject, name, VLCVarType.String, value);
            }
            finally
            {
                Marshal.FreeCoTaskMem(strPtr);
            }
            return true;
        }
        return result == 0;
    }

    /// <summary>
    /// Destroy a variable.
    /// </summary>
    /// <param name="name">Variable name</param>
    public void Destroy(string name)
    {
        VLCCore.VarDestroy(_vlcObject, name);
    }

    /// <summary>
    /// Set an integer variable value.
    /// </summary>
    /// <param name="name">Variable name</param>
    /// <param name="value">Value to set</param>
    /// <returns>True if set successfully</returns>
    public bool SetInteger(string name, long value)
    {
        var vlcValue = new VLCValueNative { Integer = value };
        return VLCCore.VarSetChecked(_vlcObject, name, VLCVarType.Integer, vlcValue) == 0;
    }

    /// <summary>
    /// Get an integer variable value.
    /// </summary>
    /// <param name="name">Variable name</param>
    /// <returns>The variable value, or 0 if not found</returns>
    public long GetInteger(string name)
    {
        int result = VLCCore.VarGetChecked(_vlcObject, name, VLCVarType.Integer, out VLCValueNative value);
        return result == 0 ? value.Integer : 0;
    }

    /// <summary>
    /// Set a string variable value.
    /// </summary>
    /// <param name="name">Variable name</param>
    /// <param name="value">Value to set</param>
    /// <returns>True if set successfully</returns>
    public bool SetString(string name, string? value)
    {
        nint strPtr = value != null ? Marshal.StringToCoTaskMemUTF8(value) : nint.Zero;
        try
        {
            var vlcValue = new VLCValueNative { String = strPtr };
            return VLCCore.VarSetChecked(_vlcObject, name, VLCVarType.String, vlcValue) == 0;
        }
        finally
        {
            if (strPtr != nint.Zero)
                Marshal.FreeCoTaskMem(strPtr);
        }
    }

    /// <summary>
    /// Get a string variable value.
    /// </summary>
    /// <param name="name">Variable name</param>
    /// <returns>The variable value, or null if not found</returns>
    public string? GetString(string name)
    {
        int result = VLCCore.VarGetChecked(_vlcObject, name, VLCVarType.String, out VLCValueNative value);
        if (result != 0 || value.String == nint.Zero)
            return null;

        // Marshal the string and free the native memory (VLC allocates this)
        string? strResult = Marshal.PtrToStringUTF8(value.String);
        VLCCore.Free(value.String);
        return strResult;
    }
}
