using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MovieMaker.Models;

public sealed class AudioTrackItem : INotifyPropertyChanged
{
    private string _infoText = "解析中...";
    private double? _durationSeconds;
    private bool _isDropTargetBefore;
    private bool _isDropTargetAfter;

    public AudioTrackItem(string path)
    {
        Path = path;
        FileName = System.IO.Path.GetFileName(path);
    }

    public string Path { get; }
    public string FileName { get; }

    public string InfoText
    {
        get => _infoText;
        private set
        {
            if (_infoText == value) return;
            _infoText = value;
            OnPropertyChanged();
        }
    }

    public double? DurationSeconds
    {
        get => _durationSeconds;
        private set
        {
            if (_durationSeconds == value) return;
            _durationSeconds = value;
            OnPropertyChanged();
        }
    }

    public bool IsDropTargetBefore
    {
        get => _isDropTargetBefore;
        set
        {
            if (_isDropTargetBefore == value) return;
            _isDropTargetBefore = value;
            OnPropertyChanged();
        }
    }

    public bool IsDropTargetAfter
    {
        get => _isDropTargetAfter;
        set
        {
            if (_isDropTargetAfter == value) return;
            _isDropTargetAfter = value;
            OnPropertyChanged();
        }
    }

    public void ApplyAnalysis(string infoText, double? durationSeconds)
    {
        InfoText = infoText;
        DurationSeconds = durationSeconds;
    }

    public void ClearDropIndicator()
    {
        IsDropTargetBefore = false;
        IsDropTargetAfter = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
