namespace VLCLR.CImport.Naming;

/// <summary>A manifest name override matches no symbol in the snapshot.</summary>
public sealed class StaleNameOverrideException(string section, string key)
    : Exception($"names.{section}: '{key}' matches no symbol in the snapshot. Remove or fix the override in vlc.cimport.json.")
{
    public string Section { get; } = section;
    public string Key { get; } = key;
}
