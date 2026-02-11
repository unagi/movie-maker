using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using MovieMaker.Infrastructure;
using MovieMaker.Models;
using MovieMaker.Services;
using MovieMaker.Views;

namespace MovieMaker.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private const double AspectTolerance = 0.01; // 1%
    private const double VerticalRatio = 9.0 / 16.0;
    private const double HorizontalRatio = 16.0 / 9.0;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".m4a", ".flac", ".aac"
    };

    private static readonly Regex MultiWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    private static readonly Regex[] TrailingNoisePatterns =
    {
        new(@"\s*[（(]\d+[)）]\s*$", RegexOptions.Compiled),
        new(@"\s*(?:[-_]\s*)?(?:のコピー|コピー|copy)(?:\s*[（(]\d+[)）]|\s+\d+)?\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase)
    };

    private string _title = string.Empty;
    private string? _imagePath;
    private string? _audioPath;
    private BitmapImage? _imagePreview;
    private string _imageFileLabel = "画像: 未設定";
    private string _audioFileLabel = "音楽: 未設定";
    private string _orientationLabel = "向き: 未判定";
    private string _aspectLabel = "比率: 未判定";
    private string _outputDirectoryLabel = "出力先: 未設定";
    private string _archiveDirectoryLabel = "アーカイブ: 未設定";
    private string _outputDirectoryPath = string.Empty;
    private string _archiveDirectoryPath = string.Empty;
    private string _statusMessage = "準備してください";
    private string _audioInfoText = string.Empty;
    private string? _autoFilledTitle;
    private double? _audioDurationSeconds;
    private bool _canEncode;
    private bool _isEncoding;
    private bool _isApplyingAutoTitle;

    private int _imageWidth;
    private int _imageHeight;
    private VideoOrientation? _orientation;
    private bool _aspectValid;

    public MainViewModel()
    {
        OpenSettingsCommand = new RelayCommand(_ => OpenSettings());
        OpenOutputFolderCommand = new RelayCommand(_ => OpenFolder(OutputDirectoryPath), _ => Directory.Exists(OutputDirectoryPath));
        OpenArchiveFolderCommand = new RelayCommand(_ => OpenFolder(ArchiveDirectoryPath), _ => Directory.Exists(ArchiveDirectoryPath));
        ClearInputsCommand = new RelayCommand(_ => ClearInputs(), _ => CanClearInputs);
        EncodeCommand = new AsyncRelayCommand(EncodeAsync, () => CanEncode);

        UpdateSettingsLabels();
        UpdateValidation(true);
    }

    public RelayCommand OpenSettingsCommand { get; }
    public RelayCommand OpenOutputFolderCommand { get; }
    public RelayCommand OpenArchiveFolderCommand { get; }
    public RelayCommand ClearInputsCommand { get; }
    public AsyncRelayCommand EncodeCommand { get; }

    public string Title
    {
        get => _title;
        set
        {
            if (_title == value) return;
            _title = value;
            if (!_isApplyingAutoTitle)
            {
                _autoFilledTitle = null;
            }
            OnPropertyChanged();
            UpdateValidation(true);
        }
    }

    public BitmapImage? ImagePreview
    {
        get => _imagePreview;
        private set
        {
            if (_imagePreview == value) return;
            _imagePreview = value;
            OnPropertyChanged();
        }
    }

    public string ImageFileLabel
    {
        get => _imageFileLabel;
        private set
        {
            if (_imageFileLabel == value) return;
            _imageFileLabel = value;
            OnPropertyChanged();
        }
    }

    public string AudioFileLabel
    {
        get => _audioFileLabel;
        private set
        {
            if (_audioFileLabel == value) return;
            _audioFileLabel = value;
            OnPropertyChanged();
        }
    }

    public string OrientationLabel
    {
        get => _orientationLabel;
        private set
        {
            if (_orientationLabel == value) return;
            _orientationLabel = value;
            OnPropertyChanged();
        }
    }

    public string AspectLabel
    {
        get => _aspectLabel;
        private set
        {
            if (_aspectLabel == value) return;
            _aspectLabel = value;
            OnPropertyChanged();
        }
    }

    public string OutputDirectoryLabel
    {
        get => _outputDirectoryLabel;
        private set
        {
            if (_outputDirectoryLabel == value) return;
            _outputDirectoryLabel = value;
            OnPropertyChanged();
        }
    }

    public string ArchiveDirectoryLabel
    {
        get => _archiveDirectoryLabel;
        private set
        {
            if (_archiveDirectoryLabel == value) return;
            _archiveDirectoryLabel = value;
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value) return;
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public bool CanEncode
    {
        get => _canEncode;
        private set
        {
            if (_canEncode == value) return;
            _canEncode = value;
            OnPropertyChanged();
            EncodeCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsEncoding
    {
        get => _isEncoding;
        private set
        {
            if (_isEncoding == value) return;
            _isEncoding = value;
            OnPropertyChanged();
        }
    }

    public string OutputDirectoryPath
    {
        get => _outputDirectoryPath;
        private set
        {
            if (_outputDirectoryPath == value) return;
            _outputDirectoryPath = value;
            OnPropertyChanged();
        }
    }

    public string ArchiveDirectoryPath
    {
        get => _archiveDirectoryPath;
        private set
        {
            if (_archiveDirectoryPath == value) return;
            _archiveDirectoryPath = value;
            OnPropertyChanged();
        }
    }

    public bool CanClearInputs => !IsEncoding && (IsImageReady || IsAudioReady);

    public bool IsImageReady => !string.IsNullOrWhiteSpace(_imagePath);
    public string ImageStatusText
    {
        get
        {
            if (!IsImageReady)
            {
                return "未設定";
            }

            var name = Path.GetFileName(_imagePath!);
            if (_imageWidth > 0 && _imageHeight > 0)
            {
                return $"{name} ({_imageWidth}x{_imageHeight})";
            }

            return name;
        }
    }

    public bool IsAudioReady => !string.IsNullOrWhiteSpace(_audioPath);
    public string AudioStatusText
    {
        get
        {
            if (!IsAudioReady)
            {
                return "未設定";
            }

            var name = Path.GetFileName(_audioPath!);
            if (string.IsNullOrWhiteSpace(_audioInfoText))
            {
                return name;
            }

            return $"{name} ({_audioInfoText})";
        }
    }

    public bool IsOutputReady => _aspectValid;
    public string OutputStatusText
    {
        get
        {
            if (!_aspectValid || _orientation == null)
            {
                return "未判定";
            }

            var label = _orientation == VideoOrientation.Vertical ? "YouTube Short" : "YouTube";
            if (!_audioDurationSeconds.HasValue)
            {
                return $"{label} (時間未取得)";
            }

            var duration = _audioDurationSeconds.Value;
            if (_orientation == VideoOrientation.Vertical && duration >= 180)
            {
                duration = 179;
            }

            return $"{label} ({FormatDuration(duration)})";
        }
    }

    public string EncodingSettingsText
    {
        get
        {
            var lines = new List<string>
            {
                "映像",
                $"・出力解像度: {GetTargetResolutionText()}",
                $"・向き判定: {GetOrientationText()}",
                "・フレームレート: 30 fps",
                "・ピクセル形式: yuv420p",
                "・アスペクト処理: scale + setsar=1",
                "・映像エンコーダ: NVENC/QSV/AMF優先、非対応時はlibx264",
                string.Empty,
                "音声",
                "・コーデック: AAC",
                "・ビットレート: 320 kbps",
                "・サンプリング周波数: 48 kHz",
                string.Empty,
                "出力制御"
            };

            if (_orientation == VideoOrientation.Vertical)
            {
                if (_audioDurationSeconds.HasValue)
                {
                    lines.Add(_audioDurationSeconds.Value >= 180
                        ? "・Shorts制限: 2:59に短縮（末尾1秒フェードアウト）"
                        : "・Shorts制限: 短縮なし（3分未満）");
                }
                else
                {
                    lines.Add("・Shorts制限: 音声長の解析待ち");
                }
            }
            else
            {
                lines.Add("・Shorts制限: 対象外（横動画）");
            }

            lines.Add("・終了条件: -shortest（短い入力長に合わせる）");
            lines.Add("・Web最適化: +faststart");

            return string.Join(Environment.NewLine, lines);
        }
    }

    public void HandleDrop(string[] files)
    {
        if (files == null || files.Length == 0)
        {
            return;
        }

        foreach (var file in files)
        {
            if (!File.Exists(file))
            {
                continue;
            }

            var ext = Path.GetExtension(file);
            if (ImageExtensions.Contains(ext))
            {
                SetImage(file);
            }
            else if (AudioExtensions.Contains(ext))
            {
                SetAudio(file);
            }
        }

        UpdateValidation(true);
    }

    private void SetImage(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();

            _imagePath = path;
            ImagePreview = bitmap;
            ImageFileLabel = $"画像: {Path.GetFileName(path)}";
            _imageWidth = bitmap.PixelWidth;
            _imageHeight = bitmap.PixelHeight;
            TryAutoFillTitleFromImage(path);

            UpdateAspectInfo();
            NotifyStatusChanged();
        }
        catch
        {
            _imagePath = null;
            ImagePreview = null;
            _imageWidth = 0;
            _imageHeight = 0;
            _orientation = null;
            _aspectValid = false;
            ImageFileLabel = "画像: 読み込み失敗";
            OrientationLabel = "向き: 未判定";
            AspectLabel = "比率: 未判定";
            NotifyStatusChanged();
        }
    }

    private void SetAudio(string path)
    {
        _audioPath = path;
        _audioInfoText = "解析中...";
        _audioDurationSeconds = null;
        AudioFileLabel = $"音楽: {Path.GetFileName(path)}";
        NotifyStatusChanged();
        _ = UpdateAudioInfoAsync(path);
    }

    private void ClearInputs()
    {
        _imagePath = null;
        _audioPath = null;
        _audioInfoText = string.Empty;
        _audioDurationSeconds = null;
        _imageWidth = 0;
        _imageHeight = 0;
        _orientation = null;
        _aspectValid = false;

        ImagePreview = null;
        ImageFileLabel = "画像: 未設定";
        AudioFileLabel = "音楽: 未設定";
        OrientationLabel = "向き: 未判定";
        AspectLabel = "比率: 未判定";

        NotifyStatusChanged();
        UpdateValidation(true);
    }

    private void UpdateAspectInfo()
    {
        if (_imageWidth <= 0 || _imageHeight <= 0)
        {
            _orientation = null;
            _aspectValid = false;
            OrientationLabel = "向き: 未判定";
            AspectLabel = "比率: 未判定";
            NotifyStatusChanged();
            return;
        }

        var ratio = (double)_imageWidth / _imageHeight;
        var isVertical = IsRatioClose(ratio, VerticalRatio);
        var isHorizontal = IsRatioClose(ratio, HorizontalRatio);

        _aspectValid = isVertical || isHorizontal;
        if (isVertical)
        {
            _orientation = VideoOrientation.Vertical;
            OrientationLabel = "向き: 縦 (9:16)";
            AspectLabel = $"比率: {ratio:F3} (9:16)";
        }
        else if (isHorizontal)
        {
            _orientation = VideoOrientation.Horizontal;
            OrientationLabel = "向き: 横 (16:9)";
            AspectLabel = $"比率: {ratio:F3} (16:9)";
        }
        else
        {
            _orientation = null;
            OrientationLabel = "向き: 未判定";
            AspectLabel = $"比率: {ratio:F3} (9:16/16:9以外)";
        }
        NotifyStatusChanged();
    }

    private static bool IsRatioClose(double ratio, double target)
    {
        return Math.Abs(ratio - target) / target <= AspectTolerance;
    }

    private void UpdateSettingsLabels()
    {
        var output = SettingsService.Current.OutputDirectory?.Trim() ?? string.Empty;
        var archive = SettingsService.Current.ArchiveDirectory?.Trim() ?? string.Empty;

        OutputDirectoryLabel = string.IsNullOrWhiteSpace(output)
            ? "出力先: 未設定"
            : $"出力先: {output}";

        ArchiveDirectoryLabel = string.IsNullOrWhiteSpace(archive)
            ? "アーカイブ: 未設定"
            : $"アーカイブ: {archive}";

        OutputDirectoryPath = output;
        ArchiveDirectoryPath = archive;

        OpenOutputFolderCommand.RaiseCanExecuteChanged();
        OpenArchiveFolderCommand.RaiseCanExecuteChanged();
    }

    private void UpdateValidation(bool updateStatus)
    {
        var errors = new List<string>();
        var title = Title?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(title))
        {
            errors.Add("タイトルを入力してください");
        }
        else if (ContainsInvalidTitleChars(title))
        {
            errors.Add("タイトルに使用できない文字が含まれています");
        }

        if (string.IsNullOrWhiteSpace(SettingsService.Current.OutputDirectory))
        {
            errors.Add("出力先ディレクトリが未設定です");
        }

        if (string.IsNullOrWhiteSpace(SettingsService.Current.ArchiveDirectory))
        {
            errors.Add("アーカイブディレクトリが未設定です");
        }

        if (string.IsNullOrWhiteSpace(_imagePath))
        {
            errors.Add("画像が未設定です");
        }
        else if (!_aspectValid)
        {
            errors.Add("画像の比率が9:16または16:9ではありません");
        }

        if (string.IsNullOrWhiteSpace(_audioPath))
        {
            errors.Add("音楽が未設定です");
        }

        if (EncodingService.ResolveFfmpegPath() == null)
        {
            errors.Add("ffmpegが見つかりません (PATH)");
        }

        var hasErrors = errors.Count > 0;
        CanEncode = !hasErrors && !IsEncoding;
        ClearInputsCommand.RaiseCanExecuteChanged();

        if (updateStatus)
        {
            if (!hasErrors)
            {
                StatusMessage = "準備完了";
            }
            else if (string.IsNullOrWhiteSpace(title) && !IsImageReady && !IsAudioReady)
            {
                StatusMessage = "準備してください";
            }
            else
            {
                StatusMessage = "入力内容を確認してください";
            }
        }
    }

    private void NotifyStatusChanged()
    {
        OnPropertyChanged(nameof(IsImageReady));
        OnPropertyChanged(nameof(ImageStatusText));
        OnPropertyChanged(nameof(IsAudioReady));
        OnPropertyChanged(nameof(AudioStatusText));
        OnPropertyChanged(nameof(IsOutputReady));
        OnPropertyChanged(nameof(OutputStatusText));
        OnPropertyChanged(nameof(EncodingSettingsText));
        OnPropertyChanged(nameof(CanClearInputs));
    }

    private string GetTargetResolutionText()
    {
        return _orientation switch
        {
            VideoOrientation.Vertical => "1080x1920",
            VideoOrientation.Horizontal => "1920x1080",
            _ => "未確定"
        };
    }

    private string GetOrientationText()
    {
        return _orientation switch
        {
            VideoOrientation.Vertical => "縦 (9:16)",
            VideoOrientation.Horizontal => "横 (16:9)",
            _ => "未判定"
        };
    }

    private async Task UpdateAudioInfoAsync(string path)
    {
        var ffmpegPath = EncodingService.ResolveFfmpegPath();
        var info = await EncodingService.GetAudioInfoAsync(path, ffmpegPath);

        if (!string.Equals(_audioPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (info == null)
        {
            _audioInfoText = "情報取得不可";
            _audioDurationSeconds = null;
        }
        else
        {
            _audioInfoText = FormatAudioInfo(info);
            _audioDurationSeconds = info.DurationSeconds;
        }
        NotifyStatusChanged();
    }

    private static string FormatAudioInfo(AudioInfo info)
    {
        var parts = new List<string>();

        if (info.SampleRate.HasValue)
        {
            parts.Add($"{info.SampleRate.Value / 1000.0:0.#}kHz");
        }

        if (info.BitDepth.HasValue)
        {
            if (!string.IsNullOrWhiteSpace(info.SampleFormat) &&
                (info.SampleFormat.StartsWith("flt", StringComparison.OrdinalIgnoreCase) ||
                 info.SampleFormat.StartsWith("dbl", StringComparison.OrdinalIgnoreCase)))
            {
                parts.Add($"{info.BitDepth.Value}bit float");
            }
            else
            {
                parts.Add($"{info.BitDepth.Value}bit");
            }
        }

        if (info.Channels.HasValue)
        {
            parts.Add($"{info.Channels.Value}ch");
        }

        if (info.BitRate.HasValue && !info.BitDepth.HasValue)
        {
            parts.Add($"{info.BitRate.Value / 1000}kbps");
        }

        return parts.Count == 0 ? "情報取得不可" : string.Join(" / ", parts);
    }

    private static string FormatDuration(double seconds)
    {
        var totalSeconds = Math.Max(0, seconds);
        var ts = TimeSpan.FromSeconds(totalSeconds);
        return ts.TotalHours >= 1
            ? ts.ToString("h\\:mm\\:ss")
            : ts.ToString("m\\:ss");
    }

    private static void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch
        {
            // ignore
        }
    }

    private static bool ContainsInvalidTitleChars(string title)
    {
        if (title.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return true;
        }

        if (title.EndsWith('.') || title.EndsWith(' '))
        {
            return true;
        }

        return false;
    }

    private void TryAutoFillTitleFromImage(string path)
    {
        var suggested = BuildTitleFromImageFileName(path);
        if (string.IsNullOrWhiteSpace(suggested))
        {
            return;
        }

        var current = Title?.Trim() ?? string.Empty;
        var shouldApply = string.IsNullOrWhiteSpace(current)
            || (!string.IsNullOrWhiteSpace(_autoFilledTitle) &&
                string.Equals(current, _autoFilledTitle, StringComparison.Ordinal));

        if (!shouldApply)
        {
            return;
        }

        _isApplyingAutoTitle = true;
        try
        {
            Title = suggested;
            _autoFilledTitle = suggested;
        }
        finally
        {
            _isApplyingAutoTitle = false;
        }
    }

    private static string BuildTitleFromImageFileName(string path)
    {
        var original = Path.GetFileNameWithoutExtension(path)?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(original))
        {
            return string.Empty;
        }

        var candidate = NormalizeWhitespace(original);

        while (true)
        {
            var previous = candidate;
            candidate = RemoveTrailingNoise(candidate);
            if (candidate.Length == 0 || string.Equals(previous, candidate, StringComparison.Ordinal))
            {
                break;
            }
        }

        return string.IsNullOrWhiteSpace(candidate) ? NormalizeWhitespace(original) : candidate;
    }

    private static string RemoveTrailingNoise(string value)
    {
        var result = value;

        foreach (var pattern in TrailingNoisePatterns)
        {
            var replaced = pattern.Replace(result, string.Empty);
            if (!string.Equals(replaced, result, StringComparison.Ordinal))
            {
                result = TrimTrailingSeparators(replaced);
            }
        }

        return NormalizeWhitespace(result);
    }

    private static string NormalizeWhitespace(string value)
    {
        return MultiWhitespaceRegex.Replace(value.Replace('　', ' ').Trim(), " ");
    }

    private static string TrimTrailingSeparators(string value)
    {
        return value.TrimEnd(' ', '\t', '_', '-');
    }

    private void OpenSettings()
    {
        var window = new SettingsWindow
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        var result = window.ShowDialog();
        if (result == true)
        {
            UpdateSettingsLabels();
            UpdateValidation(true);
        }
    }

    private async Task EncodeAsync()
    {
        UpdateValidation(true);
        if (!CanEncode)
        {
            return;
        }

        if (_orientation == null || _imagePath == null || _audioPath == null)
        {
            StatusMessage = "入力が不足しています";
            return;
        }

        var ffmpegPath = EncodingService.ResolveFfmpegPath();
        if (ffmpegPath == null)
        {
            StatusMessage = "ffmpegが見つかりません (PATH)";
            return;
        }

        try
        {
            IsEncoding = true;
            UpdateValidation(false);
            StatusMessage = "エンコード中...";

            var title = Title.Trim();
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            var outputRoot = SettingsService.Current.OutputDirectory.Trim();
            var archiveRoot = SettingsService.Current.ArchiveDirectory.Trim();

            var archiveFolderName = ResolveArchiveFolderName(title, archiveRoot);

            var outputFolder = outputRoot;
            var archiveFolder = Path.Combine(archiveRoot, archiveFolderName);

            Directory.CreateDirectory(outputFolder);
            Directory.CreateDirectory(archiveFolder);

            File.Copy(_imagePath, Path.Combine(archiveFolder, Path.GetFileName(_imagePath)), overwrite: false);
            File.Copy(_audioPath, Path.Combine(archiveFolder, Path.GetFileName(_audioPath)), overwrite: false);

            var outputPath = Path.Combine(outputFolder, $"{title}_{timestamp}.mp4");
            var logPath = Path.Combine(EncodingService.GetLogDirectory(), $"{title}_{timestamp}.log");

            var (width, height) = _orientation == VideoOrientation.Vertical
                ? (1080, 1920)
                : (1920, 1080);

            var request = new EncodeRequest(
                ffmpegPath,
                _imagePath,
                _audioPath,
                outputPath,
                _orientation.Value,
                width,
                height,
                logPath);

            var result = await EncodingService.EncodeAsync(request);

            if (result.Success)
            {
                StatusMessage = string.IsNullOrWhiteSpace(result.Encoder)
                    ? $"完了: {result.OutputPath}"
                    : $"完了: {result.OutputPath} (Encoder: {result.Encoder})";
            }
            else
            {
                StatusMessage = $"失敗: {result.ErrorMessage} (ログ: {result.LogPath})";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"失敗: {ex.Message}";
        }
        finally
        {
            IsEncoding = false;
            UpdateValidation(false);
        }
    }

    private static string ResolveArchiveFolderName(string title, string archiveRoot)
    {
        var baseName = title;
        if (Directory.Exists(Path.Combine(archiveRoot, baseName)))
        {
            baseName = $"{title}_{DateTime.Now:yyyyMMdd}";
        }

        var candidate = baseName;
        var index = 1;
        while (Directory.Exists(Path.Combine(archiveRoot, candidate)))
        {
            candidate = $"{baseName}_{index}";
            index++;
        }

        return candidate;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
