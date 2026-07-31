namespace VLCLR.ObjectDetection;

public enum RedactionEffectMode
{
    Solid,
    Mosaic,
    Blur
}

public static class RedactionEffectModeParser
{
    public static bool TryParse(
        string? value,
        out RedactionEffectMode mode)
    {
        string normalized = value?.Trim() ?? "";
        if (normalized.Length == 0 ||
            normalized.Equals(
                "solid",
                StringComparison.OrdinalIgnoreCase))
        {
            mode = RedactionEffectMode.Solid;
            return true;
        }
        if (normalized.Equals(
                "mosaic",
                StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals(
                "pixelate",
                StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals(
                "pixelated",
                StringComparison.OrdinalIgnoreCase))
        {
            mode = RedactionEffectMode.Mosaic;
            return true;
        }
        if (normalized.Equals(
                "blur",
                StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals(
                "gaussian",
                StringComparison.OrdinalIgnoreCase))
        {
            mode = RedactionEffectMode.Blur;
            return true;
        }

        mode = default;
        return false;
    }
}
