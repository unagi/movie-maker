using System.IO;
using System.Text.Json;
using MovieMaker.Models;

namespace MovieMaker.Services;

public static class SettingsService
{
    private static readonly string AppFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MovieMaker");

    private static readonly string SettingsPath = Path.Combine(AppFolder, "settings.json");

    public static AppSettings Current { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
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
        Directory.CreateDirectory(AppFolder);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(SettingsPath, json);
        Current = settings;
    }

    public static string SettingsDirectory => AppFolder;
    public static string SettingsFilePath => SettingsPath;
}
