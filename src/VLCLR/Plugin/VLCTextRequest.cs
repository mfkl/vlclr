// VLC Text Request wrapper
// Provides safe access to text rendering request data
// VLC Version: 4.0.6

using VLCLR.Native;

namespace VLCLR.Plugin;

/// <summary>
/// Encapsulates a text rendering request with all necessary information.
/// </summary>
public readonly struct VLCTextRequest
{
    /// <summary>
    /// Creates a text request from individual components.
    /// </summary>
    public VLCTextRequest(string text, VLCTextStyle style, int maxWidth, int maxHeight, int alignment)
    {
        Text = text;
        Style = style;
        MaxWidth = maxWidth;
        MaxHeight = maxHeight;
        Alignment = alignment;
    }

    /// <summary>
    /// The text to render.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// The text style (font, color, size, etc.).
    /// </summary>
    public VLCTextStyle Style { get; }

    /// <summary>
    /// Maximum width for the rendered text region (0 = no limit).
    /// </summary>
    public int MaxWidth { get; }

    /// <summary>
    /// Maximum height for the rendered text region (0 = no limit).
    /// </summary>
    public int MaxHeight { get; }

    /// <summary>
    /// Alignment flags (combination of VLCSubpictureAlign values).
    /// </summary>
    public int Alignment { get; }

    /// <summary>
    /// Gets the horizontal text alignment.
    /// </summary>
    public Rendering.TextAlignment HorizontalAlignment => VLCSubpictureAlign.ToTextAlignment(Alignment);

    /// <summary>
    /// Gets the vertical text position.
    /// </summary>
    public Rendering.TextVerticalPosition VerticalPosition => VLCSubpictureAlign.ToVerticalPosition(Alignment);

    /// <summary>
    /// Gets whether this request has any text to render.
    /// </summary>
    public bool HasText => !string.IsNullOrEmpty(Text);

    /// <summary>
    /// Gets the font size from the style, falling back to defaults.
    /// </summary>
    public int FontSize => Style.FontSize > 0 ? Style.FontSize : VLCTextStyleDefaults.FontSize;

    /// <summary>
    /// Gets the font color as an ARGB integer.
    /// </summary>
    public uint FontColorArgb
    {
        get
        {
            // Style.FontColor is 0x00RRGGBB, we need to add alpha
            byte alpha = Style.FontAlpha > 0 ? Style.FontAlpha : VLCTextStyleAlpha.Opaque;
            return ((uint)alpha << 24) | (Style.FontColor & 0x00FFFFFF);
        }
    }

    /// <summary>
    /// Gets whether bold style is requested.
    /// </summary>
    public bool IsBold => (Style.StyleFlags & VLCTextStyleFlags.Bold) != 0;

    /// <summary>
    /// Gets whether italic style is requested.
    /// </summary>
    public bool IsItalic => (Style.StyleFlags & VLCTextStyleFlags.Italic) != 0;

    /// <summary>
    /// Gets whether outline is requested.
    /// </summary>
    public bool HasOutline => (Style.StyleFlags & VLCTextStyleFlags.Outline) != 0;

    /// <summary>
    /// Gets whether shadow is requested.
    /// </summary>
    public bool HasShadow => (Style.StyleFlags & VLCTextStyleFlags.Shadow) != 0;
}
