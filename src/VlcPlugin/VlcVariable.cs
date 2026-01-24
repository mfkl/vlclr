using System.Runtime.InteropServices;
using VlcPlugin.Generated;
using VlcPlugin.Native;

namespace VlcPlugin;

/// <summary>
/// High-level wrapper for VLC variable system.
/// Provides a clean C# API for creating and managing VLC variables.
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
        int result = VlcBridge.VarCreate(_vlcObject, name, VlcVarType.Integer);
        if (result == 0)
        {
            VlcBridge.VarSetInteger(_vlcObject, name, initialValue);
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
        int result = VlcBridge.VarCreate(_vlcObject, name, VlcVarType.String);
        if (result == 0 && initialValue != null)
        {
            VlcBridge.VarSetString(_vlcObject, name, initialValue);
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
        VlcBridge.VarDestroy(_vlcObject, name);
    }

    /// <summary>
    /// Set an integer variable value.
    /// </summary>
    /// <param name="name">Variable name</param>
    /// <param name="value">Value to set</param>
    /// <returns>True if set successfully</returns>
    public bool SetInteger(string name, long value)
    {
        return VlcBridge.VarSetInteger(_vlcObject, name, value) == 0;
    }

    /// <summary>
    /// Get an integer variable value.
    /// </summary>
    /// <param name="name">Variable name</param>
    /// <returns>The variable value, or 0 if not found</returns>
    public long GetInteger(string name)
    {
        return VlcBridge.VarGetInteger(_vlcObject, name);
    }

    /// <summary>
    /// Set a string variable value.
    /// </summary>
    /// <param name="name">Variable name</param>
    /// <param name="value">Value to set</param>
    /// <returns>True if set successfully</returns>
    public bool SetString(string name, string? value)
    {
        return VlcBridge.VarSetString(_vlcObject, name, value) == 0;
    }

    /// <summary>
    /// Get a string variable value.
    /// </summary>
    /// <param name="name">Variable name</param>
    /// <returns>The variable value, or null if not found</returns>
    public string? GetString(string name)
    {
        nint ptr = VlcBridge.VarGetStringPtr(_vlcObject, name);
        if (ptr == nint.Zero)
            return null;

        // Marshal the string and free the native memory
        string? result = Marshal.PtrToStringUTF8(ptr);
        VlcBridge.VarFreeString(ptr);
        return result;
    }
}
