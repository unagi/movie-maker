using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;
using MovieMaker.Infrastructure;
using MovieMaker.Models;
using MovieMaker.Services;
using MovieMaker.Views;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace MovieMaker.ViewModels;

public sealed partial class MainViewModel
{
    private AppSettings? _jobSettings;
    private ArchiveProject? _restoredProject;
    private string? _loadedArchiveDirectory;
    private bool _isApplyingArchiveSettings;
    private readonly Dictionary<AudioTrackItem, ArchiveTrack> _restoredTracks = [];

    public RelayCommand LoadArchiveProjectCommand { get; private set; } = null!;
    public RelayCommand SaveArchiveProjectCommand { get; private set; } = null!;
    public RelayCommand ReviewArchiveSettingsCommand { get; private set; } = null!;
    public bool HasRestoredProject => _restoredProject != null;
    public double CurrentNormalizationTargetLufs => CurrentEncodingSettings.NormalizationTargetIntegratedLufs;
    public double CurrentNormalizationTargetTruePeak => CurrentEncodingSettings.NormalizationTargetTruePeakDbtp;

    private AppSettings CurrentEncodingSettings
    {
        get
        {
            if (_jobSettings == null) return SettingsService.Current;
            // Folder preferences belong to this computer, not to the restored job.
            _jobSettings.OutputDirectory = SettingsService.Current.OutputDirectory;
            _jobSettings.ArchiveDirectory = SettingsService.Current.ArchiveDirectory;
            return _jobSettings;
        }
    }

    public string ArchiveStateMessage
    {
        get
        {
            if (!HasRestoredProject) return string.Empty;
            var missing = GetArchiveMissingFields();
            return missing.Count == 0
                ? "復元した今回の設定を使用しています。既定設定は変更しません。"
                : "今回の設定を確認してください: " + string.Join("、", missing);
        }
    }

    private void InitializeArchiveCommands()
    {
        LoadArchiveProjectCommand = new RelayCommand(_ => SelectArchiveProject(), _ => !IsEncoding);
        SaveArchiveProjectCommand = new RelayCommand(_ => SaveArchiveProject(), _ => !IsEncoding && AudioTracks.Count > 0);
        ReviewArchiveSettingsCommand = new RelayCommand(_ => ReviewArchiveSettings(), _ => !IsEncoding && HasRestoredProject);
    }

    private void RefreshArchiveCommands()
    {
        LoadArchiveProjectCommand?.RaiseCanExecuteChanged();
        SaveArchiveProjectCommand?.RaiseCanExecuteChanged();
        ReviewArchiveSettingsCommand?.RaiseCanExecuteChanged();
    }

    private void ResetArchiveState()
    {
        _jobSettings = null;
        _restoredProject = null;
        _loadedArchiveDirectory = null;
        _restoredTracks.Clear();
        _skipLoudnessNormalization = false;
        OnPropertyChanged(nameof(HasRestoredProject));
        NotifyLoudnessPolicyChanged();
        OnPropertyChanged(nameof(NormalTextOverlayEnabled));
        OnPropertyChanged(nameof(TextOverlayLayoutStatusText));
        UpdateSettingsLabels();
    }

