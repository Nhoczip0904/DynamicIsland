using System.Windows.Media;

namespace DynamicIsland;

static class Palette
{
    /// <summary>
    /// Accent colour of a cover from BGRA pixels. Weighted by saturation × brightness so a vivid
    /// detail beats large black/grey areas (a plain average turns most covers muddy brown).
    /// </summary>
    public static Color Accent(byte[] bgra)
    {
        double r = 0, g = 0, b = 0, total = 0;
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            byte B = bgra[i], G = bgra[i + 1], R = bgra[i + 2];
            int max = Math.Max(R, Math.Max(G, B)), min = Math.Min(R, Math.Min(G, B));
            double weight = Math.Pow((max - min) / 255.0 * (max / 255.0), 2) + 0.0001; // epsilon keeps greyscale covers grey
            r += R * weight; g += G * weight; b += B * weight; total += weight;
        }
        return total == 0 ? Colors.Black : Color.FromRgb((byte)Math.Round(r / total), (byte)Math.Round(g / total), (byte)Math.Round(b / total));
    }

    public static Color Mix(Color color, Color with, double amount) => Color.FromRgb(
        (byte)Math.Round(color.R + (with.R - color.R) * amount),
        (byte)Math.Round(color.G + (with.G - color.G) * amount),
        (byte)Math.Round(color.B + (with.B - color.B) * amount));
}
