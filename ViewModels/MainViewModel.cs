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

public sealed partial class MainViewModel : INotifyPropertyChanged
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
    private BitmapSource? _draftImagePreview;
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
    private AudioTrackItem? _selectedAudioTrack;
    private bool _isFileDragOver;
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
        OpenLoudnessAnalysisCommand = new RelayCommand(_ => OpenLoudnessAnalysis());
        OpenTextOverlayEditorCommand = new RelayCommand(_ => OpenTextOverlayEditor(), _ => !IsEncoding);
        OpenOutputFolderCommand = new RelayCommand(_ => OpenFolder(OutputDirectoryPath), _ => Directory.Exists(OutputDirectoryPath));
        OpenArchiveFolderCommand = new RelayCommand(_ => OpenFolder(ArchiveDirectoryPath), _ => Directory.Exists(ArchiveDirectoryPath));
        ClearInputsCommand = new RelayCommand(_ => ClearInputs(), _ => CanClearInputs);
        RemoveImageCommand = new RelayCommand(_ => RemoveImage(), _ => !IsEncoding && _imagePath != null);
        RemoveAudioTrackCommand = new RelayCommand(RemoveAudioTrack, track => !IsEncoding && track is AudioTrackItem);
        MoveAudioTrackUpCommand = new RelayCommand(_ => MoveSelectedAudioTrack(-1), _ => CanMoveSelectedAudioTrack(-1));
        MoveAudioTrackDownCommand = new RelayCommand(_ => MoveSelectedAudioTrack(1), _ => CanMoveSelectedAudioTrack(1));
        MoveAudioTrackFirstCommand = new RelayCommand(_ => MoveSelectedAudioTrackTo(0), _ => CanMoveSelectedAudioTrackTo(0));
        MoveAudioTrackLastCommand = new RelayCommand(_ => MoveSelectedAudioTrackTo(AudioTracks.Count - 1), _ => CanMoveSelectedAudioTrackTo(AudioTracks.Count - 1));
        ExportAudioTrackListCommand = new RelayCommand(_ => ExportAudioTrackList(), _ => !IsEncoding && AudioTracks.Count > 0);
        EncodeCommand = new AsyncRelayCommand(EncodeAsync, () => CanEncode);
        InitializeArchiveCommands();

        UpdateSettingsLabels();
        UpdateValidation(true);
    }

    public RelayCommand OpenSettingsCommand { get; }
    public RelayCommand OpenLoudnessAnalysisCommand { get; }
    public RelayCommand OpenTextOverlayEditorCommand { get; }
    public RelayCommand OpenOutputFolderCommand { get; }
    public RelayCommand OpenArchiveFolderCommand { get; }
    public RelayCommand ClearInputsCommand { get; }
    public RelayCommand RemoveImageCommand { get; }
    public RelayCommand RemoveAudioTrackCommand { get; }
    public RelayCommand MoveAudioTrackUpCommand { get; }
    public RelayCommand MoveAudioTrackDownCommand { get; }
    public RelayCommand MoveAudioTrackFirstCommand { get; }
    public RelayCommand MoveAudioTrackLastCommand { get; }
    public RelayCommand ExportAudioTrackListCommand { get; }
    public AsyncRelayCommand EncodeCommand { get; }
    public ObservableCollection<AudioTrackItem> AudioTracks { get; } = [];

    public AudioTrackItem? SelectedAudioTrack
    {
        get => _selectedAudioTrack;
        set
        {
            if (ReferenceEquals(_selectedAudioTrack, value)) return;
            _selectedAudioTrack = value;
            OnPropertyChanged();
            RaiseAudioTrackMoveCanExecuteChanged();
        }
    }

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
            RefreshDraftImagePreview();
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
            OnPropertyChanged(nameof(EffectiveImagePreview));
        }
    }

    public BitmapSource? EffectiveImagePreview
    {
        get
        {
            if (SelectedProfile != EncodeProfile.DraftPreview)
            {
                return ImagePreview;
            }

            _draftImagePreview ??= PlaceholderImageService.CreateDraftPlaceholderBitmap(Title, VideoOrientation.Horizontal);
            return _draftImagePreview;
        }
    }

    public string EffectiveImageBadgeText => SelectedProfile == EncodeProfile.DraftPreview
        ? "仮画像（自動生成）"
        : "エンコード対象";

    public string EffectiveImageCaption => ImageStatusText;

    public bool IsFileDragOver
    {
        get => _isFileDragOver;
        private set
        {
            if (_isFileDragOver == value) return;
            _isFileDragOver = value;
            OnPropertyChanged();
        }
    }

    public string DropActionText => UseDraftMode ? "音声を末尾へ追加" : "画像を置き換え・音声を末尾へ追加";
    public string DropHeaderText => UseDraftMode ? "音声ファイル・音声フォルダをドロップ" : "画像・音声ファイル・音声フォルダをドロップ";
    public string InputSectionHeader => "入力とプレビュー";
    public OutputClassification OutputClassification => OutputClassificationService.Classify(
        _imageWidth, _imageHeight, AudioTracks.Count, _audioDurationSeconds,
        CurrentEncodingSettings.ShortsMaximumSeconds,
        AudioTracks.Any(track => track.IsDurationAnalysisFailed));
    private bool HasBlockingSettingsLoadError => _jobSettings == null && SettingsService.LoadError != null &&
        (!UseDraftMode || ShortsPolicy.AreSettingsValid(CurrentEncodingSettings));
    public string InputStateMessage => (HasBlockingSettingsLoadError ? SettingsService.LoadError : null) ??
        (UseDraftMode ? "仮動画" :
            !ShortsPolicy.AreSettingsValid(CurrentEncodingSettings) ? "Shorts上限・オフセット設定を修正してください" :
            OutputClassification.Message);
    public bool IsInputError => HasBlockingSettingsLoadError ||
        !UseDraftMode && !ShortsPolicy.AreSettingsValid(CurrentEncodingSettings) ||
        !UseDraftMode && OutputClassification.Kind == OutputKind.InputError;
    public bool HasNormalLengthWarning => !UseDraftMode && OutputClassification.HasNormalLengthWarning;
    public bool IsNormalOutput => !UseDraftMode && OutputClassification.Kind == OutputKind.Normal;
    public bool NormalTextOverlayEnabled
    {
        get => CurrentEncodingSettings.NormalTextOverlayEnabled;
        set
        {
            if (CurrentEncodingSettings.NormalTextOverlayEnabled == value) return;
            if (_jobSettings == null && SettingsService.LoadError != null)
            {
                StatusMessage = SettingsService.LoadError;
                OnPropertyChanged();
                return;
            }
            CurrentEncodingSettings.NormalTextOverlayEnabled = value;
            if (_jobSettings == null) SettingsService.Save(CurrentEncodingSettings);
            else if (_restoredProject != null) _restoredProject.Settings.NormalTextOverlayEnabled = value;
            OnPropertyChanged();
            UpdateValidation(true);
        }
    }
    public string TextOverlayLayoutStatusText =>
        TextOverlayService.TryParse(CurrentEncodingSettings.TextOverlayLayoutJson, out _, out _)
            ? "設定済み"
            : "未設定";

    public bool HasNoAudioTracks => AudioTracks.Count == 0;

    public string AudioQueueSummaryText
    {
        get
        {
            if (AudioTracks.Count == 0)
            {
                return "0ファイル";
            }

            return _audioDurationSeconds.HasValue
                ? $"{AudioTracks.Count}ファイル / 合計 {FormatDuration(_audioDurationSeconds.Value)}"
                : $"{AudioTracks.Count}ファイル";
        }
    }

    public bool HasAudioLoudnessWarning => AudioTracks.Any(track =>
        track.IsLoudnessWarning || track.IsTruePeakWarning);

    public string AudioLoudnessGuidanceText => SelectedProfile == EncodeProfile.Standard
        ? "通常動画は出力時に各曲を目標値へ調整します。"
        : "仮動画・Shortsは音量調整なしで出力します。";

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
            if (value)
            {
                IsFileDragOver = false;
            }
            RemoveAudioTrackCommand.RaiseCanExecuteChanged();
            RaiseAudioTrackMoveCanExecuteChanged();
            ExportAudioTrackListCommand.RaiseCanExecuteChanged();
            OpenTextOverlayEditorCommand.RaiseCanExecuteChanged();
            RefreshArchiveCommands();
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

    public bool CanClearInputs => !IsEncoding &&
        (!string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(_imagePath) || IsAudioReady);

    public EncodeProfile SelectedProfile
    {
        get => _selectedProfile == EncodeProfile.DraftPreview ? EncodeProfile.DraftPreview :
            _imageWidth > 0 && _imageHeight > _imageWidth ? EncodeProfile.CopyrightCheckProduction : EncodeProfile.Standard;
        set
        {
            var requested = value == EncodeProfile.DraftPreview ? EncodeProfile.DraftPreview : EncodeProfile.Standard;
            if (_selectedProfile == requested) return;
            _selectedProfile = requested;
            OnPropertyChanged();
            OnPropertyChanged(nameof(UseDraftMode));
            OnPropertyChanged(nameof(IsStandardProfile));
            OnPropertyChanged(nameof(IsCopyrightCheckProductionProfile));
            OnPropertyChanged(nameof(IsDraftProfile));
            OnPropertyChanged(nameof(IsDraftOrientationHorizontal));
            OnPropertyChanged(nameof(IsDraftOrientationVertical));
            OnPropertyChanged(nameof(IsDraftAudioQualityHigh));
            OnPropertyChanged(nameof(IsDraftAudioQualityLow));
            OnPropertyChanged(nameof(EffectiveImagePreview));
            OnPropertyChanged(nameof(EffectiveImageBadgeText));
            OnPropertyChanged(nameof(EffectiveImageCaption));
            OnPropertyChanged(nameof(DropActionText));
            OnPropertyChanged(nameof(DropHeaderText));
            OnPropertyChanged(nameof(InputSectionHeader));
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
        get => IsNormalOutput;
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
            if (_restoredProject != null) _restoredProject.DraftAudioQuality = _draftAudioQuality;
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
            if (_restoredProject != null) _restoredProject.DraftAudioQuality = _draftAudioQuality;
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

    public bool ShowReplaceImageHint => !UseDraftMode && IsImageReady;

    public bool IsImageReady => SelectedProfile == EncodeProfile.DraftPreview ||
        !string.IsNullOrWhiteSpace(_imagePath) && File.Exists(_imagePath);
    public bool IsBackgroundAspectWarning => _imageWidth > 0 && _imageHeight > 0 &&
        !IsRatioClose((double)_imageWidth / _imageHeight,
            _imageHeight > _imageWidth ? VerticalRatio : HorizontalRatio);
    public string ImageStatusText
    {
        get
        {
            if (SelectedProfile == EncodeProfile.DraftPreview)
            {
                return "仮画像を生成 (横 960x540)";
            }

            if (IsNormalOutput)
            {
                if (!IsImageReady) return "未設定（背景画像をドロップ）";
                return IsBackgroundAspectWarning
                    ? $"{Path.GetFileName(_imagePath)} ({_imageWidth}x{_imageHeight}) — 16:9以外・黒帯あり"
                    : $"{Path.GetFileName(_imagePath)} ({_imageWidth}x{_imageHeight})";
            }

            if (!IsImageReady)
            {
                return "未設定";
            }

            var name = Path.GetFileName(_imagePath!);
            if (_imageWidth > 0 && _imageHeight > 0)
            {
                return IsBackgroundAspectWarning
                    ? $"{name} ({_imageWidth}x{_imageHeight}) — 9:16以外・黒帯あり"
                    : $"{name} ({_imageWidth}x{_imageHeight})";
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

    public bool IsOutputReady => UseDraftMode || OutputClassification.CanEncodeInputs;
    public string OutputStatusText
    {
        get
        {
            var orientation = GetEffectiveOrientation();
            if (orientation == null)
            {
                return "未判定";
            }

            var label = GetOutputModeLabel(orientation.Value);
            if (!_audioDurationSeconds.HasValue)
            {
                return $"{label} (時間未取得)";
            }

            var duration = _audioDurationSeconds.Value;
            if (orientation == VideoOrientation.Vertical && duration <= CurrentEncodingSettings.ShortsMaximumSeconds &&
                ShortsPolicy.TryGetTrimTargetSeconds(duration, out var targetSeconds, CurrentEncodingSettings))
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
                $"・ソース: {(UseDraftMode ? "仮画像を自動生成" : IsNormalOutput && NormalTextOverlayEnabled ? "背景画像にトラック名を文字入れ" : "入力画像を使用")}",
                $"・出力解像度: {GetTargetResolutionText()}",
                $"・向き判定: {GetOrientationText()}",
                $"・フレームレート: {options.FrameRate} fps",
                "・ピクセル形式: yuv420p",
                "・アスペクト処理: 比率維持のscale + 黒帯 + setsar=1",
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

            if (IsNormalOutput)
            {
                var settings = CurrentEncodingSettings;
                var overrideCount = AudioTracks.Count(track => track.IsNormalizationOverrideEnabled);
                lines.Insert(lines.IndexOf("出力制御"),
                    $"・ノーマライズ: 既定 {settings.NormalizationTargetIntegratedLufs:0.###} LUFS / " +
                    $"{settings.NormalizationTargetTruePeakDbtp:0.###} dBTP、個別上書き {overrideCount}件（AAC変換用に1 dB余裕）");
            }

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
        if (IsEncoding)
        {
            StatusMessage = "エンコード中は入力を変更できません";
            return;
        }

        if (files == null || files.Length == 0)
        {
            return;
        }

        if (files.Any(path => string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase)))
        {
            if (files.Length != 1)
                StatusMessage = "復元用JSONは、画像や音声と混ぜず1ファイルだけドロップしてください";
            else
                LoadArchiveProject(files[0]);
            return;
        }

        var imageFiles = new List<string>();
        var audioFiles = new List<string>();
        var unsupportedCount = 0;
        var droppedFiles = new List<string>();
        foreach (var path in files)
        {
            if (File.Exists(path))
            {
                droppedFiles.Add(path);
                continue;
            }

            if (Directory.Exists(path))
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                                 .OrderBy(file => file, StringComparer.OrdinalIgnoreCase))
                    {
                        if (AudioExtensions.Contains(Path.GetExtension(file)))
                        {
                            audioFiles.Add(file);
                        }
                        else
                        {
                            unsupportedCount++;
                        }
                    }
                }
                catch (IOException)
                {
                    unsupportedCount++;
                }
                catch (UnauthorizedAccessException)
                {
                    unsupportedCount++;
                }

                continue;
            }

            unsupportedCount++;
        }

        foreach (var file in droppedFiles)
        {
            var ext = Path.GetExtension(file);
            if (ImageExtensions.Contains(ext))
            {
                imageFiles.Add(file);
            }
            else if (AudioExtensions.Contains(ext))
            {
                audioFiles.Add(file);
            }
            else
            {
                unsupportedCount++;
            }
        }

        var imageReplaced = false;
        var imageLoadFailed = false;
        var multipleImagesRejected = false;
        var draftImagesIgnored = 0;
        if (SelectedProfile == EncodeProfile.DraftPreview)
        {
            draftImagesIgnored = imageFiles.Count;
        }
        else if (imageFiles.Count == 1)
        {
            imageReplaced = TrySetImage(imageFiles[0]);
            imageLoadFailed = !imageReplaced;
        }
        else if (imageFiles.Count > 1)
        {
            multipleImagesRejected = true;
        }

        var (addedCount, duplicateCount) = AddAudioFiles(audioFiles);
        UpdateValidation(true);

        var messages = new List<string>();
        if (imageReplaced)
        {
            messages.Add(SelectedProfile == EncodeProfile.Standard
                ? $"背景画像を{Path.GetFileName(imageFiles[0])}に設定しました"
                : $"画像を{Path.GetFileName(imageFiles[0])}に置き換えました");
            if (SelectedProfile == EncodeProfile.Standard && IsBackgroundAspectWarning)
            {
                messages.Add("背景画像は16:9ではないため、余白に黒帯が付きます");
            }
        }
        else if (imageLoadFailed)
        {
            messages.Add($"画像を読み込めなかったため現在の画像を維持しました: {Path.GetFileName(imageFiles[0])}");
        }
        else if (multipleImagesRejected)
        {
            messages.Add(SelectedProfile == EncodeProfile.Standard
                ? "通常モードの背景画像は1枚です。画像は1件ずつドロップしてください"
                : "画像は1件ずつドロップしてください");
        }

        if (draftImagesIgnored > 0)
        {
            messages.Add($"仮動画では画像{draftImagesIgnored}件を使用しません");
        }

        if (addedCount > 0)
        {
            messages.Add($"音声{addedCount}件を末尾へ追加しました（合計{AudioTracks.Count}件）");
        }

        if (duplicateCount > 0)
        {
            messages.Add($"重複音声{duplicateCount}件をスキップしました");
        }

        if (unsupportedCount > 0)
        {
            messages.Add($"未対応ファイル{unsupportedCount}件をスキップしました");
        }

        if (messages.Count > 0)
        {
            StatusMessage = string.Join(" / ", messages);
        }
    }

    public void SetFileDragOver(bool isFileDragOver)
    {
        if (IsEncoding && isFileDragOver)
        {
            return;
        }

        IsFileDragOver = isFileDragOver;
    }

    private bool TrySetImage(string path)
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
            return true;
        }
        catch
        {
            return false;
        }
    }

    private (int AddedCount, int DuplicateCount) AddAudioFiles(IEnumerable<string> paths)
    {
        var existingPaths = new HashSet<string>(AudioTracks.Select(track => track.Path), StringComparer.OrdinalIgnoreCase);
        var addedCount = 0;
        var duplicateCount = 0;
        // Duplicate drops must not discard durations already analyzed.

        foreach (var path in paths.Where(File.Exists))
        {
            if (!existingPaths.Add(path))
            {
                duplicateCount++;
                continue;
            }

            var track = new AudioTrackItem(path, CurrentEncodingSettings.NormalizationTargetIntegratedLufs,
                CurrentEncodingSettings.NormalizationTargetTruePeakDbtp);
            track.PropertyChanged += AudioTrack_PropertyChanged;
            AudioTracks.Add(track);
            RaiseAudioTrackMoveCanExecuteChanged();
            ExportAudioTrackListCommand.RaiseCanExecuteChanged();
            addedCount++;
            _audioDurationSeconds = null;
            _ = UpdateAudioTrackInfoAsync(track);
        }

        UpdateAudioTrackPositions();
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

        track.PropertyChanged -= AudioTrack_PropertyChanged;
        _restoredTracks.Remove(track);
        if (ReferenceEquals(SelectedAudioTrack, track)) SelectedAudioTrack = null;
        RaiseAudioTrackMoveCanExecuteChanged();
        ExportAudioTrackListCommand.RaiseCanExecuteChanged();
        RecalculateAudioDuration();
        UpdateAudioTrackPositions();
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
        RaiseAudioTrackMoveCanExecuteChanged();
        UpdateAudioTrackPositions();
        NotifyStatusChanged();
        UpdateValidation(false);
        StatusMessage = $"音声トラックを{oldIndex + 1}番目から{newIndex + 1}番目へ移動しました";
    }

    private bool CanMoveSelectedAudioTrack(int delta)
    {
        var index = SelectedAudioTrack == null ? -1 : AudioTracks.IndexOf(SelectedAudioTrack);
        return !IsEncoding && index >= 0 && index + delta >= 0 && index + delta < AudioTracks.Count;
    }

    private bool CanMoveSelectedAudioTrackTo(int targetIndex)
    {
        var index = SelectedAudioTrack == null ? -1 : AudioTracks.IndexOf(SelectedAudioTrack);
        return !IsEncoding && index >= 0 && targetIndex >= 0 && targetIndex < AudioTracks.Count && index != targetIndex;
    }

    private void MoveSelectedAudioTrack(int delta)
    {
        if (!CanMoveSelectedAudioTrack(delta)) return;
        MoveAudioTrack(AudioTracks.IndexOf(SelectedAudioTrack!), AudioTracks.IndexOf(SelectedAudioTrack!) + delta);
    }

    private void MoveSelectedAudioTrackTo(int targetIndex)
    {
        if (!CanMoveSelectedAudioTrackTo(targetIndex)) return;
        MoveAudioTrack(AudioTracks.IndexOf(SelectedAudioTrack!), targetIndex);
    }

    private void RaiseAudioTrackMoveCanExecuteChanged()
    {
        MoveAudioTrackUpCommand.RaiseCanExecuteChanged();
        MoveAudioTrackDownCommand.RaiseCanExecuteChanged();
        MoveAudioTrackFirstCommand.RaiseCanExecuteChanged();
        MoveAudioTrackLastCommand.RaiseCanExecuteChanged();
    }

    private void AudioTrack_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isApplyingArchiveSettings) return;
        if (e.PropertyName is nameof(AudioTrackItem.IsNormalizationOverrideEnabled) or
            nameof(AudioTrackItem.NormalizationTargetLufsText) or nameof(AudioTrackItem.NormalizationTargetTruePeakText))
        {
            if (sender is AudioTrackItem track) ConfirmTrackNormalization(track);
            OnPropertyChanged(nameof(EncodingSettingsText));
            UpdateValidation(true);
        }
    }

    private void ExportAudioTrackList()
    {
        var tracksWithoutDuration = AudioTracks.Where(track => track.DurationSeconds is not > 0).ToArray();
        if (tracksWithoutDuration.Length > 0)
        {
            StatusMessage = tracksWithoutDuration.Any(track => track.InfoText == "解析中...")
                ? "再生時刻を取得中です。音声解析が終わってから書き出してください"
                : $"再生時刻を取得できないトラックがあります: {tracksWithoutDuration[0].FileName}";
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "YouTubeチャプター一覧を保存",
            FileName = "YouTubeチャプター.txt",
            DefaultExt = ".txt",
            AddExtension = true,
            Filter = "テキストファイル (*.txt)|*.txt"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var lines = new List<string>(AudioTracks.Count);
            var elapsedSeconds = 0.0;
            for (var index = 0; index < AudioTracks.Count; index++)
            {
                var track = AudioTracks[index];
                lines.Add($"{FormatChapterTimestamp(elapsedSeconds)} {index + 1}.{Path.GetFileNameWithoutExtension(track.FileName)}");
                elapsedSeconds += track.DurationSeconds!.Value;
            }

            File.WriteAllLines(dialog.FileName, lines, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            var chapterWarnings = new List<string>();
            if (AudioTracks.Count < 3)
            {
                chapterWarnings.Add("YouTubeのチャプター表示には3件以上必要です");
            }
            if (AudioTracks.Any(track => track.DurationSeconds is < 10))
            {
                chapterWarnings.Add("10秒未満のトラックがありチャプターとして認識されない場合があります");
            }

            StatusMessage = chapterWarnings.Count > 0
                ? $"チャプター一覧を書き出しました（{string.Join(" / ", chapterWarnings)}）: {dialog.FileName}"
                : $"チャプター一覧を書き出しました: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"チャプター一覧を書き出せませんでした: {ex.Message}";
        }
    }

    private static string FormatChapterTimestamp(double elapsedSeconds)
    {
        var totalSeconds = (long)Math.Floor(elapsedSeconds);
        var hours = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        var seconds = totalSeconds % 60;
        return $"{hours:00}:{minutes:00}:{seconds:00}";
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

    private void UpdateAudioTrackPositions()
    {
        for (var index = 0; index < AudioTracks.Count; index++)
        {
            AudioTracks[index].UpdatePosition(index + 1);
        }
    }

    private void ClearInputs()
    {
        ResetArchiveState();
        var titleChanged = !string.IsNullOrEmpty(_title);
        _title = string.Empty;
        _autoFilledTitle = null;
        _draftImagePreview = null;

        if (titleChanged)
        {
            OnPropertyChanged(nameof(Title));
        }

        _imagePath = null;
        foreach (var track in AudioTracks)
        {
            track.PropertyChanged -= AudioTrack_PropertyChanged;
        }
        AudioTracks.Clear();
        SelectedAudioTrack = null;
        RaiseAudioTrackMoveCanExecuteChanged();
        ExportAudioTrackListCommand.RaiseCanExecuteChanged();
        _audioDurationSeconds = null;
        _imageWidth = 0;
        _imageHeight = 0;
        _orientation = null;
        _aspectValid = false;
        OnPropertyChanged(nameof(EncodingSettingsText));

        ImagePreview = null;
        ImageFileLabel = "画像: 未設定";
        AudioFileLabel = "音楽: 未設定";
        OrientationLabel = "向き: 未判定";
        AspectLabel = "比率: 未判定";

        NotifyStatusChanged();
        UpdateValidation(true);
    }

    private void RemoveImage()
    {
        if (IsEncoding || _imagePath == null) return;
        _imagePath = null;
        _imageWidth = 0;
        _imageHeight = 0;
        ImagePreview = null;
        ImageFileLabel = "画像: 未設定";
        UpdateAspectInfo();
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
        var isVertical = _imageHeight > _imageWidth;
        var isHorizontal = !isVertical;

        _aspectValid = IsRatioClose(ratio, isVertical ? VerticalRatio : HorizontalRatio);
        if (isVertical)
        {
            _orientation = VideoOrientation.Vertical;
            OrientationLabel = "向き: 縦 (Shorts)";
            AspectLabel = _aspectValid ? $"比率: {ratio:F3} (9:16)" : $"比率: {ratio:F3} (黒帯あり)";
        }
        else if (isHorizontal)
        {
            _orientation = VideoOrientation.Horizontal;
            OrientationLabel = "向き: 横 (通常動画)";
            AspectLabel = _aspectValid ? $"比率: {ratio:F3} (16:9)" : $"比率: {ratio:F3} (黒帯あり)";
        }
        NotifyStatusChanged();
    }

    private static bool IsRatioClose(double ratio, double target)
    {
        return Math.Abs(ratio - target) / target <= AspectTolerance;
    }

    private void UpdateSettingsLabels()
    {
        var output = CurrentEncodingSettings.OutputDirectory?.Trim() ?? string.Empty;
        var archive = CurrentEncodingSettings.ArchiveDirectory?.Trim() ?? string.Empty;

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
        var archiveMissing = GetArchiveMissingFields();
        if (archiveMissing.Count > 0)
            errors.Add("今回の設定を確認してください: " + string.Join("、", archiveMissing));
        if (HasRestoredProject)
        {
            if (AudioTracks.Any(track => track.IsDurationAnalysisFailed))
                errors.Add("復元した音声の長さを解析できませんでした。素材を確認してください");
            else if (AudioTracks.Any(track => !track.DurationSeconds.HasValue || !track.IsLoudnessAnalysisComplete))
                errors.Add("復元した音声を解析しています");
        }
        var title = Title?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(title))
        {
            errors.Add("タイトルを入力してください");
        }
        else if (ContainsInvalidTitleChars(title))
        {
            errors.Add("タイトルに使用できない文字が含まれています");
        }

        if (string.IsNullOrWhiteSpace(CurrentEncodingSettings.OutputDirectory))
        {
            errors.Add("出力先ディレクトリが未設定です");
        }

        if (string.IsNullOrWhiteSpace(CurrentEncodingSettings.ArchiveDirectory))
        {
            errors.Add("アーカイブディレクトリが未設定です");
        }

        if (!UseDraftMode)
        {
            if (string.IsNullOrWhiteSpace(_imagePath) || !File.Exists(_imagePath))
            {
                errors.Add("画像が未設定です");
            }
            if (!OutputClassification.CanEncodeInputs)
            {
                errors.Add(OutputClassification.Message);
            }
            if (IsNormalOutput && NormalTextOverlayEnabled &&
                !TextOverlayService.TryParse(CurrentEncodingSettings.TextOverlayLayoutJson, out _, out var overlayError))
            {
                errors.Add(string.IsNullOrWhiteSpace(CurrentEncodingSettings.TextOverlayLayoutJson)
                    ? "文字入れJSONを設定してください"
                    : $"文字入れJSONを確認してください: {overlayError}");
            }
            if (IsNormalOutput && AudioTracks.Any(track => !track.NormalizationTargetsValid))
            {
                errors.Add("音声トラックのノーマライズ目標値を確認してください");
            }
        }

        if (HasBlockingSettingsLoadError || !UseDraftMode && !ShortsPolicy.AreSettingsValid(CurrentEncodingSettings))
            errors.Add(SettingsService.LoadError ?? "Shorts上限・オフセット設定を修正してください");

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
        RefreshArchiveCommands();
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
        OnPropertyChanged(nameof(ArchiveStateMessage));
        RefreshArchiveCommands();
        OnPropertyChanged(nameof(IsImageReady));
        OnPropertyChanged(nameof(IsBackgroundAspectWarning));
        OnPropertyChanged(nameof(ImageStatusText));
        OnPropertyChanged(nameof(ShowReplaceImageHint));
        OnPropertyChanged(nameof(TextOverlayLayoutStatusText));
        OnPropertyChanged(nameof(IsAudioReady));
        OnPropertyChanged(nameof(AudioStatusText));
        OnPropertyChanged(nameof(AudioQueueSummaryText));
        OnPropertyChanged(nameof(HasAudioLoudnessWarning));
        OnPropertyChanged(nameof(AudioLoudnessGuidanceText));
        OnPropertyChanged(nameof(HasNoAudioTracks));
        OnPropertyChanged(nameof(IsOutputReady));
        OnPropertyChanged(nameof(SelectedProfile));
        OnPropertyChanged(nameof(IsCopyrightCheckProductionProfile));
        OnPropertyChanged(nameof(OutputClassification));
        OnPropertyChanged(nameof(InputStateMessage));
        OnPropertyChanged(nameof(IsInputError));
        OnPropertyChanged(nameof(HasNormalLengthWarning));
        OnPropertyChanged(nameof(IsNormalOutput));
        OnPropertyChanged(nameof(IsStandardProfile));
        OnPropertyChanged(nameof(OutputStatusText));
        OnPropertyChanged(nameof(EncodingSettingsText));
        OnPropertyChanged(nameof(CanClearInputs));
        OnPropertyChanged(nameof(EffectiveImagePreview));
        OnPropertyChanged(nameof(EffectiveImageBadgeText));
        OnPropertyChanged(nameof(EffectiveImageCaption));
    }

    private bool TryParseTrackNormalizationTargets(AudioTrackItem track, out double targetLufs, out double targetTruePeak)
    {
        targetLufs = 0;
        targetTruePeak = 0;
        var lufsText = track.IsNormalizationOverrideEnabled ? track.NormalizationTargetLufsText :
            CurrentEncodingSettings.NormalizationTargetIntegratedLufs.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        var peakText = track.IsNormalizationOverrideEnabled ? track.NormalizationTargetTruePeakText :
            CurrentEncodingSettings.NormalizationTargetTruePeakDbtp.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        if (!double.TryParse(lufsText, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out targetLufs) ||
            !double.TryParse(peakText, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out targetTruePeak))
        {
            return false;
        }

        return double.IsFinite(targetLufs) && targetLufs >= EncodingService.MinimumTargetIntegratedLufs &&
               targetLufs <= EncodingService.MaximumTargetIntegratedLufs && double.IsFinite(targetTruePeak) &&
               targetTruePeak >= EncodingService.MinimumTargetTruePeakDbtp &&
               targetTruePeak <= EncodingService.MaximumTargetTruePeakDbtp;
    }

    private void RefreshDraftImagePreview()
    {
        if (_draftImagePreview == null && SelectedProfile != EncodeProfile.DraftPreview)
        {
            return;
        }

        _draftImagePreview = PlaceholderImageService.CreateDraftPlaceholderBitmap(Title, VideoOrientation.Horizontal);
        OnPropertyChanged(nameof(EffectiveImagePreview));
        OnPropertyChanged(nameof(EffectiveImageCaption));
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
        return UseDraftMode ? VideoOrientation.Horizontal : _orientation;
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
        return GetSelectedProfile() switch
        {
            EncodeProfile.CopyrightCheckProduction => $"本番画質・軽量音声 ({baseLabel})",
            EncodeProfile.DraftPreview => $"仮動画 ({baseLabel})",
            _ => baseLabel
        };
    }

    private string GetShortsRuleText(double durationSeconds)
    {
        if (ShortsPolicy.TryGetTrimTargetSeconds(durationSeconds, out var targetSeconds, CurrentEncodingSettings))
        {
            return $"・Shorts制限: {FormatDuration(targetSeconds)}に短縮（末尾1秒フェードアウト + 無音除去）";
        }

        return "・Shorts制限: 短縮なし（0:57未満、または1:00超2:57未満）";
    }

    private async Task UpdateAudioTrackInfoAsync(AudioTrackItem track)
    {
        var ffmpegPath = EncodingService.ResolveFfmpegPath();
        AudioInfo? info = null;
        AudioLoudnessResult? loudness = null;
        try
        {
            var infoTask = EncodingService.GetAudioInfoAsync(track.Path, ffmpegPath);
            var loudnessTask = EncodingService.GetAudioLoudnessAsync(track.Path, ffmpegPath);
            await Task.WhenAll(infoTask, loudnessTask);
            info = infoTask.Result;
            loudness = loudnessTask.Result;
        }
        catch
        {
            // Duration analysis failures are surfaced by the input classifier.
        }

        if (!AudioTracks.Contains(track))
        {
            return;
        }

        var infoText = info switch
        {
            null => "情報取得不可",
            { DurationSeconds: not null } => $"{FormatAudioInfo(info)} / {FormatDuration(info.DurationSeconds.Value)}",
            _ => FormatAudioInfo(info)
        };
        track.ApplyAnalysis(infoText, info?.DurationSeconds);
        track.ApplyLoudnessAnalysis(loudness?.IntegratedLufs, loudness?.TruePeakDbtp,
            loudness?.LoudnessRangeLu);
        RecalculateAudioDuration();
        NotifyStatusChanged();
        UpdateValidation(false);
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

    private static string FormatAudioLoudness(AudioLoudnessResult? loudness)
    {
        if (loudness == null)
        {
            return "解析不可";
        }

        var value = double.IsNegativeInfinity(loudness.IntegratedLufs)
            ? "-∞"
            : loudness.IntegratedLufs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

        return $"{value} LUFS";
    }

    private static string FormatAudioTruePeak(AudioLoudnessResult? loudness)
    {
        if (loudness?.TruePeakDbtp is not double truePeakDbtp)
        {
            return "解析不可";
        }

        var value = double.IsNegativeInfinity(truePeakDbtp)
            ? "-∞"
            : truePeakDbtp.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

        return $"{value} dBTP";
    }

    private static string FormatAudioLoudnessRange(AudioLoudnessResult? loudness)
    {
        return loudness?.LoudnessRangeLu is double loudnessRangeLu
            ? loudnessRangeLu.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " LU"
            : "—";
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
        var window = new SettingsWindow(environmentOnly: HasRestoredProject)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        var result = window.ShowDialog();
        if (result == true)
        {
            _isApplyingArchiveSettings = true;
            try
            {
                foreach (var track in AudioTracks)
                {
                    track.UpdateDefaultNormalizationTargets(CurrentEncodingSettings.NormalizationTargetIntegratedLufs,
                        CurrentEncodingSettings.NormalizationTargetTruePeakDbtp);
                }
            }
            finally { _isApplyingArchiveSettings = false; }
            OnPropertyChanged(nameof(EncodingSettingsText));
            UpdateSettingsLabels();
            UpdateValidation(true);
        }
    }

    private void OpenLoudnessAnalysis()
    {
        var window = new LoudnessAnalysisWindow
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        window.ShowDialog();
    }

    private void OpenTextOverlayEditor()
    {
        if (_jobSettings == null && SettingsService.LoadError != null)
        {
            StatusMessage = SettingsService.LoadError;
            return;
        }
        var window = new TextOverlayJsonWindow(CurrentEncodingSettings.TextOverlayLayoutJson)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (window.ShowDialog() != true)
        {
            return;
        }

        var current = CurrentEncodingSettings;
        var updated = new AppSettings
        {
            OutputDirectory = current.OutputDirectory,
            ArchiveDirectory = current.ArchiveDirectory,
            OneMinuteShortsOffsetSeconds = current.OneMinuteShortsOffsetSeconds,
            ThreeMinuteShortsOffsetSeconds = current.ThreeMinuteShortsOffsetSeconds,
            ShortsMaximumSeconds = current.ShortsMaximumSeconds,
            NormalTextOverlayEnabled = current.NormalTextOverlayEnabled,
            NormalizationTargetIntegratedLufs = current.NormalizationTargetIntegratedLufs,
            NormalizationTargetTruePeakDbtp = current.NormalizationTargetTruePeakDbtp,
            TextOverlayLayoutJson = window.JsonText
        };
        if (_jobSettings == null) SettingsService.Save(updated);
        else
        {
            _jobSettings = updated;
            if (_restoredProject != null) _restoredProject.Settings.TextOverlayLayoutJson = window.JsonText;
        }

        OnPropertyChanged(nameof(TextOverlayLayoutStatusText));
        UpdateValidation(true);
        StatusMessage = "曲名文字入れのJSON設定を保存しました";
    }

    private async Task EncodeAsync()
    {
        UpdateValidation(true);
        if (!CanEncode)
        {
            return;
        }

        var orientation = GetEffectiveOrientation();
        var profile = GetSelectedProfile();
        var audioSnapshot = AudioTracks.ToArray();
        var originalAudioPaths = audioSnapshot.Select(track => track.Path).ToArray();
        var originalImagePath = _imagePath;
        var settings = CurrentEncodingSettings;
        var overlayJson = settings.TextOverlayLayoutJson;
        var overlayEnabled = profile == EncodeProfile.Standard && settings.NormalTextOverlayEnabled;
        var maxSeconds = settings.ShortsMaximumSeconds;
        var oneMinuteOffset = settings.OneMinuteShortsOffsetSeconds;
        var threeMinuteOffset = settings.ThreeMinuteShortsOffsetSeconds;
        var draftAudioQuality = _draftAudioQuality;
        var archiveSnapshot = CaptureArchiveProject();
        if (orientation == null || audioSnapshot.Length == 0)
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

        string? generatedImageDirectory = null;
        IReadOnlyList<string>? trackImagePaths = null;
        try
        {
            var normalizationTargets = profile == EncodeProfile.Standard
                ? audioSnapshot.Select(track =>
                {
                    if (!TryParseTrackNormalizationTargets(track, out var lufs, out var peak))
                        throw new InvalidOperationException($"{track.FileName}: ノーマライズ目標値を確認してください");
                    return new LoudnessNormalizationTarget(lufs, peak);
                }).ToArray()
                : null;
            IsEncoding = true;
            UpdateValidation(false);
            StatusMessage = "エンコード中...";

            var title = Title.Trim();
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            var outputRoot = settings.OutputDirectory.Trim();
            var archiveRoot = settings.ArchiveDirectory.Trim();

            var archiveFolderName = ResolveArchiveFolderName(title, archiveRoot);

            var outputFolder = outputRoot;
            var archiveFolder = Path.Combine(archiveRoot, archiveFolderName);

            Directory.CreateDirectory(outputFolder);
            Directory.CreateDirectory(archiveFolder);

            var imagePath = originalImagePath;
            if (profile == EncodeProfile.DraftPreview)
            {
                imagePath = PlaceholderImageService.CreateDraftPlaceholder(archiveFolder, title, orientation.Value, DateTime.Now);
            }
            else if (originalImagePath != null)
            {
                if (!File.Exists(originalImagePath))
                {
                    throw new InvalidOperationException("背景画像を確認してください。");
                }
                imagePath = Path.Combine(archiveFolder, "image-" + Path.GetFileName(originalImagePath));
                File.Copy(originalImagePath, imagePath, overwrite: false);
            }

            var sourceImagePath = overlayEnabled ? imagePath : null;
            var archivedImagePath = imagePath;

            if (overlayEnabled)
            {
                if (!TextOverlayService.TryParse(overlayJson,
                        out var overlayConfiguration, out var overlayError) || overlayConfiguration == null)
                {
                    throw new InvalidOperationException(overlayError ?? "背景画像または文字入れJSONを確認してください。");
                }
                File.WriteAllText(Path.Combine(archiveFolder, "text-overlay-layout.json"), overlayJson);

                generatedImageDirectory = Path.Combine(Path.GetTempPath(), "MovieMaker", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(generatedImageDirectory);
                var configuration = overlayConfiguration;
                var backgroundImagePath = imagePath!;
                var generatedDirectoryPath = generatedImageDirectory;
                var trackTitles = audioSnapshot.Select(track => Path.GetFileNameWithoutExtension(track.FileName) ?? string.Empty).ToArray();
                trackImagePaths = await TextOverlayService.CreateTrackImagesAsync(backgroundImagePath,
                    generatedDirectoryPath, trackTitles, configuration);
                imagePath = trackImagePaths[0];
            }

            if (imagePath == null)
            {
                StatusMessage = "画像を準備できませんでした";
                return;
            }

            var archivedAudioPaths = new List<string>(originalAudioPaths.Length);
            for (var index = 0; index < originalAudioPaths.Length; index++)
            {
                var archived = Path.Combine(archiveFolder, $"audio-{index:D2}-" + audioSnapshot[index].FileName);
                File.Copy(originalAudioPaths[index], archived, overwrite: false);
                archivedAudioPaths.Add(archived);
            }

            archiveSnapshot.ImagePath = profile == EncodeProfile.DraftPreview ? null : Path.GetFileName(archivedImagePath);
            for (var index = 0; index < archivedAudioPaths.Count; index++)
                archiveSnapshot.Tracks[index].AudioPath = Path.GetFileName(archivedAudioPaths[index]);
            ArchiveProjectService.Save(archiveFolder, archiveSnapshot);

            var outputFileName = OutputNamingService.BuildOutputFileName(title, timestamp, profile);
            var outputPath = Path.Combine(outputFolder, outputFileName);
            var logPath = Path.Combine(EncodingService.GetLogDirectory(), $"{title}_{timestamp}.log");
            var request = new EncodeRequest(
                ffmpegPath,
                imagePath,
                archivedAudioPaths,
                outputPath,
                orientation.Value,
                profile,
                logPath,
                draftAudioQuality,
                trackImagePaths,
                normalizationTargets,
                maxSeconds,
                overlayEnabled,
                oneMinuteOffset,
                threeMinuteOffset,
                SourceImagePath: sourceImagePath);

            var result = await EncodingService.EncodeAsync(request);

            if (result.Success)
            {
                var outputLoudness = await EncodingService.GetAudioLoudnessAsync(result.OutputPath, ffmpegPath);
                var loudnessText = outputLoudness is null
                    ? "解析不可"
                    : $"{FormatAudioLoudness(outputLoudness)} / " +
                      $"TP {FormatAudioTruePeak(outputLoudness)} / " +
                      $"LRA {FormatAudioLoudnessRange(outputLoudness)}";
                StatusMessage = string.IsNullOrWhiteSpace(result.Encoder)
                    ? $"完了: {result.OutputPath} / 出力音声: {loudnessText}"
                    : $"完了: {result.OutputPath} (Encoder: {result.Encoder}) / 出力音声: {loudnessText}";
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
            if (!string.IsNullOrWhiteSpace(generatedImageDirectory))
            {
                try
                {
                    Directory.Delete(generatedImageDirectory, recursive: true);
                }
                catch
                {
                    // The encoded video is complete; temporary image cleanup is best effort.
                }
            }
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
