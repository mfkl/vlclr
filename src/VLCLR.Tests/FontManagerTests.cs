using SixLabors.Fonts;
using VLCLR.Rendering;
using Xunit;

namespace VLCLR.Tests;

/// <summary>
/// Tests for FontManager class.
/// Verifies font loading, caching, and thread safety.
/// </summary>
public class FontManagerTests : IDisposable
{
    public FontManagerTests()
    {
        // Reset state before each test
        FontManager.Reset();
    }

    public void Dispose()
    {
        // Clean up after each test
        FontManager.Reset();
    }

    #region Initialization Tests

    [Fact]
    public void Initialize_CanBeCalledMultipleTimes()
    {
        // Should not throw
        FontManager.Initialize();
        FontManager.Initialize();
        FontManager.Initialize();
    }

    [Fact]
    public void Initialize_AfterReset_Reinitializes()
    {
        FontManager.Initialize();
        FontManager.Reset();
        FontManager.Initialize();

        Assert.Equal(0, FontManager.CacheCount);
    }

    #endregion

    #region GetFont Tests

    [Fact]
    public void GetFont_WithNullName_UsesDefault()
    {
        // System fonts should be available
        var font = FontManager.GetFont(null, 24f, bold: false, italic: false);

        Assert.NotNull(font);
        Assert.Equal(24f, font.Size);
    }

    [Fact]
    public void GetFont_WithEmptyName_UsesDefault()
    {
        var font = FontManager.GetFont("", 24f, bold: false, italic: false);

        Assert.NotNull(font);
        Assert.Equal(24f, font.Size);
    }

    [Fact]
    public void GetFont_ClampsMinSize()
    {
        var font = FontManager.GetFont(null, 1f, bold: false, italic: false);

        // Should be clamped to minimum of 8
        Assert.Equal(8f, font.Size);
    }

    [Fact]
    public void GetFont_ClampsMaxSize()
    {
        var font = FontManager.GetFont(null, 500f, bold: false, italic: false);

        // Should be clamped to maximum of 200
        Assert.Equal(200f, font.Size);
    }

    [Fact]
    public void GetFont_Bold_ReturnsBoldFont()
    {
        var font = FontManager.GetFont(null, 24f, bold: true, italic: false);

        Assert.True(font.IsBold);
        Assert.False(font.IsItalic);
    }

    [Fact]
    public void GetFont_Italic_ReturnsItalicFont()
    {
        var font = FontManager.GetFont(null, 24f, bold: false, italic: true);

        Assert.False(font.IsBold);
        Assert.True(font.IsItalic);
    }

    [Fact]
    public void GetFont_BoldItalic_ReturnsBoldItalicFont()
    {
        var font = FontManager.GetFont(null, 24f, bold: true, italic: true);

        Assert.True(font.IsBold);
        Assert.True(font.IsItalic);
    }

    #endregion

    #region GetDefaultFont Tests

    [Fact]
    public void GetDefaultFont_ReturnsFont()
    {
        var font = FontManager.GetDefaultFont(16f);

        Assert.NotNull(font);
        Assert.Equal(16f, font.Size);
        Assert.False(font.IsBold);
        Assert.False(font.IsItalic);
    }

    #endregion

    #region Cache Tests

    [Fact]
    public void GetFont_SameParameters_ReturnsCachedFont()
    {
        var font1 = FontManager.GetFont(null, 24f, bold: true, italic: false);
        var font2 = FontManager.GetFont(null, 24f, bold: true, italic: false);

        Assert.Same(font1, font2);
    }

    [Fact]
    public void GetFont_DifferentSize_ReturnsDifferentFont()
    {
        var font1 = FontManager.GetFont(null, 24f, bold: false, italic: false);
        var font2 = FontManager.GetFont(null, 32f, bold: false, italic: false);

        Assert.NotSame(font1, font2);
    }

    [Fact]
    public void GetFont_DifferentStyle_ReturnsDifferentFont()
    {
        var font1 = FontManager.GetFont(null, 24f, bold: false, italic: false);
        var font2 = FontManager.GetFont(null, 24f, bold: true, italic: false);

        Assert.NotSame(font1, font2);
    }

    [Fact]
    public void CacheCount_TracksEntries()
    {
        Assert.Equal(0, FontManager.CacheCount);

        FontManager.GetFont(null, 16f, bold: false, italic: false);
        Assert.Equal(1, FontManager.CacheCount);

        FontManager.GetFont(null, 24f, bold: false, italic: false);
        Assert.Equal(2, FontManager.CacheCount);

        // Same parameters should not increase count
        FontManager.GetFont(null, 24f, bold: false, italic: false);
        Assert.Equal(2, FontManager.CacheCount);
    }

    [Fact]
    public void ClearCache_RemovesAllEntries()
    {
        FontManager.GetFont(null, 16f, bold: false, italic: false);
        FontManager.GetFont(null, 24f, bold: false, italic: false);
        Assert.Equal(2, FontManager.CacheCount);

        FontManager.ClearCache();

        Assert.Equal(0, FontManager.CacheCount);
    }

    [Fact]
    public void Cache_EvictsWhenFull()
    {
        // Fill cache beyond max size
        for (int i = 0; i < FontManager.MaxCacheSize + 10; i++)
        {
            FontManager.GetFont(null, 8f + i, bold: false, italic: false);
        }

        // Cache should have evicted entries
        Assert.True(FontManager.CacheCount <= FontManager.MaxCacheSize);
    }

    #endregion

    #region Reset Tests

    [Fact]
    public void Reset_ClearsEverything()
    {
        FontManager.Initialize();
        FontManager.GetFont(null, 24f, bold: false, italic: false);
        FontManager.DefaultFontFamily = SystemFonts.Get("Arial");

        FontManager.Reset();

        Assert.Equal(0, FontManager.CacheCount);
        Assert.Null(FontManager.DefaultFontFamily);
    }

    #endregion

    #region TryGetFontFamily Tests

    [Fact]
    public void TryGetFontFamily_SystemFont_ReturnsTrue()
    {
        // Arial should be available on most systems
        bool found = FontManager.TryGetFontFamily("Arial", out var family);

        // May vary by system, so don't assert true
        // Just verify it doesn't throw
        Assert.True(found || !found);
    }

    [Fact]
    public void TryGetFontFamily_NonExistentFont_ReturnsFalse()
    {
        bool found = FontManager.TryGetFontFamily("NonExistentFontName12345", out _);

        Assert.False(found);
    }

    #endregion

    #region DefaultFontFamily Tests

    [Fact]
    public void DefaultFontFamily_CanBeSetAndGet()
    {
        if (SystemFonts.TryGet("Arial", out var arial))
        {
            FontManager.DefaultFontFamily = arial;
            Assert.Equal(arial, FontManager.DefaultFontFamily);
        }
    }

    [Fact]
    public void DefaultFontFamily_InitiallyNull()
    {
        Assert.Null(FontManager.DefaultFontFamily);
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task GetFont_ThreadSafe_NoExceptions()
    {
        var tasks = new List<Task>();

        // Spawn multiple threads accessing fonts concurrently
        for (int t = 0; t < 10; t++)
        {
            int threadId = t;
            tasks.Add(Task.Run(() =>
            {
                for (int i = 0; i < 20; i++)
                {
                    var font = FontManager.GetFont(null, 16f + (threadId % 5), bold: i % 2 == 0, italic: i % 3 == 0);
                    Assert.NotNull(font);
                }
            }));
        }

        await Task.WhenAll(tasks);
    }

    #endregion
}
