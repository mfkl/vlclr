namespace VLCLR.Rendering;

/// <summary>
/// Vertical positioning mode for rendered text.
/// </summary>
public enum TextVerticalPosition
{
    /// <summary>Text positioned near top of canvas.</summary>
    Top,

    /// <summary>Text positioned in center of canvas.</summary>
    Center,

    /// <summary>Text positioned near bottom of canvas (typical for subtitles).</summary>
    Bottom,

    /// <summary>Text positioned at custom vertical position.</summary>
    Custom
}
