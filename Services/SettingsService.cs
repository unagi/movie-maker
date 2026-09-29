using System.IO;
using System.Text.Json;
using MovieMaker.Models;

namespace MovieMaker.Services;

public static class SettingsService
{
    public static AppSettings Current { get; private set; } = new();
    public static string? LoadError { get; private set; }

    public static void Load()
    {
        LoadError = null;
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings == null) throw new JsonException("設定内容が空です。");
                Current = settings;
                if (!ShortsPolicy.AreSettingsValid(settings))
                    LoadError = "Shorts上限またはオフセットが範囲外です。設定画面で修正してください。";
            }
        }
        catch (System.Exception ex)
        {
            Current = new AppSettings();
            LoadError = $"設定ファイルを読み取れません。設定画面で修正してください: {ex.Message}";
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
        LoadError = null;
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
