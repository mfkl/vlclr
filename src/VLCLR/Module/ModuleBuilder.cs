using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using VLCLR.Native;

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
    private string _openCallbackName;
    private string _closeCallbackName;

    private ModuleBuilder(nint vlcSetPtr, nint opaque)
    {
        _vlcSetPtr = vlcSetPtr;
        _opaque = opaque;
        _module = 0;
        _result = 0;
        _openCallback = 0;
        _closeCallback = 0;
        _openCallbackName = "Open";
        _closeCallbackName = "Close";

        // First call: VLC_MODULE_CREATE to get a module handle
        nint moduleOut = 0;
        _result = VLCVariadic.Call(vlcSetPtr, opaque, 0, VLCModuleConstants.VLC_MODULE_CREATE, (long)&moduleOut);
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
    /// Adds the name used to select this module explicitly from VLC options.
    /// Submodules must use a shortcut because VLC_MODULE_NAME is only valid for
    /// the first/root module in a plugin descriptor.
    /// </summary>
    public ModuleBuilder WithShortcut(string shortcut)
    {
        if (_result == 0)
        {
            nint shortcutPtr = PinString(shortcut);
            nint* shortcuts = stackalloc nint[1];
            shortcuts[0] = shortcutPtr;
            _result = Set(_module, VLCModuleConstants.VLC_MODULE_SHORTCUT, 1, (long)shortcuts);
        }
        return this;
    }

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
    /// Prevents VLC from unloading the plugin library while the process is running.
    /// Native AOT libraries do not support unloading, so generated VLCLR plugins
    /// must set this flag during registration.
    /// </summary>
    public ModuleBuilder WithNoUnload() => SetFlag(VLCModuleConstants.VLC_MODULE_NO_UNLOAD);

    /// <summary>
    /// Sets the module open callback. Called when VLC activates the module.
    /// </summary>
    /// <param name="cb">Function pointer to the open callback. Must be decorated with
    /// [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]</param>
    public ModuleBuilder WithOpenCallback(delegate* unmanaged[Cdecl]<nint, int> cb)
        => WithOpenCallback(cb, "Open");

    /// <summary>
    /// Sets the module open callback and its plugin-wide symbol name. Use a
    /// distinct name for each callback when one DLL contains submodules.
    /// </summary>
    public ModuleBuilder WithOpenCallback(delegate* unmanaged[Cdecl]<nint, int> cb, string name)
    {
        // Store callback pointer BEFORE calling vlc_set (required by VLC)
        _openCallback = (nint)cb;
        _openCallbackName = name;
        return this;
    }

    /// <summary>
    /// Sets the module close callback. Called when VLC deactivates the module.
    /// </summary>
    /// <param name="cb">Function pointer to the close callback. Must be decorated with
    /// [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]</param>
    public ModuleBuilder WithCloseCallback(delegate* unmanaged[Cdecl]<nint, void> cb)
        => WithCloseCallback(cb, "Close");

    /// <summary>
    /// Sets the module close callback and its plugin-wide symbol name.
    /// </summary>
    public ModuleBuilder WithCloseCallback(delegate* unmanaged[Cdecl]<nint, void> cb, string name)
    {
        _closeCallback = (nint)cb;
        _closeCallbackName = name;
        return this;
    }

    /// <summary>
    /// Adds an integer configuration option.
    /// </summary>
    /// <param name="name">The config option name (used with --option=value)</param>
    /// <param name="defaultValue">Default value</param>
    /// <param name="description">Human-readable description</param>
    /// <param name="longDescription">Optional detailed help text</param>
    public ModuleBuilder AddIntegerConfig(string name, long defaultValue, string description, string? longDescription = null)
    {
        AddIntegerConfigCore(name, defaultValue, description, longDescription);
        return this;
    }

    private nint AddIntegerConfigCore(string name, long defaultValue, string description, string? longDescription)
    {
        if (_result != 0) return 0;

        // Create config item
        nint configOut = 0;
        _result = Set(_module, VLCModuleConstants.VLC_CONFIG_CREATE, VLCConfigTypes.CONFIG_ITEM_INTEGER, (long)&configOut);
        if (_result != 0) return 0;

        // Set name
        SetConfigString(configOut, VLCModuleConstants.VLC_CONFIG_NAME, name);
        if (_result != 0) return 0;

        // Set default value
        SetConfigLong(configOut, VLCModuleConstants.VLC_CONFIG_VALUE, defaultValue);
        if (_result != 0) return 0;

        // Set description
        SetConfigDesc(configOut, description, longDescription);

        return _result == 0 ? configOut : 0;
    }

    /// <summary>
    /// Adds an integer configuration option with range constraints.
    /// </summary>
    public ModuleBuilder AddIntegerConfig(string name, long defaultValue, long min, long max, string description, string? longDescription = null)
    {
        nint config = AddIntegerConfigCore(name, defaultValue, description, longDescription);
        if (_result != 0) return this;

        // Set range - requires passing min and max as two int64 values
        // Note: VLC_CONFIG_RANGE expects (min, max) as two separate int64 arguments
        _result = Set(config, VLCModuleConstants.VLC_CONFIG_RANGE, min, max);

        return this;
    }

    /// <summary>
    /// Adds a float configuration option.
    /// </summary>
    /// <param name="name">The config option name</param>
    /// <param name="defaultValue">Default value</param>
    /// <param name="description">Human-readable description</param>
    /// <param name="longDescription">Optional detailed help text</param>
    public ModuleBuilder AddFloatConfig(string name, double defaultValue, string description, string? longDescription = null)
    {
        AddFloatConfigCore(name, defaultValue, description, longDescription);
        return this;
    }

    private nint AddFloatConfigCore(string name, double defaultValue, string description, string? longDescription)
    {
        if (_result != 0) return 0;

        // Create config item
        nint configOut = 0;
        _result = Set(_module, VLCModuleConstants.VLC_CONFIG_CREATE, VLCConfigTypes.CONFIG_ITEM_FLOAT, (long)&configOut);
        if (_result != 0) return 0;

        // Set name
        SetConfigString(configOut, VLCModuleConstants.VLC_CONFIG_NAME, name);
        if (_result != 0) return 0;

        // Set default value (as double)
        SetConfigDouble(configOut, VLCModuleConstants.VLC_CONFIG_VALUE, defaultValue);
        if (_result != 0) return 0;

        // Set description
        SetConfigDesc(configOut, description, longDescription);

        return _result == 0 ? configOut : 0;
    }

    /// <summary>
    /// Adds a float configuration option with range constraints.
    /// </summary>
    public ModuleBuilder AddFloatConfig(string name, double defaultValue, double min, double max, string description, string? longDescription = null)
    {
        nint config = AddFloatConfigCore(name, defaultValue, description, longDescription);
        if (_result != 0) return this;

        // Set range
        // System V reads variadic doubles from vector registers, so only Apple
        // arm64 can pass their bits through integer slots.
        if (VLCVariadic.PassesOnStack)
            _result = Set(config, VLCModuleConstants.VLC_CONFIG_RANGE, VLCVaList.Double(min), VLCVaList.Double(max));
        else
        {
            var vlcSetRange = (delegate* unmanaged[Cdecl]<nint, nint, int, double, double, int>)_vlcSetPtr;
            _result = vlcSetRange(_opaque, config, VLCModuleConstants.VLC_CONFIG_RANGE, min, max);
        }

        return this;
    }

    /// <summary>
    /// Adds a boolean configuration option.
    /// </summary>
    /// <param name="name">The config option name</param>
    /// <param name="defaultValue">Default value</param>
    /// <param name="description">Human-readable description</param>
    /// <param name="longDescription">Optional detailed help text</param>
    public ModuleBuilder AddBoolConfig(string name, bool defaultValue, string description, string? longDescription = null)
    {
        if (_result != 0) return this;

        // Create config item
        nint configOut = 0;
        _result = Set(_module, VLCModuleConstants.VLC_CONFIG_CREATE, VLCConfigTypes.CONFIG_ITEM_BOOL, (long)&configOut);
        if (_result != 0) return this;

        // Set name
        SetConfigString(configOut, VLCModuleConstants.VLC_CONFIG_NAME, name);
        if (_result != 0) return this;

        // Set default value (as int64: 1 or 0)
        SetConfigLong(configOut, VLCModuleConstants.VLC_CONFIG_VALUE, defaultValue ? 1 : 0);
        if (_result != 0) return this;

        // Set description
        SetConfigDesc(configOut, description, longDescription);

        return this;
    }

    /// <summary>
    /// Adds a string configuration option.
    /// </summary>
    /// <param name="name">The config option name</param>
    /// <param name="defaultValue">Default value</param>
    /// <param name="description">Human-readable description</param>
    /// <param name="longDescription">Optional detailed help text</param>
    public ModuleBuilder AddStringConfig(string name, string? defaultValue, string description, string? longDescription = null)
    {
        if (_result != 0) return this;

        // Create config item
        nint configOut = 0;
        _result = Set(_module, VLCModuleConstants.VLC_CONFIG_CREATE, VLCConfigTypes.CONFIG_ITEM_STRING, (long)&configOut);
        if (_result != 0) return this;

        // Set name
        SetConfigString(configOut, VLCModuleConstants.VLC_CONFIG_NAME, name);
        if (_result != 0) return this;

        // Set default value
        if (defaultValue != null)
        {
            SetConfigString(configOut, VLCModuleConstants.VLC_CONFIG_VALUE, defaultValue);
            if (_result != 0) return this;
        }

        // Set description
        SetConfigDesc(configOut, description, longDescription);

        return this;
    }

    /// <summary>
    /// Adds a file path configuration option.
    /// </summary>
    public ModuleBuilder AddFileConfig(string name, string? defaultValue, string description, string? longDescription = null)
    {
        if (_result != 0) return this;

        nint configOut = 0;
        _result = Set(_module, VLCModuleConstants.VLC_CONFIG_CREATE, VLCConfigTypes.CONFIG_ITEM_LOADFILE, (long)&configOut);
        if (_result != 0) return this;

        SetConfigString(configOut, VLCModuleConstants.VLC_CONFIG_NAME, name);
        if (_result != 0) return this;

        if (defaultValue != null)
        {
            SetConfigString(configOut, VLCModuleConstants.VLC_CONFIG_VALUE, defaultValue);
            if (_result != 0) return this;
        }

        SetConfigDesc(configOut, description, longDescription);
        return this;
    }

    /// <summary>
    /// Adds a directory path configuration option.
    /// </summary>
    public ModuleBuilder AddDirectoryConfig(string name, string? defaultValue, string description, string? longDescription = null)
    {
        if (_result != 0) return this;

        nint configOut = 0;
        _result = Set(_module, VLCModuleConstants.VLC_CONFIG_CREATE, VLCConfigTypes.CONFIG_ITEM_DIRECTORY, (long)&configOut);
        if (_result != 0) return this;

        SetConfigString(configOut, VLCModuleConstants.VLC_CONFIG_NAME, name);
        if (_result != 0) return this;

        if (defaultValue != null)
        {
            SetConfigString(configOut, VLCModuleConstants.VLC_CONFIG_VALUE, defaultValue);
            if (_result != 0) return this;
        }

        SetConfigDesc(configOut, description, longDescription);
        return this;
    }

    /// <summary>
    /// Sets the subcategory for subsequent config items.
    /// </summary>
    public ModuleBuilder WithSubcategory(int subcategory)
    {
        if (_result != 0) return this;

        nint configOut = 0;
        _result = Set(_module, VLCModuleConstants.VLC_CONFIG_CREATE, VLCConfigTypes.CONFIG_SUBCATEGORY, (long)&configOut);
        if (_result != 0) return this;

        // Set the subcategory value
        SetConfigLong(configOut, VLCModuleConstants.VLC_CONFIG_VALUE, subcategory);
        return this;
    }

    private void SetConfigString(nint config, int key, string value)
    {
        _result = Set(config, key, PinString(value));
    }

    private void SetConfigLong(nint config, int key, long value)
    {
        _result = Set(config, key, value);
    }

    private void SetConfigDouble(nint config, int key, double value)
    {
        if (VLCVariadic.PassesOnStack)
            _result = Set(config, key, VLCVaList.Double(value));
        else
        {
            var vlcSet = (delegate* unmanaged[Cdecl]<nint, nint, int, double, int>)_vlcSetPtr;
            _result = vlcSet(_opaque, config, key, value);
        }
    }

    private void SetConfigDesc(nint config, string description, string? longDescription)
    {
        nint descPtr = PinString(description);
        nint longDescPtr = longDescription != null ? PinString(longDescription) : 0;
        _result = Set(config, VLCModuleConstants.VLC_CONFIG_DESC, descPtr, longDescPtr);
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
            _result = Set(_module, VLCModuleConstants.VLC_MODULE_CB_OPEN, PinString(_openCallbackName), _openCallback);
            if (_result != 0)
                return _result;
        }

        // Register close callback if set
        if (_closeCallback != 0)
        {
            _result = Set(_module, VLCModuleConstants.VLC_MODULE_CB_CLOSE, PinString(_closeCallbackName), _closeCallback);
            if (_result != 0)
                return _result;
        }

        return _result;
    }

    private ModuleBuilder SetString(int key, string value)
    {
        if (_result == 0)
        {
            _result = Set(_module, key, PinString(value));
        }
        return this;
    }

    private ModuleBuilder SetInt(int key, int value)
    {
        if (_result == 0)
        {
            _result = Set(_module, key, value);
        }
        return this;
    }

    private ModuleBuilder SetFlag(int key)
    {
        if (_result == 0)
        {
            _result = Set(_module, key);
        }
        return this;
    }

    /// <summary>
    /// <c>vlc_set(opaque, target, property, ...)</c> with integer or pointer
    /// arguments, each passed in a 64-bit variadic slot.
    /// </summary>
    private readonly int Set(nint target, int property, long first = 0, long second = 0) =>
        VLCVariadic.Call(_vlcSetPtr, _opaque, target, property, first, second);

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
