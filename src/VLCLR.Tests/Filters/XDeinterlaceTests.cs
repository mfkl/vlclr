using VLCLR.Filters.Video;
using Xunit;

namespace VLCLR.Tests.Filters;

// The port is checked against VLC's C, sample for sample, by
// benchmarks/YadifCompare/Harness; these check its behaviour.
public class XDeinterlaceTests
{
    [Fact]
    public void Render_KeepsAFlatPictureFlat()
    {
        using var input = new TestPicture(64, 48, 8, seed: 1);
        using var output = new TestPicture(64, 48, 8, seed: 2);
        input.Fill(_ => 100);

        XDeinterlace.Render(input.Planes, output.Planes);

        Assert.All(output.Visible(), b => Assert.Equal(100, b));
    }

    [Fact]
    public void Render_RebuildsTheSecondFieldOfACombedPictureFromTheFirst()
    {
        using var input = new TestPicture(64, 48, 8, seed: 1);
        using var output = new TestPicture(64, 48, 8, seed: 2);
        input.Fill(line => line % 2 == 0 ? (byte)60 : (byte)180);

        XDeinterlace.Render(input.Planes, output.Planes);

        // Every line takes the even field, but the last one of each plane,
        // which X blends with the line above.
        for (int plane = 0; plane < 3; plane++)
        {
            int lines = output.Planes[plane].VisibleLines;
            for (int line = 0; line < lines - 1; line++)
            {
                Assert.All(output.Line(plane, line).ToArray(), b => Assert.Equal(60, b));
            }
            Assert.All(output.Line(plane, lines - 1).ToArray(), b => Assert.Equal(120, b));
        }
    }

    [Fact]
    public void Render_RejectsAnOutputWithFewerPlanes()
    {
        using var picture = new TestPicture(16, 8, 8, seed: 1);
        Assert.Throws<ArgumentException>(() => XDeinterlace.Render(picture.Planes, picture.Planes.AsSpan(0, 2)));
    }
}
