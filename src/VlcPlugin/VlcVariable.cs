using System.Runtime.InteropServices;
using VlcPlugin.Types;
using VlcPlugin.Native;

namespace VlcPlugin;

/// <summary>
/// High-level wrapper for VLC variable system.
/// Provides a clean C# API for creating and managing VLC variables.
/// Uses direct P/Invoke to libvlccore.
/// </summary>
public sealed class VlcVariable
{
    private readonly nint _vlcObject;

    /// <summary>
    /// Creates a variable manager bound to a specific VLC object.
    /// </summary>
    /// <param name="vlcObject">Pointer to vlc_object_t</param>
    public VlcVariable(nint vlcObject)
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
        int result = VlcCore.VarCreate(_vlcObject, name, VlcVarType.Integer);
        if (result == 0)
        {
            var value = new VlcValueNative { Integer = initialValue };
            VlcCore.VarSetChecked(_vlcObject, name, VlcVarType.Integer, value);
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
        int result = VlcCore.VarCreate(_vlcObject, name, VlcVarType.String);
        if (result == 0 && initialValue != null)
        {
            nint strPtr = Marshal.StringToCoTaskMemUTF8(initialValue);
            try
            {
                var value = new VlcValueNative { String = strPtr };
                VlcCore.VarSetChecked(_vlcObject, name, VlcVarType.String, value);
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
        VlcCore.VarDestroy(_vlcObject, name);
    }

    /// <summary>
    /// Set an integer variable value.
    /// </summary>
    /// <param name="name">Variable name</param>
    /// <param name="value">Value to set</param>
    /// <returns>True if set successfully</returns>
    public bool SetInteger(string name, long value)
    {
        var vlcValue = new VlcValueNative { Integer = value };
        return VlcCore.VarSetChecked(_vlcObject, name, VlcVarType.Integer, vlcValue) == 0;
    }

    /// <summary>
    /// Get an integer variable value.
    /// </summary>
    /// <param name="name">Variable name</param>
    /// <returns>The variable value, or 0 if not found</returns>
    public long GetInteger(string name)
    {
        int result = VlcCore.VarGetChecked(_vlcObject, name, VlcVarType.Integer, out VlcValueNative value);
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
            var vlcValue = new VlcValueNative { String = strPtr };
            return VlcCore.VarSetChecked(_vlcObject, name, VlcVarType.String, vlcValue) == 0;
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
        int result = VlcCore.VarGetChecked(_vlcObject, name, VlcVarType.String, out VlcValueNative value);
        if (result != 0 || value.String == nint.Zero)
            return null;

        // Marshal the string and free the native memory (VLC allocates this)
        string? strResult = Marshal.PtrToStringUTF8(value.String);
        VlcCore.Free(value.String);
        return strResult;
    }
}
