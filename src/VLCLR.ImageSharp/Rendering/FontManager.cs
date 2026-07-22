// Font manager for text rendering
// Loads and caches fonts for text rendering using SixLabors.Fonts

using System.Reflection;
using SixLabors.Fonts;

namespace VLCLR.Rendering;

/// <summary>
/// Manages font loading and caching for text rendering.
/// Supports loading fonts from embedded resources and caches fonts by name/size/style.
/// </summary>
/// <remarks>
/// This class is thread-safe. All public methods use locking internally.
/// Fonts can be loaded from embedded resources in any assembly or from system fonts.
/// </remarks>
public static class FontManager
{
    private static readonly object _lock = new();
    private static FontCollection? _fontCollection;
    private static FontFamily? _defaultFontFamily;
    private static readonly Dictionary<FontKey, Font> _fontCache = new();

    /// <summary>
    /// Maximum number of cached fonts to prevent memory growth.
    /// </summary>
    public const int MaxCacheSize = 32;

    /// <summary>
    /// Default font name when no font is specified.
    /// </summary>
    public const string DefaultFontName = "Arial";

    /// <summary>
    /// Key for font cache lookup.
    /// </summary>
    private readonly record struct FontKey(string Name, float Size, FontStyle Style);

    /// <summary>
    /// Gets or sets the default font family. Set this during initialization.
    /// </summary>
    public static FontFamily? DefaultFontFamily
    {
        get
        {
            lock (_lock)
            {
                return _defaultFontFamily;
            }
        }
        set
        {
            lock (_lock)
            {
                _defaultFontFamily = value;
            }
        }
    }

    /// <summary>
    /// Initializes the font manager with default configuration.
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
        }
    }

    /// <summary>
    /// Loads a font from an embedded resource and optionally sets it as the default.
    /// </summary>
    /// <param name="assembly">The assembly containing the embedded resource.</param>
    /// <param name="resourceName">The full name of the embedded resource (e.g., "MyNamespace.Resources.Font.ttf").</param>
    /// <param name="setAsDefault">If true, sets this font as the default font family.</param>
    /// <returns>The loaded FontFamily.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the resource is not found.</exception>
    public static FontFamily LoadEmbeddedFont(Assembly assembly, string resourceName, bool setAsDefault = false)
    {
        lock (_lock)
        {
            Initialize();

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                // List available resources for debugging
                var resources = assembly.GetManifestResourceNames();
                throw new InvalidOperationException(
                    $"Font resource not found: {resourceName}. Available: {string.Join(", ", resources)}");
            }

            var family = _fontCollection!.Add(stream);

            if (setAsDefault)
            {
                _defaultFontFamily = family;
            }

            return family;
        }
    }

    /// <summary>
    /// Gets a font with the specified parameters.
    /// Falls back to default font if requested font is not available.
    /// </summary>
    /// <param name="name">Font family name (or null for default).</param>
    /// <param name="size">Font size in pixels.</param>
    /// <param name="bold">Whether the font should be bold.</param>
    /// <param name="italic">Whether the font should be italic.</param>
    /// <returns>Font instance ready for rendering.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no fonts are available.</exception>
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
                    // Try system fonts as last resort
                    if (!SystemFonts.TryGet(DefaultFontName, out family))
                    {
                        throw new InvalidOperationException(
                            $"No fonts available - FontManager not initialized and '{DefaultFontName}' not found in system fonts");
                    }
                }
                else
                {
                    family = _defaultFontFamily.Value;
                }
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
    /// Resets the font manager to its initial state.
    /// Clears all cached fonts and loaded font families.
    /// </summary>
    public static void Reset()
    {
        lock (_lock)
        {
            _fontCache.Clear();
            _fontCollection = null;
            _defaultFontFamily = null;
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

    /// <summary>
    /// Tries to get a font family by name from the collection or system fonts.
    /// </summary>
    /// <param name="name">The font family name.</param>
    /// <param name="family">The found font family, if any.</param>
    /// <returns>True if the font family was found, false otherwise.</returns>
    public static bool TryGetFontFamily(string name, out FontFamily family)
    {
        lock (_lock)
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