    private void SelectArchiveProject()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "復元用設定を読み込む",
            Filter = "復元用設定 (movie-maker-project.json)|movie-maker-project.json|JSON (*.json)|*.json"
        };
        if (dialog.ShowDialog() == true) LoadArchiveProject(dialog.FileName);
    }

    public void LoadArchiveProject(string path)
    {
        if (IsEncoding)
        {
            StatusMessage = "エンコード中は復元できません";
            return;
        }
        try
        {
            var loaded = ArchiveProjectService.Load(path);
            var project = loaded.Project;
            var image = loaded.ImageFullPath == null ? null : ReadArchiveImage(loaded.ImageFullPath);
            var orientation = image == null ? (VideoOrientation?)null :
                image.PixelHeight > image.PixelWidth ? VideoOrientation.Vertical : VideoOrientation.Horizontal;
            if (project.UseDraftMode == false && project.Orientation != null && project.Orientation != orientation)
                throw new InvalidDataException("保存された画像の向きと現在の素材が一致しません。");
            if (CanClearInputs && !ConfirmArchiveAction("現在の入力と今回の設定を、選択したアーカイブに置き換えますか？", "設定を復元"))
                return;

            var hadMissingSettings = ArchiveProjectService.GetMissingFields(project).Count > 0;
            if (hadMissingSettings)
            {
                var window = new ArchiveProjectWindow(project, SettingsService.Current, orientation,
                    Path.GetFileName(loaded.DirectoryPath)) { Owner = Application.Current.MainWindow };
                if (window.ShowDialog() != true) return;
                project = window.Project;
            }

            var currentPreset = EncodingOptionsResolver.Resolve(project.Orientation!.Value,
                project.Profile!.Value, project.DraftAudioQuality ?? DraftAudioQuality.High);
            var policyChanges = ArchiveProjectService.GetLegacyPolicyChanges(project);
            if (project.Preset != null && project.Preset != currentPreset ||
                project.AppVersion != null && project.AppVersion != CurrentAppVersion || policyChanges.Count > 0)
            {
                var oldQuality = project.Preset == null ? "不明" :
                    $"{project.Preset.Width}x{project.Preset.Height} / {project.Preset.FrameRate}fps / " +
                    $"AAC {project.Preset.AudioBitrate} / {project.Preset.AudioSampleRate}Hz";
                var newQuality = $"{currentPreset.Width}x{currentPreset.Height} / {currentPreset.FrameRate}fps / " +
                    $"AAC {currentPreset.AudioBitrate} / {currentPreset.AudioSampleRate}Hz";
                var policyText = policyChanges.Count == 0 ? string.Empty :
                    "\n\n処理ポリシーの変更:\n・" + string.Join("\n・", policyChanges);
                if (!ConfirmArchiveAction($"作成時と現在のアプリ版・品質設定または音量処理が異なります。\n" +
                        $"保存時: {project.AppVersion ?? "不明"} / {oldQuality}\n現在: {CurrentAppVersion} / {newQuality}\n" +
                        policyText + "\n\n現在の処理で再エンコードする準備を進めますか？", "作成時との差を確認")) return;
            }
            // Prepare all objects before replacing the current inputs.
            var settings = MaterializeJobSettings(project.Settings);
            var tracks = project.Tracks.Select((saved, index) => CreateRestoredTrack(
                loaded.AudioFullPaths[index], saved, settings)).ToArray();
            ClearInputs();
            _restoredProject = ArchiveProjectService.Clone(project);
            _loadedArchiveDirectory = loaded.DirectoryPath;
            _jobSettings = settings;
            _selectedProfile = project.UseDraftMode == true ? EncodeProfile.DraftPreview : EncodeProfile.Standard;
            _draftAudioQuality = project.DraftAudioQuality ?? DraftAudioQuality.High;
            _skipLoudnessNormalization = project.Settings.SkipLoudnessNormalization;
            _imagePath = project.UseDraftMode == true ? null : loaded.ImageFullPath;
            ImagePreview = project.UseDraftMode == true ? null : image;
            _imageWidth = ImagePreview?.PixelWidth ?? 0;
            _imageHeight = ImagePreview?.PixelHeight ?? 0;
            ImageFileLabel = _imagePath == null ? "画像: 自動生成" : $"画像: {Path.GetFileName(_imagePath)}";
            _title = project.Title!;
            _autoFilledTitle = null;
            for (var index = 0; index < tracks.Length; index++)
            {
                var track = tracks[index];
                _restoredTracks[track] = _restoredProject.Tracks[index];
                track.PropertyChanged += AudioTrack_PropertyChanged;
                AudioTracks.Add(track);
            }
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(HasRestoredProject));
            OnPropertyChanged(nameof(UseDraftMode));
            OnPropertyChanged(nameof(IsDraftProfile));
            OnPropertyChanged(nameof(IsDraftAudioQualityHigh));
            OnPropertyChanged(nameof(IsDraftAudioQualityLow));
            OnPropertyChanged(nameof(NormalTextOverlayEnabled));
            NotifyLoudnessPolicyChanged();
            UpdateAudioTrackPositions();
            UpdateAudioFileLabel();
            UpdateAspectInfo();
            NotifyStatusChanged();
            UpdateValidation(false);
            StatusMessage = hadMissingSettings
                ? "材料と曲順を読み込み、今回の設定を確認しました。音声を解析しています。復元用設定を保存できます。"
                : "音声トラック順と今回の設定を復元しました。音声を解析しています。";
            foreach (var track in tracks) _ = UpdateAudioTrackInfoAsync(track);
        }
        catch (Exception ex)
        {
            StatusMessage = $"復元用設定を読み込めませんでした: {ex.Message}";
        }
    }

    private static BitmapImage ReadArchiveImage(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static string CurrentAppVersion => typeof(MainViewModel).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
        typeof(MainViewModel).Assembly.GetName().Version?.ToString() ?? "不明";

    private static bool ConfirmArchiveAction(string message, string title) =>
        MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;

    private static AppSettings MaterializeJobSettings(ArchiveProcessingSettings saved)
    {
        var defaults = SettingsService.Current;
        return new AppSettings
        {
            OutputDirectory = defaults.OutputDirectory,
            ArchiveDirectory = defaults.ArchiveDirectory,
            ShortsMaximumSeconds = saved.ShortsMaximumSeconds ?? defaults.ShortsMaximumSeconds,
            OneMinuteShortsOffsetSeconds = saved.OneMinuteShortsOffsetSeconds ?? defaults.OneMinuteShortsOffsetSeconds,
            ThreeMinuteShortsOffsetSeconds = saved.ThreeMinuteShortsOffsetSeconds ?? defaults.ThreeMinuteShortsOffsetSeconds,
            NormalizationTargetIntegratedLufs = saved.NormalizationTargetIntegratedLufs ?? defaults.NormalizationTargetIntegratedLufs,
            NormalizationTargetTruePeakDbtp = saved.NormalizationTargetTruePeakDbtp ?? defaults.NormalizationTargetTruePeakDbtp,
            NormalTextOverlayEnabled = saved.NormalTextOverlayEnabled ?? defaults.NormalTextOverlayEnabled,
            TextOverlayLayoutJson = saved.TextOverlayLayoutJson ?? defaults.TextOverlayLayoutJson
        };
    }

    private void NotifyLoudnessPolicyChanged()
    {
        OnPropertyChanged(nameof(SkipLoudnessNormalization));
        OnPropertyChanged(nameof(IsTrackNormalizationApplied));
        OnPropertyChanged(nameof(CanOverrideLoudnessNormalization));
        OnPropertyChanged(nameof(AudioLoudnessGuidanceText));
    }

    private static AudioTrackItem CreateRestoredTrack(string path, ArchiveTrack saved, AppSettings settings)
    {
        var track = new AudioTrackItem(path, settings.NormalizationTargetIntegratedLufs,
            settings.NormalizationTargetTruePeakDbtp, saved.OriginalFileName);
        if (saved.TargetIntegratedLufs is double lufs)
            track.NormalizationTargetLufsText = lufs.ToString("R", CultureInfo.InvariantCulture);
        if (saved.TargetTruePeakDbtp is double peak)
            track.NormalizationTargetTruePeakText = peak.ToString("R", CultureInfo.InvariantCulture);
        track.IsNormalizationOverrideEnabled = saved.IsNormalizationOverrideEnabled ?? false;
        return track;
    }

    public void ConfirmTrackNormalization(AudioTrackItem track)
    {
        if (_restoredTracks.TryGetValue(track, out var saved))
        {
            saved.IsNormalizationOverrideEnabled = track.IsNormalizationOverrideEnabled;
            saved.TargetIntegratedLufs = ParseArchiveTarget(track.NormalizationTargetLufsText);
            saved.TargetTruePeakDbtp = ParseArchiveTarget(track.NormalizationTargetTruePeakText);
        }
        UpdateValidation(false);
        OnPropertyChanged(nameof(ArchiveStateMessage));
    }

    private static double? ParseArchiveTarget(string value) =>
        double.TryParse(value, System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
        double.IsFinite(number) ? number : null;

    private ArchiveProject CaptureArchiveProject()
    {
        var settings = CurrentEncodingSettings;
        var saved = _restoredProject?.Settings;
        return new ArchiveProject
        {
            Origin = "app",
            Title = Title.Trim(),
            UseDraftMode = UseDraftMode,
            Profile = SelectedProfile,
            Orientation = GetEffectiveOrientation(),
            DraftAudioQuality = _restoredProject == null || _restoredProject.DraftAudioQuality != null ? _draftAudioQuality : null,
            ImagePath = _imagePath == null ? null : Path.GetFileName(_imagePath),
            Tracks = AudioTracks.Select(track =>
            {
                _restoredTracks.TryGetValue(track, out var original);
                return new ArchiveTrack
                {
                    AudioPath = Path.GetFileName(track.Path),
                    OriginalFileName = track.FileName,
                    IsNormalizationOverrideEnabled = original == null || original.IsNormalizationOverrideEnabled != null
                        ? track.IsNormalizationOverrideEnabled : null,
                    TargetIntegratedLufs = original == null || original.TargetIntegratedLufs != null
                        ? ParseArchiveTarget(track.NormalizationTargetLufsText) : null,
                    TargetTruePeakDbtp = original == null || original.TargetTruePeakDbtp != null
                        ? ParseArchiveTarget(track.NormalizationTargetTruePeakText) : null
                };
            }).ToList(),
            Settings = new ArchiveProcessingSettings
            {
                ShortsMaximumSeconds = saved == null || saved.ShortsMaximumSeconds != null ? settings.ShortsMaximumSeconds : null,
                OneMinuteShortsOffsetSeconds = saved == null || saved.OneMinuteShortsOffsetSeconds != null ? settings.OneMinuteShortsOffsetSeconds : null,
                ThreeMinuteShortsOffsetSeconds = saved == null || saved.ThreeMinuteShortsOffsetSeconds != null ? settings.ThreeMinuteShortsOffsetSeconds : null,
                NormalizationTargetIntegratedLufs = saved == null || saved.NormalizationTargetIntegratedLufs != null ? settings.NormalizationTargetIntegratedLufs : null,
                NormalizationTargetTruePeakDbtp = saved == null || saved.NormalizationTargetTruePeakDbtp != null ? settings.NormalizationTargetTruePeakDbtp : null,
                NormalTextOverlayEnabled = saved == null || saved.NormalTextOverlayEnabled != null ? settings.NormalTextOverlayEnabled : null,
                TextOverlayLayoutJson = saved == null || saved.TextOverlayLayoutJson != null ? settings.TextOverlayLayoutJson : null,
                SkipLoudnessNormalization = _skipLoudnessNormalization
            },
            AppVersion = CurrentAppVersion,
            Preset = EncodingOptionsResolver.Resolve(GetEffectiveOrientation() ?? VideoOrientation.Horizontal,
                SelectedProfile, _draftAudioQuality)
        };
    }

    private IReadOnlyList<string> GetArchiveMissingFields() => HasRestoredProject
        ? ArchiveProjectService.GetMissingFields(CaptureArchiveProject()) : Array.Empty<string>();

    private void ReviewArchiveSettings()
    {
        if (IsEncoding || !HasRestoredProject) return;
        try
        {
            var current = CaptureArchiveProject();
            var window = new ArchiveProjectWindow(current, CurrentEncodingSettings, _orientation, Title)
                { Owner = Application.Current.MainWindow };
            if (window.ShowDialog() != true) return;
            _restoredProject = window.Project;
            _jobSettings = MaterializeJobSettings(_restoredProject.Settings);
            _selectedProfile = _restoredProject.UseDraftMode == true ? EncodeProfile.DraftPreview : EncodeProfile.Standard;
            _draftAudioQuality = _restoredProject.DraftAudioQuality ?? _draftAudioQuality;
            _skipLoudnessNormalization = _restoredProject.Settings.SkipLoudnessNormalization;
            _title = _restoredProject.Title!;
            _isApplyingArchiveSettings = true;
            try
            {
                for (var index = 0; index < AudioTracks.Count; index++)
                {
                    var track = AudioTracks[index];
                    var target = _restoredProject.Tracks[index];
                    _restoredTracks[track] = target;
                    track.IsNormalizationOverrideEnabled = target.IsNormalizationOverrideEnabled ?? false;
                    track.UpdateDefaultNormalizationTargets(CurrentNormalizationTargetLufs, CurrentNormalizationTargetTruePeak);
                }
            }
            finally { _isApplyingArchiveSettings = false; }
            _draftImagePreview = null;
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(UseDraftMode));
            OnPropertyChanged(nameof(IsDraftAudioQualityHigh));
            OnPropertyChanged(nameof(IsDraftAudioQualityLow));
            OnPropertyChanged(nameof(NormalTextOverlayEnabled));
            NotifyLoudnessPolicyChanged();
            NotifyStatusChanged();
            UpdateValidation(true);
        }
        catch (Exception ex) { StatusMessage = $"今回の設定を変更できませんでした: {ex.Message}"; }
    }

    private void SaveArchiveProject()
    {
        if (IsEncoding) return;
        try
        {
            var snapshot = CaptureArchiveProject();
            if (ContainsInvalidTitleChars(snapshot.Title ?? string.Empty))
                throw new InvalidDataException("タイトルに使用できない文字が含まれています。");
            var missing = ArchiveProjectService.GetMissingFields(snapshot);
            if (missing.Count > 0) throw new InvalidDataException("今回の設定を確認してください: " + string.Join("、", missing));
            var reuse = _loadedArchiveDirectory != null && AudioTracks.All(track => IsInsideArchive(track.Path, _loadedArchiveDirectory)) &&
                (UseDraftMode || _imagePath != null && IsInsideArchive(_imagePath, _loadedArchiveDirectory));
            string directory;
            if (reuse)
            {
                directory = _loadedArchiveDirectory!;
                snapshot.ImagePath = _imagePath == null ? null : Path.GetRelativePath(directory, _imagePath);
                for (var index = 0; index < AudioTracks.Count; index++)
                    snapshot.Tracks[index].AudioPath = Path.GetRelativePath(directory, AudioTracks[index].Path);
                if (File.Exists(Path.Combine(directory, ArchiveProjectService.FileName)) &&
                    !ConfirmArchiveAction("このアーカイブの復元用設定を、今回の設定で上書きしますか？", "復元用設定を保存")) return;
                ArchiveProjectService.Save(directory, snapshot, overwrite: true);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(SettingsService.Current.ArchiveDirectory))
                    throw new InvalidDataException("設定でアーカイブ先を指定してください。");
                var archiveRoot = SettingsService.Current.ArchiveDirectory.Trim();
                directory = Path.Combine(archiveRoot, ResolveArchiveFolderName(snapshot.Title!, archiveRoot));
                CopyArchiveMaterials(directory, snapshot);
                ArchiveProjectService.Save(directory, snapshot);
            }
            StatusMessage = $"材料と今回の設定を保存しました: {Path.Combine(directory, ArchiveProjectService.FileName)}";
        }
        catch (Exception ex) { StatusMessage = $"復元用設定を保存できませんでした: {ex.Message}"; }
    }

    private static bool IsInsideArchive(string path, string directory) => Path.GetFullPath(path)
        .StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    private void CopyArchiveMaterials(string directory, ArchiveProject project)
    {
        Directory.CreateDirectory(directory);
        if (UseDraftMode)
        {
            PlaceholderImageService.CreateDraftPlaceholder(directory, project.Title!, VideoOrientation.Horizontal, DateTime.Now);
            project.ImagePath = null;
        }
        else
        {
            var imageName = "image-" + Path.GetFileName(_imagePath!);
            File.Copy(_imagePath!, Path.Combine(directory, imageName), overwrite: false);
            project.ImagePath = imageName;
        }
        for (var index = 0; index < AudioTracks.Count; index++)
        {
            var track = AudioTracks[index];
            var name = $"audio-{index:D2}-" + track.FileName;
            File.Copy(track.Path, Path.Combine(directory, name), overwrite: false);
            project.Tracks[index].AudioPath = name;
        }
        if (project.Profile == EncodeProfile.Standard && project.Settings.NormalTextOverlayEnabled == true)
            File.WriteAllText(Path.Combine(directory, "text-overlay-layout.json"), project.Settings.TextOverlayLayoutJson!);
    }
}
