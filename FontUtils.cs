namespace BouncingClockScreensaver;

internal static class FontUtils
{
    /// <summary>
    /// .NET has no direct "is this font monospaced" flag, so approximate it by
    /// comparing the rendered width of a narrow and a wide character at a fixed
    /// size — in a monospace font they come out equal.
    /// </summary>
    public static bool IsMonospace(FontFamily family)
    {
        try
        {
            if (!family.IsStyleAvailable(FontStyle.Regular))
            {
                return false;
            }

            using var font = new Font(family, 24f, FontStyle.Regular, GraphicsUnit.Pixel);
            using var bmp = new Bitmap(1, 1);
            using var g = Graphics.FromImage(bmp);

            var narrow = g.MeasureString("i", font, PointF.Empty, StringFormat.GenericTypographic);
            var wide = g.MeasureString("W", font, PointF.Empty, StringFormat.GenericTypographic);

            return Math.Abs(narrow.Width - wide.Width) < 0.5f;
        }
        catch
        {
            return false;
        }
    }
}
