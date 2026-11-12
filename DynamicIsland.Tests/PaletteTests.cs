using System.Windows.Media;

namespace DynamicIsland.Tests;

public class PaletteTests
{
    static byte[] Pixels(params (byte R, byte G, byte B)[] colors) =>
        colors.SelectMany(c => new byte[] { c.B, c.G, c.R, 255 }).ToArray(); // BGRA

    [Fact]
    public void Vivid_colour_wins_over_large_dark_and_grey_areas()
    {
        byte[] cover = Pixels((10, 10, 10), (10, 10, 10), (128, 128, 128), (128, 128, 128), (230, 40, 120));
        Color accent = Palette.Accent(cover);
        Assert.True(accent.R > 180 && accent.G < 90, $"expected pink-ish, got {accent}");
    }

    [Fact]
    public void Greyscale_cover_gives_grey()
    {
        Color accent = Palette.Accent(Pixels((100, 100, 100), (100, 100, 100)));
        Assert.Equal(Color.FromRgb(100, 100, 100), accent);
    }

    [Fact]
    public void Mix_moves_towards_the_other_colour()
    {
        Color mixed = Palette.Mix(Color.FromRgb(200, 100, 0), Colors.Black, 0.75);
        Assert.Equal(Color.FromRgb(50, 25, 0), mixed);
    }
}
