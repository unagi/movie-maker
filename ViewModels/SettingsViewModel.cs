using System.ComponentModel;
using System.Runtime.CompilerServices;
using MovieMaker.Models;
using MovieMaker.Services;

namespace MovieMaker.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private string _outputDirectory;
    private string _archiveDirectory;
    private string _oneMinuteShortsOffsetSeconds;
    private string _threeMinuteShortsOffsetSeconds;

    public SettingsViewModel(AppSettings settings)
    {
        _outputDirectory = settings.OutputDirectory;
        _archiveDirectory = settings.ArchiveDirectory;
        _oneMinuteShortsOffsetSeconds = settings.OneMinuteShortsOffsetSeconds.ToString("0.###");
        _threeMinuteShortsOffsetSeconds = settings.ThreeMinuteShortsOffsetSeconds.ToString("0.###");
    }

    public string OutputDirectory
    {
        get => _outputDirectory;
        set
        {
            if (_outputDirectory == value) return;
            _outputDirectory = value;
            OnPropertyChanged();
        }
    }

    public string ArchiveDirectory
    {
        get => _archiveDirectory;
        set
        {
            if (_archiveDirectory == value) return;
            _archiveDirectory = value;
            OnPropertyChanged();
        }
    }

    public string OneMinuteShortsOffsetSeconds
    {
        get => _oneMinuteShortsOffsetSeconds;
        set
        {
            if (_oneMinuteShortsOffsetSeconds == value) return;
            _oneMinuteShortsOffsetSeconds = value;
            OnPropertyChanged();
        }
    }

    public string ThreeMinuteShortsOffsetSeconds
    {
        get => _threeMinuteShortsOffsetSeconds;
        set
        {
            if (_threeMinuteShortsOffsetSeconds == value) return;
            _threeMinuteShortsOffsetSeconds = value;
            OnPropertyChanged();
        }
    }

    public bool TryCreateSettings(out AppSettings settings, out string? errorMessage)
    {
        settings = new AppSettings();
        errorMessage = null;

        if (!double.TryParse(OneMinuteShortsOffsetSeconds?.Trim(), out var oneMinuteOffset) || oneMinuteOffset < 0 || oneMinuteOffset >= 60)
        {
            errorMessage = "1分Shortsオフセットは 0〜60 未満の数値で入力してください。";
            return false;
        }

        if (!double.TryParse(ThreeMinuteShortsOffsetSeconds?.Trim(), out var threeMinuteOffset) || threeMinuteOffset < 0 || threeMinuteOffset >= 180)
        {
            errorMessage = "3分Shortsオフセットは 0〜180 未満の数値で入力してください。";
            return false;
        }

        settings = new AppSettings
        {
            OutputDirectory = OutputDirectory?.Trim() ?? string.Empty,
            ArchiveDirectory = ArchiveDirectory?.Trim() ?? string.Empty,
            OneMinuteShortsOffsetSeconds = Math.Round(oneMinuteOffset, 3),
            ThreeMinuteShortsOffsetSeconds = Math.Round(threeMinuteOffset, 3)
        };
        return true;
    }

    public string VersionText => VersionService.DisplayVersion;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
