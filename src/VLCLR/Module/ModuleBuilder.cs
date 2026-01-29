using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace VLCLR.Module;

/// <summary>
/// Fluent API for VLC module registration in vlc_entry.
/// Handles string pinning and callback registration automatically.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// [UnmanagedCallersOnly(EntryPoint = "vlc_entry")]
/// public static int VlcEntry(nint vlcSetPtr, nint opaque)
/// {
///     return ModuleBuilder.Create(vlcSetPtr, opaque)
///         .WithName("my_filter")
///         .WithCapability("video filter")
///         .WithOpenCallback(&amp;FilterOpen)
///         .Register();
/// }
/// </code>
/// </remarks>
public unsafe ref struct ModuleBuilder
{
    // Static storage for pinned strings - keeps them alive for plugin lifetime
    // This is intentional: module strings must remain valid as long as VLC has the plugin loaded
    private static readonly List<GCHandle> s_pinnedHandles = new();
    private static readonly object s_lock = new();

    private readonly nint _vlcSetPtr;
    private readonly nint _opaque;
    private nint _module;
    private int _result;

    // Stored callback pointers - must be set before vlc_set call
    private nint _openCallback;
    private nint _closeCallback;

    private ModuleBuilder(nint vlcSetPtr, nint opaque)
    {
        _vlcSetPtr = vlcSetPtr;
        _opaque = opaque;
        _module = 0;
        _result = 0;
        _openCallback = 0;
        _closeCallback = 0;

        // First call: VLC_MODULE_CREATE to get a module handle
        nint moduleOut = 0;
        var vlcSetCreate = (delegate* unmanaged[Cdecl]<nint, nint, int, nint*, int>)vlcSetPtr;
        _result = vlcSetCreate(opaque, 0, VLCModuleConstants.VLC_MODULE_CREATE, &moduleOut);
        _module = moduleOut;
    }

    /// <summary>
    /// Creates a new ModuleBuilder from the vlc_set function pointer and opaque context.
    /// </summary>
    /// <param name="vlcSetPtr">Function pointer to vlc_set from vlc_entry</param>
    /// <param name="opaque">Opaque context from vlc_entry</param>
    /// <returns>A new ModuleBuilder instance</returns>
    public static ModuleBuilder Create(nint vlcSetPtr, nint opaque)
    {
        return new ModuleBuilder(vlcSetPtr, opaque);
    }

    /// <summary>
    /// Sets the module name (internal identifier used for --video-filter=name).
    /// </summary>
    public ModuleBuilder WithName(string name) => SetString(VLCModuleConstants.VLC_MODULE_NAME, name);

    /// <summary>
    /// Sets the module short name (display name in UI).
    /// </summary>
    public ModuleBuilder WithShortName(string name) => SetString(VLCModuleConstants.VLC_MODULE_SHORTNAME, name);

    /// <summary>
    /// Sets the module description (shown in module info).
    /// </summary>
    public ModuleBuilder WithDescription(string desc) => SetString(VLCModuleConstants.VLC_MODULE_DESCRIPTION, desc);

    /// <summary>
    /// Sets the module capability (e.g., "video filter", "interface", "audio filter").
    /// </summary>
    public ModuleBuilder WithCapability(string cap) => SetString(VLCModuleConstants.VLC_MODULE_CAPABILITY, cap);

    /// <summary>
    /// Sets the module score (priority for capability selection, higher = preferred).
    /// </summary>
    public ModuleBuilder WithScore(int score) => SetInt(VLCModuleConstants.VLC_MODULE_SCORE, score);

    /// <summary>
    /// Sets the module open callback. Called when VLC activates the module.
    /// </summary>
    /// <param name="cb">Function pointer to the open callback. Must be decorated with
    /// [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]</param>
    public ModuleBuilder WithOpenCallback(delegate* unmanaged[Cdecl]<nint, int> cb)
    {
        // Store callback pointer BEFORE calling vlc_set (required by VLC)
        _openCallback = (nint)cb;
        return this;
    }

    /// <summary>
    /// Sets the module close callback. Called when VLC deactivates the module.
    /// </summary>
    /// <param name="cb">Function pointer to the close callback. Must be decorated with
    /// [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]</param>
    public ModuleBuilder WithCloseCallback(delegate* unmanaged[Cdecl]<nint, void> cb)
    {
        _closeCallback = (nint)cb;
        return this;
    }

    /// <summary>
    /// Completes the module registration and returns the result.
    /// </summary>
    /// <returns>0 on success, non-zero on failure</returns>
    public int Register()
    {
        if (_result != 0)
            return _result;

        // Register open callback if set
        if (_openCallback != 0)
        {
            var vlcSetCallback = (delegate* unmanaged[Cdecl]<nint, nint, int, nint, nint, int>)_vlcSetPtr;
            // VLC_MODULE_CB_OPEN requires a name string - use "Open" as default
            nint namePtr = PinString("Open");
            _result = vlcSetCallback(_opaque, _module, VLCModuleConstants.VLC_MODULE_CB_OPEN, namePtr, _openCallback);
            if (_result != 0)
                return _result;
        }

        // Register close callback if set
        if (_closeCallback != 0)
        {
            var vlcSetCallback = (delegate* unmanaged[Cdecl]<nint, nint, int, nint, nint, int>)_vlcSetPtr;
            nint namePtr = PinString("Close");
            _result = vlcSetCallback(_opaque, _module, VLCModuleConstants.VLC_MODULE_CB_CLOSE, namePtr, _closeCallback);
            if (_result != 0)
                return _result;
        }

        return _result;
    }

    private ModuleBuilder SetString(int key, string value)
    {
        if (_result == 0)
        {
            nint ptr = PinString(value);
            var vlcSet = (delegate* unmanaged[Cdecl]<nint, nint, int, nint, int>)_vlcSetPtr;
            _result = vlcSet(_opaque, _module, key, ptr);
        }
        return this;
    }

    private ModuleBuilder SetInt(int key, int value)
    {
        if (_result == 0)
        {
            var vlcSetInt = (delegate* unmanaged[Cdecl]<nint, nint, int, int, int>)_vlcSetPtr;
            _result = vlcSetInt(_opaque, _module, key, value);
        }
        return this;
    }

    /// <summary>
    /// Pins a string for the lifetime of the plugin.
    /// Strings passed to VLC must remain valid as long as the plugin is loaded.
    /// </summary>
    private static nint PinString(string value)
    {
        // Convert to null-terminated UTF-8 bytes
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value + "\0");

        // Pin the byte array
        GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        nint ptr = handle.AddrOfPinnedObject();

        // Store handle to prevent GC (intentionally never freed - plugin lifetime)
        lock (s_lock)
        {
            s_pinnedHandles.Add(handle);
        }

        return ptr;
    }
}
