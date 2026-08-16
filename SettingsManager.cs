using System.Text.Json;
using System.Text.Json.Serialization;

namespace BouncingClockScreensaver;

/// <summary>
/// Loads/saves <see cref="AppSettings"/> to a JSON file under %APPDATA%, shared by
/// the config dialog and the running screensaver so both always see the latest values.
/// </summary>
public static class SettingsManager
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BouncingClockScreensaver");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Applied globally (rather than per-property) so it also reaches enums
        // nested inside collections, like the ClockElement values in ElementOrder.
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings != null)
                {
                    return settings;
                }
            }
        }
        catch
        {
            // Corrupt or unreadable settings file -> fall back to defaults rather than crash
            // the screensaver (which runs unattended and must never hard-fail).
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }
}
