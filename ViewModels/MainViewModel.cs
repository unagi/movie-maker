using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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
    private string _statusMessage = "準備してください";
    private bool _canEncode;
    private bool _isEncoding;

    private int _imageWidth;
    private int _imageHeight;
    private VideoOrientation? _orientation;
    private bool _aspectValid;

    public MainViewModel()
    {
        OpenSettingsCommand = new RelayCommand(_ => OpenSettings());
        EncodeCommand = new AsyncRelayCommand(EncodeAsync, () => CanEncode);

        UpdateSettingsLabels();
        UpdateValidation(true);
    }

    public RelayCommand OpenSettingsCommand { get; }
    public AsyncRelayCommand EncodeCommand { get; }

    public string Title
    {
        get => _title;
        set
        {
            if (_title == value) return;
            _title = value;
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

            UpdateAspectInfo();
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
        }
    }

    private void SetAudio(string path)
    {
        _audioPath = path;
        AudioFileLabel = $"音楽: {Path.GetFileName(path)}";
    }

    private void UpdateAspectInfo()
    {
        if (_imageWidth <= 0 || _imageHeight <= 0)
        {
            _orientation = null;
            _aspectValid = false;
            OrientationLabel = "向き: 未判定";
            AspectLabel = "比率: 未判定";
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

        CanEncode = errors.Count == 0 && !IsEncoding;

        if (updateStatus)
        {
            StatusMessage = errors.Count == 0 ? "準備完了" : string.Join(" / ", errors);
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

            var titleFolderName = ResolveTitleFolderName(title, outputRoot, archiveRoot);

            var outputFolder = Path.Combine(outputRoot, titleFolderName);
            var archiveFolder = Path.Combine(archiveRoot, titleFolderName);

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
                StatusMessage = $"完了: {result.OutputPath}";
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

    private static string ResolveTitleFolderName(string title, string outputRoot, string archiveRoot)
    {
        var baseName = title;
        if (Directory.Exists(Path.Combine(outputRoot, baseName)) ||
            Directory.Exists(Path.Combine(archiveRoot, baseName)))
        {
            baseName = $"{title}_{DateTime.Now:yyyyMMdd}";
        }

        var candidate = baseName;
        var index = 1;
        while (Directory.Exists(Path.Combine(outputRoot, candidate)) ||
               Directory.Exists(Path.Combine(archiveRoot, candidate)))
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
