using System.Text.Json.Serialization;

namespace BouncingClockScreensaver;

/// <summary>The three things that can be shown, stacked vertically in any order.</summary>
public enum ClockElement
{
    Time,
    Date,
    Logo,
}

/// <summary>Horizontal alignment of one element within the shared block width.</summary>
public enum Justification
{
    Left,
    Center,
    Right,
}

/// <summary>
/// All user-configurable screensaver settings. Serialized to JSON in %APPDATA%.
/// </summary>
public sealed class AppSettings
{
    public bool ShowTime { get; set; } = true;
    /// <summary>.NET custom date/time format string, e.g. "HH:mm:ss".</summary>
    public string TimeFormat { get; set; } = "HH:mm:ss";
    public string TimeFontFamily { get; set; } = "Segoe UI";
    public float TimeFontSize { get; set; } = 80f;
    public int TimeColorArgb { get; set; } = System.Drawing.Color.FromArgb(255, 255, 165, 0).ToArgb(); // orange
    public bool TimeBold { get; set; } = false;
    public bool TimeItalic { get; set; } = false;
    public Justification TimeJustification { get; set; } = Justification.Center;

    public bool ShowDate { get; set; } = true;
    /// <summary>.NET custom date/time format string, e.g. "ddd, d MMMM yyyy".</summary>
    public string DateFormat { get; set; } = "ddd, d MMMM yyyy";
    public string DateFontFamily { get; set; } = "Segoe UI";
    public float DateFontSize { get; set; } = 18f;
    public int DateColorArgb { get; set; } = System.Drawing.Color.FromArgb(255, 255, 100, 40).ToArgb();
    public bool DateBold { get; set; } = false;
    public bool DateItalic { get; set; } = false;
    public Justification DateJustification { get; set; } = Justification.Center;

    public bool EnableLogo { get; set; } = false;
    public string LogoPath { get; set; } = string.Empty;
    /// <summary>Logo size adjustment: -10 (smallest) .. 0 (default) .. +10 (largest).</summary>
    public int LogoSizeAdjust { get; set; } = 0;
    public Justification LogoJustification { get; set; } = Justification.Center;

    /// <summary>
    /// Top-to-bottom stacking order of whichever of Time/Date/Logo are enabled.
    /// Always normalize with <see cref="NormalizeElementOrder"/> before relying on
    /// it containing exactly the three values once each — a hand-edited or
    /// old settings file might not.
    /// </summary>
    public List<ClockElement> ElementOrder { get; set; } = new() { ClockElement.Time, ClockElement.Logo, ClockElement.Date };

    /// <summary>0 (stationary) - 10 (fastest).</summary>
    public int MovementSpeed { get; set; } = 4;

    public int BackgroundColorArgb { get; set; } = System.Drawing.Color.Black.ToArgb();

    public bool MonospacedFontsOnly { get; set; } = false;

    [JsonIgnore]
    public System.Drawing.Color TimeColor
    {
        get => System.Drawing.Color.FromArgb(TimeColorArgb);
        set => TimeColorArgb = value.ToArgb();
    }

    [JsonIgnore]
    public System.Drawing.Color DateColor
    {
        get => System.Drawing.Color.FromArgb(DateColorArgb);
        set => DateColorArgb = value.ToArgb();
    }

    [JsonIgnore]
    public System.Drawing.Color BackgroundColor
    {
        get => System.Drawing.Color.FromArgb(BackgroundColorArgb);
        set => BackgroundColorArgb = value.ToArgb();
    }

    /// <summary>
    /// Returns <paramref name="order"/> with duplicates removed and any missing
    /// element appended, so it always contains exactly the three values once
    /// each — defensive against a hand-edited or pre-reorder-feature settings
    /// file, so an enabled element can never silently fail to render.
    /// </summary>
    public static List<ClockElement> NormalizeElementOrder(IEnumerable<ClockElement>? order)
    {
        var result = new List<ClockElement>(3);
        if (order != null)
        {
            foreach (var k in order)
            {
                if (!result.Contains(k)) result.Add(k);
            }
        }
        foreach (ClockElement k in Enum.GetValues<ClockElement>())
        {
            if (!result.Contains(k)) result.Add(k);
        }
        return result;
    }

    public AppSettings Clone()
    {
        var clone = (AppSettings)MemberwiseClone();
        clone.ElementOrder = new List<ClockElement>(ElementOrder); // MemberwiseClone is shallow; don't share the list
        return clone;
    }
}
