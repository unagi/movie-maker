using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MovieMaker.Models;

public sealed class AudioTrackItem : INotifyPropertyChanged
{
    private const double LowLoudnessReviewThresholdLufs = -15;
    private const double HighLoudnessReviewThresholdLufs = -12;
    private const double HighTruePeakReviewThresholdDbtp = -1;

    private string _infoText = "解析中...";
    private string _loudnessText = string.Empty;
    private string _truePeakText = string.Empty;
    private string _loudnessRangeText = string.Empty;
    private string _audioReviewReasonText = string.Empty;
    private string _audioReviewDetailText = string.Empty;
    private double? _durationSeconds;
    private bool _isDurationAnalysisFailed;
    private int _position;
    private bool _isDropTargetBefore;
    private bool _isDropTargetAfter;
    private bool _isLoudnessAnalysisAvailable;
    private bool _isLoudnessAnalysisComplete;
    private bool _isLoudnessWarning;
    private bool _isTruePeakWarning;
    private bool _isNormalizationOverrideEnabled;
    private string _normalizationTargetLufsText;
    private string _normalizationTargetTruePeakText;

    public AudioTrackItem(string path, double defaultTargetLufs = -14, double defaultTargetTruePeak = -1)
    {
        Path = path;
        FileName = System.IO.Path.GetFileName(path);
        _normalizationTargetLufsText = defaultTargetLufs.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        _normalizationTargetTruePeakText = defaultTargetTruePeak.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }

    public string Path { get; }
    public string FileName { get; }
    public string NormalizationTargetSummaryText => IsNormalizationOverrideEnabled ? "目標：個別" : "目標：共通";
    public bool IsNormalizationOverrideEnabled
    {
        get => _isNormalizationOverrideEnabled;
        set { if (_isNormalizationOverrideEnabled == value) return; _isNormalizationOverrideEnabled = value; OnPropertyChanged(); OnPropertyChanged(nameof(NormalizationTargetsValid)); OnPropertyChanged(nameof(NormalizationTargetSummaryText)); }
    }

    public string NormalizationTargetLufsText
    {
        get => _normalizationTargetLufsText;
        set { if (_normalizationTargetLufsText == value) return; _normalizationTargetLufsText = value; OnPropertyChanged(); OnPropertyChanged(nameof(NormalizationTargetsValid)); }
    }

    public string NormalizationTargetTruePeakText
    {
        get => _normalizationTargetTruePeakText;
        set { if (_normalizationTargetTruePeakText == value) return; _normalizationTargetTruePeakText = value; OnPropertyChanged(); OnPropertyChanged(nameof(NormalizationTargetsValid)); }
    }

    public bool NormalizationTargetsValid => !IsNormalizationOverrideEnabled ||
        (double.TryParse(NormalizationTargetLufsText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lufs) &&
         lufs >= -70 && lufs <= -5 && double.TryParse(NormalizationTargetTruePeakText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var peak) &&
         peak >= -8 && peak <= 0);

    public void UpdateDefaultNormalizationTargets(double targetLufs, double targetTruePeak)
    {
        if (IsNormalizationOverrideEnabled) return;
        NormalizationTargetLufsText = targetLufs.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        NormalizationTargetTruePeakText = targetTruePeak.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }

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
            OnPropertyChanged(nameof(HasAudioMeasurements));
            OnPropertyChanged(nameof(AudioMeasurementStateText));
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
            OnPropertyChanged(nameof(HasAudioMeasurements));
            OnPropertyChanged(nameof(AudioMeasurementStateText));
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

    public bool HasAudioMeasurements => IsLoudnessAnalysisComplete && IsLoudnessAnalysisAvailable;
    public string AudioMeasurementStateText => !IsLoudnessAnalysisComplete
        ? "音量解析中"
        : IsLoudnessAnalysisAvailable ? string.Empty : "音量解析不可";

