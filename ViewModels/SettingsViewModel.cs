using System.ComponentModel;
using System.Runtime.CompilerServices;
using MovieMaker.Models;
using MovieMaker.Services;

namespace MovieMaker.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private string _outputDirectory;
    private string _archiveDirectory;

    public SettingsViewModel(AppSettings settings)
    {
        _outputDirectory = settings.OutputDirectory;
        _archiveDirectory = settings.ArchiveDirectory;
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

    public AppSettings ToSettings()
    {
        return new AppSettings
        {
            OutputDirectory = OutputDirectory?.Trim() ?? string.Empty,
            ArchiveDirectory = ArchiveDirectory?.Trim() ?? string.Empty
        };
    }

    public string VersionText => VersionService.DisplayVersion;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
