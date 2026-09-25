using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MovieMaker.Models;

public sealed class LoudnessAnalysisItem : INotifyPropertyChanged
{
    private string _loudnessText = "解析待ち";
    private string _truePeakText = "解析待ち";
    private string _loudnessRangeText = "解析待ち";
    private string _statusText = "待機中";
    private bool _isAnalyzing;
    private bool _isAnalysisAvailable;
    private bool _isAnalysisComplete;
    private bool _isWarning;
    private bool _isTruePeakWarning;

    public LoudnessAnalysisItem(string path)
    {
        Path = path;
        FileName = System.IO.Path.GetFileName(path);
    }

    public string Path { get; }
    public string FileName { get; }

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

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value) return;
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        private set
        {
            if (_isAnalyzing == value) return;
            _isAnalyzing = value;
            OnPropertyChanged();
        }
    }

    public bool IsAnalysisAvailable
    {
        get => _isAnalysisAvailable;
        private set
        {
            if (_isAnalysisAvailable == value) return;
            _isAnalysisAvailable = value;
            OnPropertyChanged();
        }
    }

    public bool IsAnalysisComplete
    {
        get => _isAnalysisComplete;
        private set
        {
            if (_isAnalysisComplete == value) return;
            _isAnalysisComplete = value;
            OnPropertyChanged();
        }
    }

    public bool IsWarning
    {
        get => _isWarning;
        private set
        {
            if (_isWarning == value) return;
            _isWarning = value;
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

    public void MarkAnalyzing()
    {
        IsAnalyzing = true;
        IsAnalysisAvailable = false;
        IsAnalysisComplete = false;
        IsWarning = false;
        IsTruePeakWarning = false;
        LoudnessText = "解析中...";
        TruePeakText = "解析中...";
        LoudnessRangeText = "解析中...";
        StatusText = "解析中...";
    }

    public void ApplyResult(
        string loudnessText,
        bool isWarning,
        string truePeakText,
        bool isTruePeakWarning,
        string loudnessRangeText)
    {
        IsAnalyzing = false;
        IsAnalysisAvailable = true;
        IsAnalysisComplete = true;
        IsWarning = isWarning || isTruePeakWarning;
        IsTruePeakWarning = isTruePeakWarning;
        LoudnessText = loudnessText;
        TruePeakText = truePeakText;
        LoudnessRangeText = loudnessRangeText;
        StatusText = isWarning || isTruePeakWarning ? "要確認" : "OK";
    }

    public void MarkUnavailable()
    {
        IsAnalyzing = false;
        IsAnalysisAvailable = false;
        IsAnalysisComplete = true;
        IsWarning = false;
        IsTruePeakWarning = false;
        LoudnessText = "解析不可";
        TruePeakText = "解析不可";
        LoudnessRangeText = "解析不可";
        StatusText = "解析不可";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
