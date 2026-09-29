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
    private string _shortsMaximumSeconds;
    private readonly bool _normalTextOverlayEnabled;
    private string _normalizationTargetIntegratedLufs;
    private string _normalizationTargetTruePeakDbtp;
    private readonly string _textOverlayLayoutJson;

    public SettingsViewModel(AppSettings settings)
    {
        _outputDirectory = settings.OutputDirectory;
        _archiveDirectory = settings.ArchiveDirectory;
        _oneMinuteShortsOffsetSeconds = settings.OneMinuteShortsOffsetSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        _threeMinuteShortsOffsetSeconds = settings.ThreeMinuteShortsOffsetSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        _shortsMaximumSeconds = settings.ShortsMaximumSeconds.ToString();
        _normalTextOverlayEnabled = settings.NormalTextOverlayEnabled;
        _normalizationTargetIntegratedLufs = settings.NormalizationTargetIntegratedLufs.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        _normalizationTargetTruePeakDbtp = settings.NormalizationTargetTruePeakDbtp.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        _textOverlayLayoutJson = settings.TextOverlayLayoutJson;
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
            OnPropertyChanged(nameof(ShortsSettingsError));
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
            OnPropertyChanged(nameof(ShortsSettingsError));
        }
    }

    public string ShortsMaximumSeconds
    {
        get => _shortsMaximumSeconds;
        set
        {
            if (_shortsMaximumSeconds == value) return;
            _shortsMaximumSeconds = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShortsSettingsError));
        }
    }

    public string ShortsSettingsError => TryReadShortsSettings(out _, out _, out _, out var error)
        ? string.Empty : error ?? string.Empty;

    public string NormalizationTargetIntegratedLufs
    {
        get => _normalizationTargetIntegratedLufs;
        set { if (_normalizationTargetIntegratedLufs == value) return; _normalizationTargetIntegratedLufs = value; OnPropertyChanged(); }
    }

    public string NormalizationTargetTruePeakDbtp
    {
        get => _normalizationTargetTruePeakDbtp;
        set { if (_normalizationTargetTruePeakDbtp == value) return; _normalizationTargetTruePeakDbtp = value; OnPropertyChanged(); }
    }

    public bool TryCreateSettings(out AppSettings settings, out string? errorMessage)
    {
        settings = new AppSettings();
        errorMessage = null;

        if (!TryReadShortsSettings(out var shortsMaximum, out var oneMinuteOffset,
                out var threeMinuteOffset, out errorMessage))
            return false;

        if (!double.TryParse(NormalizationTargetIntegratedLufs?.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var targetLufs) ||
            targetLufs < EncodingService.MinimumTargetIntegratedLufs || targetLufs > EncodingService.MaximumTargetIntegratedLufs)
        {
            errorMessage = "ノーマライズ目標LUFSは -70〜-5 の数値で入力してください。";
            return false;
        }

        if (!double.TryParse(NormalizationTargetTruePeakDbtp?.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var targetTruePeak) ||
            targetTruePeak < EncodingService.MinimumTargetTruePeakDbtp || targetTruePeak > EncodingService.MaximumTargetTruePeakDbtp)
        {
            errorMessage = "True Peak目標は -8〜0 dBTP の数値で入力してください。";
            return false;
        }

        settings = new AppSettings
        {
            OutputDirectory = OutputDirectory?.Trim() ?? string.Empty,
            ArchiveDirectory = ArchiveDirectory?.Trim() ?? string.Empty,
            OneMinuteShortsOffsetSeconds = oneMinuteOffset,
            ThreeMinuteShortsOffsetSeconds = threeMinuteOffset,
            ShortsMaximumSeconds = shortsMaximum,
            NormalTextOverlayEnabled = _normalTextOverlayEnabled,
            NormalizationTargetIntegratedLufs = Math.Round(targetLufs, 3),
            NormalizationTargetTruePeakDbtp = Math.Round(targetTruePeak, 3),
            TextOverlayLayoutJson = _textOverlayLayoutJson
        };
        return true;
    }

    private bool TryReadShortsSettings(out int shortsMaximum, out double oneMinuteOffset,
        out double threeMinuteOffset, out string? errorMessage)
    {
        errorMessage = null;
        oneMinuteOffset = 0;
        threeMinuteOffset = 0;
        if (!int.TryParse(ShortsMaximumSeconds?.Trim(), out shortsMaximum) || shortsMaximum is < 1 or > 180)
        {
            errorMessage = "ショート動画の最大秒数は 1〜180 の整数で入力してください。";
            return false;
        }

        if (!double.TryParse(OneMinuteShortsOffsetSeconds?.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out oneMinuteOffset) || !double.IsFinite(oneMinuteOffset) || oneMinuteOffset < 0 || oneMinuteOffset > 59)
        {
            errorMessage = "1分Shortsオフセットは 0〜59 の数値で入力してください。";
            return false;
        }

        if (!double.TryParse(ThreeMinuteShortsOffsetSeconds?.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out threeMinuteOffset) || !double.IsFinite(threeMinuteOffset) || threeMinuteOffset < 0 || threeMinuteOffset >= 120)
        {
            errorMessage = "3分Shortsオフセットは 0〜120 未満の数値で入力してください。";
            return false;
        }
        return true;
    }

    public string VersionText => VersionService.DisplayVersion;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
