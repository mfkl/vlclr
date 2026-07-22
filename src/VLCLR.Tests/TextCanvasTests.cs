using Xunit;
using VLCLR.Rendering;
using VLCLR.Text;

namespace VLCLR.Tests;

/// <summary>
/// Tests for the TextCanvas class.
/// </summary>
public class TextCanvasTests
{
    [Fact]
    public void Constructor_CreatesCanvasWithCorrectDimensions()
    {
        // Arrange & Act
        using var canvas = new TextCanvas(800, 600);

        // Assert
        Assert.Equal(800, canvas.Width);
        Assert.Equal(600, canvas.Height);
    }

    [Fact]
    public void Constructor_WithOptions_UsesProvidedOptions()
    {
        // Arrange
        var options = new TextCanvasOptions
        {
            BackgroundPadding = 10,
            CanvasMargin = 20,
            VerticalPosition = TextVerticalPosition.Top
        };

        // Act
        using var canvas = new TextCanvas(800, 600, options);

        // Assert - canvas should be created successfully
        Assert.Equal(800, canvas.Width);
        Assert.Equal(600, canvas.Height);
    }

    [Fact]
    public void EnsureSize_ResizesCanvasWhenDimensionsChange()
    {
        // Arrange
        using var canvas = new TextCanvas(800, 600);

        // Act
        canvas.EnsureSize(1920, 1080);

        // Assert
        Assert.Equal(1920, canvas.Width);
        Assert.Equal(1080, canvas.Height);
    }

    [Fact]
    public void EnsureSize_DoesNotReallocateWhenSameDimensions()
    {
        // Arrange
        using var canvas = new TextCanvas(800, 600);
        var originalImage = canvas.GetImage();

        // Act
        canvas.EnsureSize(800, 600);

        // Assert - should be same instance
        Assert.Same(originalImage, canvas.GetImage());
    }

    [Fact]
    public void GetImage_ReturnsValidImage()
    {
        // Arrange
        using var canvas = new TextCanvas(100, 100);

        // Act
        var image = canvas.GetImage();

        // Assert
        Assert.NotNull(image);
        Assert.Equal(100, image.Width);
        Assert.Equal(100, image.Height);
    }

    [Fact]
    public void GetPixels_ReturnsCorrectSizeBuffer()
    {
        // Arrange
        using var canvas = new TextCanvas(100, 100);

        // Act
        var pixels = canvas.GetPixels();

        // Assert - RGBA = 4 bytes per pixel
        Assert.Equal(100 * 100 * 4, pixels.Length);
    }

    [Fact]
    public void Constructor_DoesNotAllocateAnEagerPixelMirror()
    {
        // Warm ImageSharp initialization before measuring the canvas allocation.
        using (var warmup = new TextCanvas(1, 1))
        {
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        using var canvas = new TextCanvas(1920, 1080);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // One RGBA image is expected; the old implementation allocated a second
        // 7.91 MiB byte[] mirror eagerly.
        Assert.True(allocated < 1024 * 1024, $"Constructor allocated {allocated:N0} managed bytes");
    }

    [Fact]
    public void Clear_MakesCanvasTransparent()
    {
        // Arrange
        using var canvas = new TextCanvas(10, 10);

        // Act
        canvas.Clear();
        var pixels = canvas.GetPixels();

        // Assert - all pixels should be transparent (RGBA all zeros)
        Assert.All(pixels, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Render_WithEmptySegmentList_DoesNotThrow()
    {
        // Arrange
        using var canvas = new TextCanvas(800, 600);
        var segments = new List<ParsedTextSegment>();

        // Act & Assert - should not throw
        var exception = Record.Exception(() => canvas.Render(segments));
        Assert.Null(exception);
    }

    [Fact]
    public void RenderText_WithValidStyle_ProducesNonTransparentPixels()
    {
        // Arrange
        using var canvas = new TextCanvas(200, 100);
        var style = new TextStyleWrapper
        {
            FontSize = 20,
            ForegroundColor = 0xFFFFFF, // White
            ForegroundAlpha = 255
        };

        // Act
        canvas.RenderText("Test", style);
        var pixels = canvas.GetPixels();

        // Assert - at least some pixels should be non-transparent
        Assert.Contains(pixels, b => b != 0);
    }

    [Fact]
    public void RenderText_WithOutline_ProducesPixels()
    {
        // Arrange
        using var canvas = new TextCanvas(200, 100);
        var style = new TextStyleWrapper
        {
            FontSize = 20,
            ForegroundColor = 0xFFFFFF,
            ForegroundAlpha = 255,
            HasOutline = true,
            OutlineColor = 0x000000,
            OutlineAlpha = 255,
            OutlineWidth = 2
        };

        // Act
        canvas.RenderText("Test", style);
        var pixels = canvas.GetPixels();

        // Assert - should have rendered something
        Assert.Contains(pixels, b => b != 0);
    }

    [Fact]
    public void Dispose_ClearsResources()
    {
        // Arrange
        var canvas = new TextCanvas(100, 100);

        // Act
        canvas.Dispose();

        // Assert - GetImage should return null after dispose
        Assert.Null(canvas.GetImage());
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var canvas = new TextCanvas(100, 100);

        // Act & Assert - should not throw on multiple dispose calls
        canvas.Dispose();
        var exception = Record.Exception(() => canvas.Dispose());
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(TextAlignment.Left)]
    [InlineData(TextAlignment.Center)]
    [InlineData(TextAlignment.Right)]
    public void Render_WithDifferentAlignments_DoesNotThrow(TextAlignment alignment)
    {
        // Arrange
        using var canvas = new TextCanvas(800, 600);
        var style = new TextStyleWrapper { FontSize = 20 };
        var segments = new List<ParsedTextSegment>
        {
            new("Test text", style)
        };

        // Act & Assert - should not throw for any alignment
        var exception = Record.Exception(() => canvas.Render(segments, alignment));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(TextVerticalPosition.Top)]
    [InlineData(TextVerticalPosition.Center)]
    [InlineData(TextVerticalPosition.Bottom)]
    [InlineData(TextVerticalPosition.Custom)]
    public void RenderText_WithDifferentVerticalPositions_DoesNotThrow(TextVerticalPosition position)
    {
        // Arrange
        var options = new TextCanvasOptions
        {
            VerticalPosition = position,
            CustomVerticalPosition = 0.5f
        };
        using var canvas = new TextCanvas(800, 600, options);
        var style = new TextStyleWrapper { FontSize = 20 };

        // Act & Assert - should not throw for any vertical position
        var exception = Record.Exception(() => canvas.RenderText("Test", style));
        Assert.Null(exception);
    }
}
