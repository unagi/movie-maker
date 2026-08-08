using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    private string? _autoFilledTitle;
    private double? _audioDurationSeconds;
    private bool _canEncode;
    private bool _isEncoding;
    private bool _isApplyingAutoTitle;
    private EncodeProfile _selectedProfile = EncodeProfile.Standard;
    private DraftAudioQuality _draftAudioQuality = DraftAudioQuality.High;

    private int _imageWidth;
    private int _imageHeight;
    private VideoOrientation? _orientation;
    private bool _aspectValid;
    private VideoOrientation _draftOrientation = VideoOrientation.Horizontal;

    public MainViewModel()
    {
        OpenSettingsCommand = new RelayCommand(_ => OpenSettings());
        OpenOutputFolderCommand = new RelayCommand(_ => OpenFolder(OutputDirectoryPath), _ => Directory.Exists(OutputDirectoryPath));
        OpenArchiveFolderCommand = new RelayCommand(_ => OpenFolder(ArchiveDirectoryPath), _ => Directory.Exists(ArchiveDirectoryPath));
        ClearInputsCommand = new RelayCommand(_ => ClearInputs(), _ => CanClearInputs);
        RemoveAudioTrackCommand = new RelayCommand(RemoveAudioTrack, track => !IsEncoding && track is AudioTrackItem);
        EncodeCommand = new AsyncRelayCommand(EncodeAsync, () => CanEncode);

        UpdateSettingsLabels();
        UpdateValidation(true);
    }

    public RelayCommand OpenSettingsCommand { get; }
    public RelayCommand OpenOutputFolderCommand { get; }
    public RelayCommand OpenArchiveFolderCommand { get; }
    public RelayCommand ClearInputsCommand { get; }
    public RelayCommand RemoveAudioTrackCommand { get; }
    public AsyncRelayCommand EncodeCommand { get; }
    public ObservableCollection<AudioTrackItem> AudioTracks { get; } = [];

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

    public bool CanClearInputs => !IsEncoding && (!string.IsNullOrWhiteSpace(_imagePath) || IsAudioReady);

    public EncodeProfile SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (_selectedProfile == value) return;
            _selectedProfile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(UseDraftMode));
            OnPropertyChanged(nameof(IsStandardProfile));
            OnPropertyChanged(nameof(IsCopyrightCheckProductionProfile));
            OnPropertyChanged(nameof(IsDraftProfile));
            OnPropertyChanged(nameof(IsDraftOrientationHorizontal));
            OnPropertyChanged(nameof(IsDraftOrientationVertical));
            OnPropertyChanged(nameof(IsDraftAudioQualityHigh));
            OnPropertyChanged(nameof(IsDraftAudioQualityLow));
            NotifyStatusChanged();
            UpdateValidation(true);
        }
    }

    public bool UseDraftMode
    {
        get => SelectedProfile == EncodeProfile.DraftPreview;
        set
        {
            if (value)
            {
                SelectedProfile = EncodeProfile.DraftPreview;
            }
            else if (SelectedProfile == EncodeProfile.DraftPreview)
            {
                SelectedProfile = EncodeProfile.Standard;
            }
        }
    }

    public bool IsStandardProfile
    {
        get => SelectedProfile == EncodeProfile.Standard;
        set
        {
            if (value)
            {
                SelectedProfile = EncodeProfile.Standard;
            }
        }
    }

    public bool IsCopyrightCheckProductionProfile
    {
        get => SelectedProfile == EncodeProfile.CopyrightCheckProduction;
        set
        {
            if (value)
            {
                SelectedProfile = EncodeProfile.CopyrightCheckProduction;
            }
        }
    }

    public bool IsDraftProfile
    {
        get => SelectedProfile == EncodeProfile.DraftPreview;
        set
        {
            if (value)
            {
                SelectedProfile = EncodeProfile.DraftPreview;
            }
        }
    }

    public VideoOrientation DraftOrientation
    {
        get => _draftOrientation;
        set
        {
            if (_draftOrientation == value) return;
            _draftOrientation = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDraftOrientationHorizontal));
            OnPropertyChanged(nameof(IsDraftOrientationVertical));
            NotifyStatusChanged();
            UpdateValidation(true);
        }
    }

    public bool IsDraftAudioQualityHigh
    {
        get => _draftAudioQuality == DraftAudioQuality.High;
        set
        {
            if (!value)
            {
                return;
            }

            if (_draftAudioQuality == DraftAudioQuality.High)
            {
                return;
            }

            _draftAudioQuality = DraftAudioQuality.High;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDraftAudioQualityLow));
            NotifyStatusChanged();
        }
    }

    public bool IsDraftAudioQualityLow
    {
        get => _draftAudioQuality == DraftAudioQuality.Low;
        set
        {
            if (!value)
            {
                return;
            }

            if (_draftAudioQuality == DraftAudioQuality.Low)
            {
                return;
            }

            _draftAudioQuality = DraftAudioQuality.Low;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDraftAudioQualityHigh));
            NotifyStatusChanged();
        }
    }

    public bool IsDraftOrientationHorizontal
    {
        get => DraftOrientation == VideoOrientation.Horizontal;
        set
        {
            if (value)
            {
                DraftOrientation = VideoOrientation.Horizontal;
            }
        }
    }

    public bool IsDraftOrientationVertical
    {
        get => DraftOrientation == VideoOrientation.Vertical;
        set
        {
            if (value)
            {
                DraftOrientation = VideoOrientation.Vertical;
            }
        }
    }

    public bool ShowReplaceImageHint => SelectedProfile != EncodeProfile.DraftPreview && IsImageReady;

    public bool IsImageReady => SelectedProfile == EncodeProfile.DraftPreview || !string.IsNullOrWhiteSpace(_imagePath);
    public string ImageStatusText
    {
        get
        {
            if (SelectedProfile == EncodeProfile.DraftPreview)
            {
                return "仮画像を生成 (横 960x540)";
            }

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

    public bool IsAudioReady => AudioTracks.Count > 0;
    public string AudioStatusText
    {
        get
        {
            if (!IsAudioReady)
            {
                return "未設定";
            }

            if (AudioTracks.Count > 1)
            {
                return _audioDurationSeconds.HasValue
                    ? $"{AudioTracks.Count}ファイル ({FormatDuration(_audioDurationSeconds.Value)})"
                    : $"{AudioTracks.Count}ファイル";
            }

            var track = AudioTracks[0];
            return string.IsNullOrWhiteSpace(track.InfoText)
                ? track.FileName
                : $"{track.FileName} ({track.InfoText})";
        }
    }

    public bool IsOutputReady => SelectedProfile == EncodeProfile.DraftPreview || _aspectValid;
    public string OutputStatusText
    {
        get
        {
            var orientation = GetEffectiveOrientation();
            if (orientation == null || (SelectedProfile != EncodeProfile.DraftPreview && !_aspectValid))
            {
                return "未判定";
            }

            var label = GetOutputModeLabel(orientation.Value);
            if (!_audioDurationSeconds.HasValue)
            {
                return $"{label} (時間未取得)";
            }

            var duration = _audioDurationSeconds.Value;
            if (orientation == VideoOrientation.Vertical &&
                ShortsPolicy.TryGetTrimTargetSeconds(duration, out var targetSeconds))
            {
                duration = targetSeconds;
            }

            return $"{label} ({FormatDuration(duration)})";
        }
    }

    public string EncodingSettingsText
    {
        get
        {
            var options = EncodingOptionsResolver.Resolve(GetEffectiveOrientation() ?? DraftOrientation, GetSelectedProfile(), _draftAudioQuality);
            var lines = new List<string>
            {
                $"モード: {GetProfileDescription(GetSelectedProfile())}",
                string.Empty,
                "映像",
                $"・ソース: {(SelectedProfile == EncodeProfile.DraftPreview ? "仮画像を自動生成" : "入力画像を使用")}",
                $"・出力解像度: {GetTargetResolutionText()}",
                $"・向き判定: {GetOrientationText()}",
                $"・フレームレート: {options.FrameRate} fps",
                "・ピクセル形式: yuv420p",
                "・アスペクト処理: scale + setsar=1",
                "・映像エンコーダ: NVENC/QSV/AMF優先、非対応時はlibx264",
                string.Empty,
                "音声",
                "・コーデック: AAC",
                $"・入力: {(AudioTracks.Count > 1 ? $"{AudioTracks.Count}ファイルを表示順で連結" : "単一ファイル")}",
                $"・ビットレート: {options.AudioBitrate}",
                $"・サンプリング周波数: {int.Parse(options.AudioSampleRate) / 1000.0:0.#} kHz",
                string.Empty,
                "出力制御"
            };

            if ((GetEffectiveOrientation() ?? DraftOrientation) == VideoOrientation.Vertical)
            {
                if (_audioDurationSeconds.HasValue)
                {
                    lines.Add(GetShortsRuleText(_audioDurationSeconds.Value));
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

        var audioFiles = new List<string>();
        foreach (var file in files)
        {
            if (!File.Exists(file))
            {
                continue;
            }

            var ext = Path.GetExtension(file);
            if (SelectedProfile != EncodeProfile.DraftPreview && ImageExtensions.Contains(ext))
            {
                SetImage(file);
            }
            else if (AudioExtensions.Contains(ext))
            {
                audioFiles.Add(file);
            }
        }

        var (addedCount, duplicateCount) = AddAudioFiles(audioFiles);
        UpdateValidation(true);
        if (addedCount > 0 || duplicateCount > 0)
        {
            StatusMessage = duplicateCount > 0
                ? $"音声{addedCount}件を追加、重複{duplicateCount}件をスキップしました"
                : $"音声{addedCount}件を追加しました（合計{AudioTracks.Count}件）";
        }
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

    private (int AddedCount, int DuplicateCount) AddAudioFiles(IEnumerable<string> paths)
    {
        var existingPaths = new HashSet<string>(AudioTracks.Select(track => track.Path), StringComparer.OrdinalIgnoreCase);
        var addedCount = 0;
        var duplicateCount = 0;
        _audioDurationSeconds = null;

        foreach (var path in paths.Where(File.Exists))
        {
            if (!existingPaths.Add(path))
            {
                duplicateCount++;
                continue;
            }

            var track = new AudioTrackItem(path);
            AudioTracks.Add(track);
            addedCount++;
            _ = UpdateAudioTrackInfoAsync(track);
        }

        UpdateAudioFileLabel();
        NotifyStatusChanged();
        return (addedCount, duplicateCount);
    }

    private void RemoveAudioTrack(object? parameter)
    {
        if (parameter is not AudioTrackItem track || !AudioTracks.Remove(track))
        {
            return;
        }

        RecalculateAudioDuration();
        UpdateAudioFileLabel();
        NotifyStatusChanged();
        UpdateValidation(true);
        StatusMessage = $"音声を削除しました: {track.FileName}";
    }

    public void MoveAudioTrack(int oldIndex, int newIndex)
    {
        if (IsEncoding || oldIndex < 0 || oldIndex >= AudioTracks.Count ||
            newIndex < 0 || newIndex >= AudioTracks.Count || oldIndex == newIndex)
        {
            return;
        }

        AudioTracks.Move(oldIndex, newIndex);
        NotifyStatusChanged();
        StatusMessage = $"音声トラックを{oldIndex + 1}番目から{newIndex + 1}番目へ移動しました";
    }

    private void UpdateAudioFileLabel()
    {
        AudioFileLabel = AudioTracks.Count switch
        {
            0 => "音楽: 未設定",
            1 => $"音楽: {AudioTracks[0].FileName}",
            _ => $"音楽: {AudioTracks.Count}ファイル"
        };
    }

    private void ClearInputs()
    {
        _imagePath = null;
        AudioTracks.Clear();
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
        NotifyStatusChanged();
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
            if (SelectedProfile != EncodeProfile.DraftPreview)
            {
                errors.Add("画像が未設定です");
            }
        }
        else if (SelectedProfile != EncodeProfile.DraftPreview && !_aspectValid)
        {
            errors.Add("画像の比率が9:16または16:9ではありません");
        }

        if (AudioTracks.Count == 0)
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
        OnPropertyChanged(nameof(ShowReplaceImageHint));
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
            _ => BuildResolutionText(GetEffectiveOrientation() ?? DraftOrientation)
        };
    }

    private string BuildResolutionText(VideoOrientation orientation)
    {
        var options = EncodingOptionsResolver.Resolve(orientation, GetSelectedProfile());
        return $"{options.Width}x{options.Height}";
    }

    private VideoOrientation? GetEffectiveOrientation()
    {
        return SelectedProfile == EncodeProfile.DraftPreview ? VideoOrientation.Horizontal : _orientation;
    }

    private EncodeProfile GetSelectedProfile()
    {
        return SelectedProfile;
    }

    private string GetOrientationText()
    {
        return GetEffectiveOrientation() switch
        {
            VideoOrientation.Vertical => SelectedProfile == EncodeProfile.DraftPreview ? "縦 (仮画像)" : "縦 (9:16)",
            VideoOrientation.Horizontal => SelectedProfile == EncodeProfile.DraftPreview ? "横 (仮画像・固定)" : "横 (16:9)",
            _ => "未確定"
        };
    }

    private static string GetProfileDescription(EncodeProfile profile)
    {
        return profile switch
        {
            EncodeProfile.Standard => "通常出力",
            EncodeProfile.CopyrightCheckProduction => "本番画質・軽量音声 (本番解像度 / AAC 128kbps / 32kHz)",
            EncodeProfile.DraftPreview => "仮動画 (仮画像 / 軽量画質 / 音質切替可)",
            _ => "不明"
        };
    }

    private string GetOutputModeLabel(VideoOrientation orientation)
    {
        var baseLabel = orientation == VideoOrientation.Vertical ? "YouTube Short" : "YouTube";
        return SelectedProfile switch
        {
            EncodeProfile.CopyrightCheckProduction => $"本番画質・軽量音声 ({baseLabel})",
            EncodeProfile.DraftPreview => $"仮動画 ({baseLabel})",
            _ => baseLabel
        };
    }

    private static string GetShortsRuleText(double durationSeconds)
    {
        if (ShortsPolicy.TryGetTrimTargetSeconds(durationSeconds, out var targetSeconds))
        {
            return $"・Shorts制限: {FormatDuration(targetSeconds)}に短縮（末尾1秒フェードアウト + 無音除去）";
        }

        return "・Shorts制限: 短縮なし（0:57未満、または1:00超2:57未満）";
    }

    private async Task UpdateAudioTrackInfoAsync(AudioTrackItem track)
    {
        var ffmpegPath = EncodingService.ResolveFfmpegPath();
        var info = await EncodingService.GetAudioInfoAsync(track.Path, ffmpegPath);
        if (!AudioTracks.Contains(track))
        {
            return;
        }

        track.ApplyAnalysis(info == null ? "情報取得不可" : FormatAudioInfo(info), info?.DurationSeconds);
        RecalculateAudioDuration();
        NotifyStatusChanged();
    }

    private void RecalculateAudioDuration()
    {
        if (AudioTracks.Count == 0 || AudioTracks.Any(track => !track.DurationSeconds.HasValue))
        {
            _audioDurationSeconds = null;
            return;
        }

        _audioDurationSeconds = AudioTracks.Sum(track => track.DurationSeconds!.Value);
    }

    private static string FormatAudioInfo(AudioInfo info)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(info.CodecName))
        {
            parts.Add(info.CodecName.ToUpperInvariant());
        }

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

        var orientation = GetEffectiveOrientation();
        if (orientation == null || AudioTracks.Count == 0)
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

            var imagePath = _imagePath;
            if (SelectedProfile == EncodeProfile.DraftPreview)
            {
                imagePath = PlaceholderImageService.CreateDraftPlaceholder(archiveFolder, title, orientation.Value, DateTime.Now);
            }
            else if (imagePath != null)
            {
                File.Copy(imagePath, Path.Combine(archiveFolder, Path.GetFileName(imagePath)), overwrite: false);
            }

            if (imagePath == null)
            {
                StatusMessage = "画像を準備できませんでした";
                return;
            }

            foreach (var audioPath in AudioTracks.Select(track => track.Path))
            {
                File.Copy(audioPath, Path.Combine(archiveFolder, Path.GetFileName(audioPath)), overwrite: false);
            }

            var outputFileName = OutputNamingService.BuildOutputFileName(title, timestamp, GetSelectedProfile());
            var outputPath = Path.Combine(outputFolder, outputFileName);
            var logPath = Path.Combine(EncodingService.GetLogDirectory(), $"{title}_{timestamp}.log");

            var request = new EncodeRequest(
                ffmpegPath,
                imagePath,
                AudioTracks.Select(track => track.Path).ToArray(),
                outputPath,
                orientation.Value,
                GetSelectedProfile(),
                logPath,
                _draftAudioQuality);

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
