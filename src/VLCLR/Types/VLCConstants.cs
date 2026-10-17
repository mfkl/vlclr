// VLC constant definitions
// Source: vlc/include/vlc_variables.h, vlc/include/vlc_playlist.h
// VLC Version: 4.0.6

using Interop = VLCLR.Native.Interop;

namespace VLCLR.Types;

/// <summary>
/// VLC variable type constants.
/// Source: vlc_variables.h (lines 38-69)
/// </summary>
public static class VLCVarType
{
    // Type masks (lines 38-40)

    /// <summary>Mask to extract variable type</summary>
    public const int TypeMask = Interop.VlcVariables.VarType;

    /// <summary>Mask to extract variable class</summary>
    public const int ClassMask = Interop.VlcVariables.VarClass;

    /// <summary>Mask to extract variable flags</summary>
    public const int FlagsMask = Interop.VlcVariables.VarFlags;

    // Variable types (lines 47-53)

    /// <summary>Void variable (trigger only)</summary>
    public const int Void = Interop.VlcVariables.VarVoid;

    /// <summary>Boolean variable</summary>
    public const int Bool = Interop.VlcVariables.VarBool;

    /// <summary>Integer variable (64-bit)</summary>
    public const int Integer = Interop.VlcVariables.VarInteger;

    /// <summary>String variable</summary>
    public const int String = Interop.VlcVariables.VarString;

    /// <summary>Float variable</summary>
    public const int Float = Interop.VlcVariables.VarFloat;

    /// <summary>Address/pointer variable</summary>
    public const int Address = Interop.VlcVariables.VarAddress;

    /// <summary>Coordinates variable (x, y)</summary>
    public const int Coords = Interop.VlcVariables.VarCoords;

    // Additive flags (lines 61-68)

    /// <summary>Variable has choices</summary>
    public const int HasChoice = Interop.VlcVariables.VarHaschoice;

    /// <summary>Variable is a command</summary>
    public const int IsCommand = Interop.VlcVariables.VarIscommand;

    /// <summary>Inherit value from parent object or config</summary>
    public const int DoInherit = Interop.VlcVariables.VarDoinherit;
}

/// <summary>
/// VLC variable action constants for var_Change().
/// Source: vlc_variables.h (lines 79-101)
/// </summary>
public static class VLCVarAction
{
    /// <summary>Set the step value</summary>
    public const int SetStep = Interop.VlcVariables.VarSetstep;

    /// <summary>Set value without triggering callbacks</summary>
    public const int SetValue = Interop.VlcVariables.VarSetvalue;

    /// <summary>Set the text description</summary>
    public const int SetText = Interop.VlcVariables.VarSettext;

    /// <summary>Get the text description</summary>
    public const int GetText = Interop.VlcVariables.VarGettext;

    /// <summary>Get the minimum value</summary>
    public const int GetMin = Interop.VlcVariables.VarGetmin;

    /// <summary>Get the maximum value</summary>
    public const int GetMax = Interop.VlcVariables.VarGetmax;

    /// <summary>Get the step value</summary>
    public const int GetStep = Interop.VlcVariables.VarGetstep;

    /// <summary>Add a choice</summary>
    public const int AddChoice = Interop.VlcVariables.VarAddchoice;

    /// <summary>Delete a choice</summary>
    public const int DelChoice = Interop.VlcVariables.VarDelchoice;

    /// <summary>Clear all choices</summary>
    public const int ClearChoices = Interop.VlcVariables.VarClearchoices;

    /// <summary>Get all choices</summary>
    public const int GetChoices = Interop.VlcVariables.VarGetchoices;

    /// <summary>Get the number of choices</summary>
    public const int ChoicesCount = Interop.VlcVariables.VarChoicescount;

    /// <summary>Set min and max values</summary>
    public const int SetMinMax = Interop.VlcVariables.VarSetminmax;
}

/// <summary>
/// VLC playlist playback order constants.
/// Source: vlc_playlist.h
/// </summary>
public static class VLCPlaylistOrder
{
    /// <summary>Play items in order</summary>
    public const int Normal = (int)Interop.PlaylistPlaybackOrder.Normal;

    /// <summary>Shuffle playback order</summary>
    public const int Random = (int)Interop.PlaylistPlaybackOrder.Random;
}

/// <summary>
/// VLC playlist repeat mode constants.
/// Source: vlc_playlist.h
/// </summary>
public static class VLCPlaylistRepeat
{
    /// <summary>No repeat</summary>
    public const int None = (int)Interop.PlaylistPlaybackRepeat.None;

    /// <summary>Repeat current item</summary>
    public const int One = (int)Interop.PlaylistPlaybackRepeat.Current;

    /// <summary>Repeat entire playlist</summary>
    public const int All = (int)Interop.PlaylistPlaybackRepeat.All;
}
