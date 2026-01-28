// Typed delegates for variadic vlc_set callback
// The vlc_set function is variadic in C: int vlc_set(void* opaque, void* target, int prop, ...)
// Since C# cannot call variadic functions directly, we use typed delegates for each argument pattern.

using System.Runtime.InteropServices;

namespace VlcPlugin.Module;

/// <summary>
/// Delegate for VLC_MODULE_CREATE: vlc_set(opaque, NULL, VLC_MODULE_CREATE, &amp;module)
/// Creates a new module and returns pointer via out parameter.
/// </summary>
/// <param name="opaque">Opaque context from vlc_entry</param>
/// <param name="target">Must be IntPtr.Zero (NULL) for module creation</param>
/// <param name="property">Must be VLC_MODULE_CREATE (0)</param>
/// <param name="outModule">Receives the created module pointer</param>
/// <returns>0 on success, non-zero on failure</returns>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate int VlcSetCreateDelegate(nint opaque, nint target, int property, nint* outModule);

/// <summary>
/// Delegate for VLC_CONFIG_CREATE: vlc_set(opaque, NULL, VLC_CONFIG_CREATE, type, &amp;config)
/// Creates a config item.
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate int VlcSetConfigCreateDelegate(nint opaque, nint target, int property, int configType, nint* outConfig);

/// <summary>
/// Delegate for string properties: vlc_set(opaque, module, prop, "string")
/// Used for VLC_MODULE_NAME, VLC_MODULE_CAPABILITY, VLC_MODULE_DESCRIPTION, etc.
/// </summary>
/// <param name="opaque">Opaque context from vlc_entry</param>
/// <param name="module">Module pointer from VLC_MODULE_CREATE</param>
/// <param name="property">Property to set (VLC_MODULE_NAME, etc.)</param>
/// <param name="value">String value (UTF-8)</param>
/// <returns>0 on success, non-zero on failure</returns>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate int VlcSetStringDelegate(
    nint opaque,
    nint module,
    int property,
    [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

/// <summary>
/// Delegate for integer properties: vlc_set(opaque, module, prop, int_value)
/// Used for VLC_MODULE_SCORE, VLC_CONFIG_VALUE (int64), etc.
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate int VlcSetIntDelegate(nint opaque, nint module, int property, int value);

/// <summary>
/// Delegate for int64 properties: vlc_set(opaque, module, prop, int64_value)
/// Used for VLC_CONFIG_VALUE with integer configs.
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate int VlcSetInt64Delegate(nint opaque, nint module, int property, long value);

/// <summary>
/// Delegate for callback registration: vlc_set(opaque, module, prop, "name", func_ptr)
/// Used for VLC_MODULE_CB_OPEN and VLC_MODULE_CB_CLOSE.
/// </summary>
/// <param name="opaque">Opaque context from vlc_entry</param>
/// <param name="module">Module pointer</param>
/// <param name="property">VLC_MODULE_CB_OPEN or VLC_MODULE_CB_CLOSE</param>
/// <param name="name">Name of the callback function (for debugging)</param>
/// <param name="callback">Function pointer to the callback</param>
/// <returns>0 on success, non-zero on failure</returns>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate int VlcSetCallbackDelegate(
    nint opaque,
    nint module,
    int property,
    [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
    nint callback);

/// <summary>
/// Delegate for shortcuts: vlc_set(opaque, module, VLC_MODULE_SHORTCUT, count, shortcuts_array)
/// Registers module shortcuts (alternative names).
/// </summary>
/// <param name="opaque">Opaque context from vlc_entry</param>
/// <param name="module">Module pointer</param>
/// <param name="property">VLC_MODULE_SHORTCUT</param>
/// <param name="count">Number of shortcuts</param>
/// <param name="shortcuts">Pointer to array of const char* strings</param>
/// <returns>0 on success, non-zero on failure</returns>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate int VlcSetShortcutsDelegate(nint opaque, nint module, int property, nuint count, nint* shortcuts);

/// <summary>
/// Delegate for config description: vlc_set(opaque, config, VLC_CONFIG_DESC, text, longtext, unused)
/// Note: Third string parameter is legacy, pass null.
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate int VlcSetConfigDescDelegate(
    nint opaque,
    nint config,
    int property,
    [MarshalAs(UnmanagedType.LPUTF8Str)] string? text,
    [MarshalAs(UnmanagedType.LPUTF8Str)] string? longtext,
    [MarshalAs(UnmanagedType.LPUTF8Str)] string? unused);

/// <summary>
/// Delegate for no-argument properties: vlc_set(opaque, module, prop)
/// Used for VLC_MODULE_NO_UNLOAD, VLC_CONFIG_PRIVATE, VLC_CONFIG_VOLATILE, etc.
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate int VlcSetNoArgDelegate(nint opaque, nint module, int property);

/// <summary>
/// Delegate for config capability: vlc_set(opaque, config, VLC_CONFIG_CAPABILITY, cap_string)
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate int VlcSetConfigCapabilityDelegate(
    nint opaque,
    nint config,
    int property,
    [MarshalAs(UnmanagedType.LPUTF8Str)] string capability);

/// <summary>
/// Delegate for integer range: vlc_set(opaque, config, VLC_CONFIG_RANGE, min, max)
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate int VlcSetIntRangeDelegate(nint opaque, nint config, int property, long min, long max);

/// <summary>
/// Delegate for float range: vlc_set(opaque, config, VLC_CONFIG_RANGE, min, max)
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate int VlcSetFloatRangeDelegate(nint opaque, nint config, int property, double min, double max);

/// <summary>
/// Helper class for managing vlc_set delegate casts.
/// Since all delegates point to the same variadic C function, we create typed wrappers
/// by casting the function pointer to the appropriate delegate type.
/// </summary>
public static class VlcSetHelpers
{
    /// <summary>
    /// Creates a delegate of the specified type from the vlc_set function pointer.
    /// </summary>
    public static T GetDelegate<T>(nint vlcSetPtr) where T : Delegate
    {
        return Marshal.GetDelegateForFunctionPointer<T>(vlcSetPtr);
    }
}
