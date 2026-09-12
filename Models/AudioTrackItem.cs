using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MovieMaker.Models;

public sealed class AudioTrackItem : INotifyPropertyChanged
{
    private string _infoText = "解析中...";
    private string _loudnessText = "音量解析中...";
    private string _truePeakText = "True Peak解析中...";
    private string _loudnessRangeText = "LRA解析中...";
    private double? _durationSeconds;
    private int _position;
    private bool _isDropTargetBefore;
    private bool _isDropTargetAfter;
    private bool _isLoudnessAnalysisAvailable;
    private bool _isLoudnessAnalysisComplete;
    private bool _isLoudnessWarning;
    private bool _isTruePeakWarning;

    public AudioTrackItem(string path)
    {
        Path = path;
        FileName = System.IO.Path.GetFileName(path);
    }

    public string Path { get; }
    public string FileName { get; }

    public int Position
    {
        get => _position;
        private set
        {
            if (_position == value) return;
            _position = value;
            OnPropertyChanged();
        }
    }

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

    public string LoudnessText
    {
        get => _loudnessText;
        private set
        {
            if (_loudnessText == value) return;
            _loudnessText = value;
            OnPropertyChanged();
        }
    }

    public bool IsLoudnessAnalysisComplete
    {
        get => _isLoudnessAnalysisComplete;
        private set
        {
            if (_isLoudnessAnalysisComplete == value) return;
            _isLoudnessAnalysisComplete = value;
            OnPropertyChanged();
        }
    }

    public bool IsLoudnessAnalysisAvailable
    {
        get => _isLoudnessAnalysisAvailable;
        private set
        {
            if (_isLoudnessAnalysisAvailable == value) return;
            _isLoudnessAnalysisAvailable = value;
            OnPropertyChanged();
        }
    }

    public bool IsLoudnessWarning
    {
        get => _isLoudnessWarning;
        private set
        {
            if (_isLoudnessWarning == value) return;
            _isLoudnessWarning = value;
            OnPropertyChanged();
        }
    }

    public string TruePeakText
    {
        get => _truePeakText;
        private set
        {
            if (_truePeakText == value) return;
            _truePeakText = value;
            OnPropertyChanged();
        }
    }

    public string LoudnessRangeText
    {
        get => _loudnessRangeText;
        private set
        {
            if (_loudnessRangeText == value) return;
            _loudnessRangeText = value;
            OnPropertyChanged();
        }
    }

    public bool IsTruePeakWarning
    {
        get => _isTruePeakWarning;
        private set
        {
            if (_isTruePeakWarning == value) return;
            _isTruePeakWarning = value;
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

    public void ApplyLoudnessAnalysis(
        string loudnessText,
        bool isWarning,
        bool isAvailable,
        string truePeakText,
        bool isTruePeakWarning,
        string loudnessRangeText)
    {
        LoudnessText = loudnessText;
        IsLoudnessWarning = isWarning;
        IsLoudnessAnalysisAvailable = isAvailable;
        TruePeakText = truePeakText;
        IsTruePeakWarning = isTruePeakWarning;
        LoudnessRangeText = loudnessRangeText;
        IsLoudnessAnalysisComplete = true;
    }

    public void UpdatePosition(int position)
    {
        Position = position;
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
