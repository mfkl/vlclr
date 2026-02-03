// Font manager for subtitle renderer
// Loads and caches fonts for text rendering using SixLabors.Fonts

using System.Reflection;
using SixLabors.Fonts;

namespace SubtitleRenderer;

/// <summary>
/// Manages font loading and caching for subtitle rendering.
/// Loads embedded default font and caches fonts by name/size/style.
/// </summary>
public static class FontManager
{
    private static readonly object _lock = new();
    private static FontCollection? _fontCollection;
    private static FontFamily? _defaultFontFamily;
    private static readonly Dictionary<FontKey, Font> _fontCache = new();

    // Maximum number of cached fonts to prevent memory growth
    private const int MaxCacheSize = 32;

    // Default font name that matches the embedded resource
    private const string DefaultFontName = "JetBrains Mono";

    /// <summary>
    /// Key for font cache lookup.
    /// </summary>
    private readonly record struct FontKey(string Name, float Size, FontStyle Style);

    /// <summary>
    /// Initializes the font manager, loading the embedded default font.
    /// Thread-safe - can be called multiple times.
    /// </summary>
    public static void Initialize()
    {
        lock (_lock)
        {
            if (_fontCollection != null)
            {
                return;
            }

            _fontCollection = new FontCollection();
            _defaultFontFamily = LoadEmbeddedFont(_fontCollection);

            Console.Error.WriteLine($"[.NET Subtitle] FontManager initialized, default font: {_defaultFontFamily?.Name ?? "none"}");
        }
    }

    /// <summary>
    /// Gets a font with the specified parameters.
    /// Falls back to embedded font if requested font is not available.
    /// </summary>
    /// <param name="name">Font family name (or null for default).</param>
    /// <param name="size">Font size in pixels.</param>
    /// <param name="bold">Whether the font should be bold.</param>
    /// <param name="italic">Whether the font should be italic.</param>
    /// <returns>Font instance ready for rendering.</returns>
    public static Font GetFont(string? name, float size, bool bold, bool italic)
    {
        // Ensure initialization
        Initialize();

        // Clamp size to reasonable bounds
        size = Math.Clamp(size, 8f, 200f);

        // Build font style
        FontStyle style = FontStyle.Regular;
        if (bold && italic)
        {
            style = FontStyle.BoldItalic;
        }
        else if (bold)
        {
            style = FontStyle.Bold;
        }
        else if (italic)
        {
            style = FontStyle.Italic;
        }

        // Use default font name if not specified
        string fontName = string.IsNullOrEmpty(name) ? DefaultFontName : name;

        var key = new FontKey(fontName, size, style);

        lock (_lock)
        {
            // Check cache
            if (_fontCache.TryGetValue(key, out Font? cachedFont))
            {
                return cachedFont;
            }

            // Try to find requested font family
            FontFamily family;
            if (!TryGetFontFamily(fontName, out family))
            {
                // Fall back to default font
                if (_defaultFontFamily == null)
                {
                    throw new InvalidOperationException("No fonts available - FontManager not initialized");
                }
                family = _defaultFontFamily.Value;
            }

            // Create the font
            Font font = family.CreateFont(size, style);

            // Evict oldest entries if cache is full
            if (_fontCache.Count >= MaxCacheSize)
            {
                EvictOldestCacheEntries();
            }

            _fontCache[key] = font;
            return font;
        }
    }

    /// <summary>
    /// Gets the default font at the specified size.
    /// </summary>
    /// <param name="size">Font size in pixels.</param>
    /// <returns>Default font instance.</returns>
    public static Font GetDefaultFont(float size) => GetFont(null, size, bold: false, italic: false);

    /// <summary>
    /// Clears the font cache. Useful for testing or memory cleanup.
    /// </summary>
    public static void ClearCache()
    {
        lock (_lock)
        {
            _fontCache.Clear();
        }
    }

    /// <summary>
    /// Gets the number of cached fonts.
    /// </summary>
    public static int CacheCount
    {
        get
        {
            lock (_lock)
            {
                return _fontCache.Count;
            }
        }
    }

    private static FontFamily LoadEmbeddedFont(FontCollection collection)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "SubtitleRenderer.Resources.JetBrainsMono-Regular.ttf";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            // List available resources for debugging
            var resources = assembly.GetManifestResourceNames();
            Console.Error.WriteLine($"[.NET Subtitle] Available resources: {string.Join(", ", resources)}");
            throw new InvalidOperationException($"Font resource not found: {resourceName}");
        }

        return collection.Add(stream);
    }

    private static bool TryGetFontFamily(string name, out FontFamily family)
    {
        // First check our collection
        if (_fontCollection != null && _fontCollection.TryGet(name, out family))
        {
            return true;
        }

        // Try system fonts
        if (SystemFonts.TryGet(name, out family))
        {
            return true;
        }

        family = default;
        return false;
    }

    private static void EvictOldestCacheEntries()
    {
        // Simple eviction: remove first half of entries
        // In practice, fonts are typically reused, so this is rarely hit
        int toRemove = _fontCache.Count / 2;
        var keysToRemove = _fontCache.Keys.Take(toRemove).ToList();
        foreach (var key in keysToRemove)
        {
            _fontCache.Remove(key);
        }
    }
}
