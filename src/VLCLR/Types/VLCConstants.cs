// VLC constant definitions
// Source: vlc/include/vlc_variables.h, vlc/include/vlc_playlist.h
// VLC Version: 4.0.6

namespace VLCLR.Types;

/// <summary>
/// VLC variable type constants.
/// Source: vlc_variables.h (lines 38-69)
/// </summary>
public static class VLCVarType
{
    // Type masks (lines 38-40)

    /// <summary>Mask to extract variable type</summary>
    public const int TypeMask = 0x00ff;

    /// <summary>Mask to extract variable class</summary>
    public const int ClassMask = 0x00f0;

    /// <summary>Mask to extract variable flags</summary>
    public const int FlagsMask = 0xff00;

    // Variable types (lines 47-53)

    /// <summary>Void variable (trigger only)</summary>
    public const int Void = 0x0010;

    /// <summary>Boolean variable</summary>
    public const int Bool = 0x0020;

    /// <summary>Integer variable (64-bit)</summary>
    public const int Integer = 0x0030;

    /// <summary>String variable</summary>
    public const int String = 0x0040;

    /// <summary>Float variable</summary>
    public const int Float = 0x0050;

    /// <summary>Address/pointer variable</summary>
    public const int Address = 0x0070;

    /// <summary>Coordinates variable (x, y)</summary>
    public const int Coords = 0x00A0;

    // Additive flags (lines 61-68)

    /// <summary>Variable has choices</summary>
    public const int HasChoice = 0x0100;

    /// <summary>Variable is a command</summary>
    public const int IsCommand = 0x2000;

    /// <summary>Inherit value from parent object or config</summary>
    public const int DoInherit = 0x8000;
}

/// <summary>
/// VLC variable action constants for var_Change().
/// Source: vlc_variables.h (lines 79-101)
/// </summary>
public static class VLCVarAction
{
    /// <summary>Set the step value</summary>
    public const int SetStep = 0x0012;

    /// <summary>Set value without triggering callbacks</summary>
    public const int SetValue = 0x0013;

    /// <summary>Set the text description</summary>
    public const int SetText = 0x0014;

    /// <summary>Get the text description</summary>
    public const int GetText = 0x0015;

    /// <summary>Get the minimum value</summary>
    public const int GetMin = 0x0016;

    /// <summary>Get the maximum value</summary>
    public const int GetMax = 0x0017;

    /// <summary>Get the step value</summary>
    public const int GetStep = 0x0018;

    /// <summary>Add a choice</summary>
    public const int AddChoice = 0x0020;

    /// <summary>Delete a choice</summary>
    public const int DelChoice = 0x0021;

    /// <summary>Clear all choices</summary>
    public const int ClearChoices = 0x0022;

    /// <summary>Get all choices</summary>
    public const int GetChoices = 0x0024;

    /// <summary>Get the number of choices</summary>
    public const int ChoicesCount = 0x0026;

    /// <summary>Set min and max values</summary>
    public const int SetMinMax = 0x0027;
}

/// <summary>
/// VLC playlist playback order constants.
/// Source: vlc_playlist.h
/// </summary>
public static class VLCPlaylistOrder
{
    /// <summary>Play items in order</summary>
    public const int Normal = 0;

    /// <summary>Shuffle playback order</summary>
    public const int Random = 1;
}

/// <summary>
/// VLC playlist repeat mode constants.
/// Source: vlc_playlist.h
/// </summary>
public static class VLCPlaylistRepeat
{
    /// <summary>No repeat</summary>
    public const int None = 0;

    /// <summary>Repeat current item</summary>
    public const int One = 1;

    /// <summary>Repeat entire playlist</summary>
    public const int All = 2;
}
