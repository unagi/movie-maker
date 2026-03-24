using System.IO;
using System.Text.Json;
using MovieMaker.Models;

namespace MovieMaker.Services;

public static class SettingsService
{
    public static AppSettings Current { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    Current = settings;
                }
            }
        }
        catch
        {
            Current = new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(SettingsFilePath, json);
        Current = settings;
    }

    public static string SettingsDirectory
    {
        get
        {
            var overrideDirectory = Environment.GetEnvironmentVariable("MOVIEMAKER_SETTINGS_DIR");
            if (!string.IsNullOrWhiteSpace(overrideDirectory))
            {
                return overrideDirectory;
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MovieMaker");
        }
    }

    public static string SettingsFilePath => Path.Combine(SettingsDirectory, "settings.json");
}
