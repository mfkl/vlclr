// VLC Plugin Attributes
// Attributes used by the source generator to create VLC module entry points
// VLC Version: 4.0.6

namespace VLCLR.Plugin;

/// <summary>
/// Marks a class as a VLC module plugin.
/// The source generator will create the necessary entry points (vlc_entry, etc.)
/// for classes marked with this attribute.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// [VLCModule("my_filter")]
/// [VLCCapability("video filter")]
/// public partial class MyFilter : VLCVideoFilterBase
/// {
///     protected override void ProcessFrame(VLCFrame frame) { ... }
/// }
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class VLCModuleAttribute : Attribute
{
    /// <summary>
    /// Gets the module name used for VLC registration.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Creates a VLC module attribute with the specified name.
    /// </summary>
    /// <param name="name">The module name (e.g., "my_filter")</param>
    public VLCModuleAttribute(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }
}

/// <summary>
/// Specifies the capability of a VLC module.
/// Multiple capabilities can be specified for a single module.
/// </summary>
/// <remarks>
/// Common capabilities:
/// - "video filter" - Video processing filter
/// - "sub source" - Subtitle source
/// - "text renderer" - Text rendering for subtitles
/// - "audio filter" - Audio processing filter
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class VLCCapabilityAttribute : Attribute
{
    /// <summary>
    /// Gets the capability type (e.g., "video filter", "text renderer").
    /// </summary>
    public string Capability { get; }

    /// <summary>
    /// Gets or sets the capability score. Higher scores mean higher priority.
    /// Default is 0.
    /// </summary>
    public int Score { get; set; } = 0;

    /// <summary>
    /// Creates a VLC capability attribute.
    /// </summary>
    /// <param name="capability">The capability type</param>
    public VLCCapabilityAttribute(string capability)
    {
        Capability = capability ?? throw new ArgumentNullException(nameof(capability));
    }
}

/// <summary>
/// Provides a human-readable description for a VLC module.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class VLCDescriptionAttribute : Attribute
{
    /// <summary>
    /// Gets the module description.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Creates a VLC description attribute.
    /// </summary>
    /// <param name="description">The module description</param>
    public VLCDescriptionAttribute(string description)
    {
        Description = description ?? throw new ArgumentNullException(nameof(description));
    }
}

/// <summary>
/// Specifies a shortcut name for the VLC module.
/// Shortcuts allow quick activation via command line (e.g., --video-filter=shortcut).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class VLCShortcutAttribute : Attribute
{
    /// <summary>
    /// Gets the shortcut name.
    /// </summary>
    public string Shortcut { get; }

    /// <summary>
    /// Creates a VLC shortcut attribute.
    /// </summary>
    /// <param name="shortcut">The shortcut name</param>
    public VLCShortcutAttribute(string shortcut)
    {
        Shortcut = shortcut ?? throw new ArgumentNullException(nameof(shortcut));
    }
}

/// <summary>
/// Specifies a configuration option for a VLC module.
/// The source generator will create the appropriate VLC configuration entries.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// [VLCModule("my_filter")]
/// [VLCConfig("opacity", VLCConfigType.Float, Default = 1.0f, Min = 0.0f, Max = 1.0f)]
/// public partial class MyFilter : VLCVideoFilterBase { }
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class VLCConfigAttribute : Attribute
{
    /// <summary>
    /// Gets the configuration option name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the configuration type.
    /// </summary>
    public VLCConfigType Type { get; }

    /// <summary>
    /// Gets or sets the default value.
    /// </summary>
    public object? Default { get; set; }

    /// <summary>
    /// Gets or sets the minimum value (for numeric types).
    /// </summary>
    public object? Min { get; set; }

    /// <summary>
    /// Gets or sets the maximum value (for numeric types).
    /// </summary>
    public object? Max { get; set; }

    /// <summary>
    /// Gets or sets the human-readable description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the long description (help text).
    /// </summary>
    public string? LongDescription { get; set; }

    /// <summary>
    /// Creates a VLC configuration attribute.
    /// </summary>
    /// <param name="name">The configuration option name</param>
    /// <param name="type">The configuration type</param>
    public VLCConfigAttribute(string name, VLCConfigType type)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Type = type;
    }
}

/// <summary>
/// VLC configuration option types.
/// </summary>
public enum VLCConfigType
{
    /// <summary>Integer value</summary>
    Integer,

    /// <summary>Floating-point value</summary>
    Float,

    /// <summary>Boolean value</summary>
    Bool,

    /// <summary>String value</summary>
    String,

    /// <summary>Password string (hidden input)</summary>
    Password,

    /// <summary>File path</summary>
    File,

    /// <summary>Directory path</summary>
    Directory,

    /// <summary>Module name</summary>
    Module,

    /// <summary>Key binding</summary>
    Key
}
