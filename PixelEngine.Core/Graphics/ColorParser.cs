using Microsoft.Xna.Framework;

namespace PixelEngine.Graphics;

public static class ColorParser
{
    public static Color Parse(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var text = value.Trim().TrimStart('#');
        if (text.Length == 6 && uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            return new Color((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        if (text.Length == 8 && uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var argb))
            return new Color((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));
        return fallback;
    }
}