    public string AudioReviewReasonText
    {
        get => _audioReviewReasonText;
        private set
        {
            if (_audioReviewReasonText == value) return;
            _audioReviewReasonText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasAudioReviewWarning));
        }
    }

    public string AudioReviewDetailText
    {
        get => _audioReviewDetailText;
        private set
        {
            if (_audioReviewDetailText == value) return;
            _audioReviewDetailText = value;
            OnPropertyChanged();
        }
    }

    public bool HasAudioReviewWarning => AudioReviewReasonText.Length > 0;

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

    public bool IsDurationAnalysisFailed
    {
        get => _isDurationAnalysisFailed;
        private set
        {
            if (_isDurationAnalysisFailed == value) return;
            _isDurationAnalysisFailed = value;
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
        var validDuration = durationSeconds.HasValue && double.IsFinite(durationSeconds.Value) && durationSeconds.Value > 0;
        InfoText = validDuration ? infoText : "音声の長さを取得できませんでした";
        DurationSeconds = validDuration ? durationSeconds : null;
        IsDurationAnalysisFailed = !validDuration;
    }

    public void ApplyLoudnessAnalysis(double? integratedLufs, double? truePeakDbtp, double? loudnessRangeLu)
    {
        var hasLoudness = integratedLufs.HasValue && !double.IsNaN(integratedLufs.Value) &&
            !double.IsPositiveInfinity(integratedLufs.Value);
        if (!hasLoudness)
        {
            LoudnessText = string.Empty;
            TruePeakText = string.Empty;
            LoudnessRangeText = string.Empty;
            IsLoudnessWarning = false;
            IsTruePeakWarning = false;
            AudioReviewReasonText = string.Empty;
            AudioReviewDetailText = string.Empty;
            IsLoudnessAnalysisAvailable = false;
            IsLoudnessAnalysisComplete = true;
            return;
        }

        var lufs = integratedLufs!.Value;
        var lufsText = FormatMeasurement(lufs, "LUFS");
        var hasTruePeak = truePeakDbtp.HasValue && !double.IsNaN(truePeakDbtp.Value) &&
            !double.IsPositiveInfinity(truePeakDbtp.Value);
        var truePeakText = hasTruePeak ? FormatMeasurement(truePeakDbtp!.Value, "dBTP") : "— dBTP";
        LoudnessText = lufsText;
        TruePeakText = truePeakText;
        LoudnessRangeText = loudnessRangeLu.HasValue && double.IsFinite(loudnessRangeLu.Value)
            ? $"LRA {loudnessRangeLu.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} LU"
            : "LRA —";

        var reviewReasons = new List<string>();
        var reviewDetails = new List<string>();
        if (lufs < LowLoudnessReviewThresholdLufs)
        {
            reviewReasons.Add("音量低め");
            reviewDetails.Add($"入力音量 {lufsText}：-15 LUFS未満。YouTubeでは小さい音量は引き上げられません。素材または出力結果を確認してください。");
        }
        else if (lufs > HighLoudnessReviewThresholdLufs)
        {
            reviewReasons.Add("音量高め");
            reviewDetails.Add($"入力音量 {lufsText}：-12 LUFS超。仕上がりに応じて素材または出力結果を確認してください。");
        }

        if (hasTruePeak && truePeakDbtp!.Value > HighTruePeakReviewThresholdDbtp)
        {
            reviewReasons.Add("TP超");
            reviewDetails.Add($"入力True Peak {truePeakText}：-1 dBTP超。仕上がりに応じて素材または出力結果を確認してください。");
        }

        IsLoudnessWarning = lufs < LowLoudnessReviewThresholdLufs || lufs > HighLoudnessReviewThresholdLufs;
        IsTruePeakWarning = hasTruePeak && truePeakDbtp!.Value > HighTruePeakReviewThresholdDbtp;
        AudioReviewReasonText = string.Join("・", reviewReasons);
        AudioReviewDetailText = string.Join(Environment.NewLine, reviewDetails);
        IsLoudnessAnalysisAvailable = true;
        IsLoudnessAnalysisComplete = true;
    }

    private static string FormatMeasurement(double value, string unit)
    {
        var text = double.IsNegativeInfinity(value)
            ? "-∞"
            : value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        return $"{text} {unit}";
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
