using MovieMaker.Models;
using MovieMaker.Services;
using MovieMaker.ViewModels;
using System.ComponentModel;
using System.Reflection;
using Xunit;

namespace MovieMaker.Tests;

[Collection("ProcessEnvironment")]
public sealed class ArchiveAudioPolicyTests
{
    [Fact]
    public void GetMissingFields_StandardSingleTrackDoesNotRequireNormalizationTargets()
    {
        var project = CreateProject(1);

        var missing = ArchiveProjectService.GetMissingFields(project);

        Assert.DoesNotContain("共通LUFS（-70～-5）", missing);
        Assert.DoesNotContain("共通True Peak（-8～0）", missing);
        Assert.DoesNotContain("音声1の共通／個別目標", missing);
    }

    [Fact]
    public void GetMissingFields_StandardMultipleTracksRequiresTargetsOnlyWhenEnabled()
    {
        var project = CreateProject(2);

        var enabledMissing = ArchiveProjectService.GetMissingFields(project);

        Assert.Contains("共通LUFS（-70～-5）", enabledMissing);
        Assert.Contains("共通True Peak（-8～0）", enabledMissing);
        Assert.Contains("音声1の共通／個別目標", enabledMissing);

        project.Settings.SkipLoudnessNormalization = true;
        project.Settings.NormalizationTargetIntegratedLufs = -100;
        project.Settings.NormalizationTargetTruePeakDbtp = 4;
        foreach (var track in project.Tracks)
        {
            track.IsNormalizationOverrideEnabled = true;
            track.TargetIntegratedLufs = -100;
            track.TargetTruePeakDbtp = 4;
        }

        var skippedMissing = ArchiveProjectService.GetMissingFields(project);

        Assert.DoesNotContain("共通LUFS（-70～-5）", skippedMissing);
        Assert.DoesNotContain("共通True Peak（-8～0）", skippedMissing);
        Assert.DoesNotContain("音声1の個別目標", skippedMissing);

        project.Settings.NormalTextOverlayEnabled = true;
        project.Settings.TextOverlayLayoutJson = null;
        Assert.Contains("文字合成レイアウト", ArchiveProjectService.GetMissingFields(project));
    }

    [Fact]
    public void GetMissingFields_ShortsAndDraftNeverRequireNormalizationTargets()
    {
        var shorts = CreateProject(1);
        shorts.Profile = EncodeProfile.CopyrightCheckProduction;
        shorts.Orientation = VideoOrientation.Vertical;
        var shortsMissing = ArchiveProjectService.GetMissingFields(shorts);

        Assert.DoesNotContain("共通LUFS（-70～-5）", shortsMissing);
        Assert.DoesNotContain("音声1の共通／個別目標", shortsMissing);

        var draft = CreateProject(1);
        draft.UseDraftMode = true;
        draft.Profile = EncodeProfile.DraftPreview;
        draft.DraftAudioQuality = DraftAudioQuality.Low;
        var draftMissing = ArchiveProjectService.GetMissingFields(draft);

        Assert.DoesNotContain("共通LUFS（-70～-5）", draftMissing);
        Assert.DoesNotContain("音声1の共通／個別目標", draftMissing);
    }

    [Fact]
    public void SaveAndLoad_AllowInvalidTargetsWhenVideoLevelNormalizationIsOff()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MovieMaker-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var project = CreateProject(2);
            project.Settings.SkipLoudnessNormalization = true;
            project.Settings.NormalizationTargetIntegratedLufs = -100;
            project.Settings.NormalizationTargetTruePeakDbtp = 4;
            File.WriteAllBytes(Path.Combine(directory, "background.png"), [0x01]);
            for (var index = 0; index < project.Tracks.Count; index++)
            {
                var name = $"song-{index}.mp3";
                File.WriteAllBytes(Path.Combine(directory, name), [0x01]);
                project.Tracks[index].AudioPath = name;
                project.Tracks[index].IsNormalizationOverrideEnabled = true;
                project.Tracks[index].TargetIntegratedLufs = -100;
                project.Tracks[index].TargetTruePeakDbtp = 4;
            }

            ArchiveProjectService.Save(directory, project);
            var loaded = ArchiveProjectService.Load(Path.Combine(directory, ArchiveProjectService.FileName));

            Assert.Equal(2, loaded.Project.SchemaVersion);
            Assert.True(loaded.Project.Settings.SkipLoudnessNormalization);
            Assert.Equal(-100, loaded.Project.Settings.NormalizationTargetIntegratedLufs);
            Assert.Equal(4, loaded.Project.Settings.NormalizationTargetTruePeakDbtp);
            Assert.Equal(-100, loaded.Project.Tracks[0].TargetIntegratedLufs);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreWithNormalizationOff_ResavePreservesUnconfirmedIndividualTargets()
    {
        var directory = CreateArchiveFixture(trackCount: 2, skipNormalization: true);
        Task analysisCompletion = Task.CompletedTask;
        try
        {
            var viewModel = new MainViewModel();
            viewModel.LoadArchiveProject(Path.Combine(directory, ArchiveProjectService.FileName));
            analysisCompletion = WaitForAudioAnalysisAsync(viewModel.AudioTracks.ToArray());

            var snapshot = CaptureArchiveProject(viewModel);
            Assert.True(snapshot.Settings.SkipLoudnessNormalization);
            Assert.Equal(true, snapshot.Tracks[0].IsNormalizationOverrideEnabled);
            Assert.Null(snapshot.Tracks[0].TargetIntegratedLufs);
            Assert.Null(snapshot.Tracks[0].TargetTruePeakDbtp);

            ArchiveProjectService.Save(directory, snapshot, overwrite: true);
            var reloaded = ArchiveProjectService.Load(Path.Combine(directory, ArchiveProjectService.FileName));

            Assert.Null(reloaded.Project.Tracks[0].TargetIntegratedLufs);
            Assert.Null(reloaded.Project.Tracks[0].TargetTruePeakDbtp);
        }
        finally
        {
            await analysisCompletion;
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreOffThenEnableNormalization_RequiresUnconfirmedIndividualTargets()
    {
        var directory = CreateArchiveFixture(trackCount: 2, skipNormalization: true);
        Task analysisCompletion = Task.CompletedTask;
        try
        {
            var viewModel = new MainViewModel();
            viewModel.LoadArchiveProject(Path.Combine(directory, ArchiveProjectService.FileName));
            analysisCompletion = WaitForAudioAnalysisAsync(viewModel.AudioTracks.ToArray());
            Assert.True(viewModel.SkipLoudnessNormalization);

            viewModel.SkipLoudnessNormalization = false;

            Assert.Contains("音声1の個別目標", viewModel.ArchiveStateMessage);
            Assert.Contains("音声1の個別目標", ArchiveProjectService.GetMissingFields(CaptureArchiveProject(viewModel)));
        }
        finally
        {
            await analysisCompletion;
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreSingleTrackWithOffThenAddTrack_RequiresUnconfirmedIndividualTargetsWhenEnabled()
    {
        var directory = CreateArchiveFixture(trackCount: 1, skipNormalization: true);
        Task analysisCompletion = Task.CompletedTask;
        try
        {
            var viewModel = new MainViewModel();
            viewModel.LoadArchiveProject(Path.Combine(directory, ArchiveProjectService.FileName));
            var restoredTracks = viewModel.AudioTracks.ToArray();
            analysisCompletion = WaitForAudioAnalysisAsync(restoredTracks);
            Assert.True(viewModel.SkipLoudnessNormalization);

            viewModel.AudioTracks.Add(new AudioTrackItem("added.mp3"));
            viewModel.SkipLoudnessNormalization = false;

            Assert.Contains("音声1の個別目標", viewModel.ArchiveStateMessage);
            Assert.Contains("音声1の個別目標", ArchiveProjectService.GetMissingFields(CaptureArchiveProject(viewModel)));
        }
        finally
        {
            await analysisCompletion;
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExplicitIndividualTargetEdits_ConfirmPreviouslyUnrecordedTargets()
    {
        var directory = CreateArchiveFixture(trackCount: 2, skipNormalization: true);
        Task analysisCompletion = Task.CompletedTask;
        try
        {
            var viewModel = new MainViewModel();
            viewModel.LoadArchiveProject(Path.Combine(directory, ArchiveProjectService.FileName));
            analysisCompletion = WaitForAudioAnalysisAsync(viewModel.AudioTracks.ToArray());
            viewModel.SkipLoudnessNormalization = false;
            var track = viewModel.AudioTracks[0];
            Assert.Contains("音声1の個別目標", viewModel.ArchiveStateMessage);

            track.NormalizationTargetLufsText = "-13.5";
            track.NormalizationTargetTruePeakText = "-2";

            var snapshot = CaptureArchiveProject(viewModel);
            Assert.Equal(-13.5, snapshot.Tracks[0].TargetIntegratedLufs);
            Assert.Equal(-2, snapshot.Tracks[0].TargetTruePeakDbtp);
            Assert.DoesNotContain("音声1の個別目標", viewModel.ArchiveStateMessage);
        }
        finally
        {
            await analysisCompletion;
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void NewArchiveProject_UsesSchemaTwoAndNormalizationOnByDefault()
    {
        var project = new ArchiveProject();

        Assert.Equal(2, project.SchemaVersion);
        Assert.False(project.Settings.SkipLoudnessNormalization);
    }

    [Fact]
    public void GetLegacyPolicyChanges_ExplainsSingleTrackAndShortsProcessingChanges()
    {
        var legacySingle = CreateProject(1);
        legacySingle.SchemaVersion = 1;
        var singleChanges = string.Join("\n", ArchiveProjectService.GetLegacyPolicyChanges(legacySingle));

        Assert.Contains("通常1曲", singleChanges);
        Assert.Contains("音量調整は、旧仕様のありから現在のなし", singleChanges);

        var legacyShorts = CreateProject(1);
        legacyShorts.SchemaVersion = 1;
        legacyShorts.Profile = EncodeProfile.CopyrightCheckProduction;
        legacyShorts.Orientation = VideoOrientation.Vertical;
        var shortsChanges = string.Join("\n", ArchiveProjectService.GetLegacyPolicyChanges(legacyShorts));

        Assert.Contains("128 kbps / 32 kHz", shortsChanges);
        Assert.Contains("AAC 320 kbps / 48 kHz", shortsChanges);
    }

    [Fact]
    public void Load_AcceptsLegacySchemaVersionOneProject()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MovieMaker-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "music"));
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "music", "song.mp3"), [0x01]);
            var jsonPath = Path.Combine(directory, ArchiveProjectService.FileName);
            File.WriteAllText(jsonPath, LegacySchemaVersionOneJson);

            var loaded = ArchiveProjectService.Load(jsonPath);

            Assert.Equal(1, loaded.Project.SchemaVersion);
            Assert.False(loaded.Project.Settings.SkipLoudnessNormalization);
            Assert.Equal("legacy-project", loaded.Project.Title);
            Assert.Single(loaded.AudioFullPaths);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ArchiveProject CreateProject(int trackCount) => new()
    {
        Title = "archive-test",
        UseDraftMode = false,
        Profile = EncodeProfile.Standard,
        Orientation = VideoOrientation.Horizontal,
        DraftAudioQuality = null,
        ImagePath = "background.png",
        Tracks = Enumerable.Range(0, trackCount).Select(index => new ArchiveTrack
        {
            AudioPath = $"music/song-{index}.mp3",
            OriginalFileName = $"song-{index}.mp3",
            IsNormalizationOverrideEnabled = null,
            TargetIntegratedLufs = null,
            TargetTruePeakDbtp = null
        }).ToList(),
        Settings = new ArchiveProcessingSettings
        {
            ShortsMaximumSeconds = 60,
            OneMinuteShortsOffsetSeconds = 0,
            ThreeMinuteShortsOffsetSeconds = 0,
            NormalizationTargetIntegratedLufs = null,
            NormalizationTargetTruePeakDbtp = null,
            NormalTextOverlayEnabled = false,
            TextOverlayLayoutJson = null
        }
    };

    private static string CreateArchiveFixture(int trackCount, bool skipNormalization)
    {
        var directory = Path.Combine(Path.GetTempPath(), "MovieMaker-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "background.png"), Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        var project = new ArchiveProject
        {
            Title = "archive-audio-policy",
            UseDraftMode = false,
            Profile = EncodeProfile.Standard,
            Orientation = VideoOrientation.Horizontal,
            ImagePath = "background.png",
            Tracks = Enumerable.Range(0, trackCount).Select(index =>
            {
                var fileName = $"song-{index}.mp3";
                File.WriteAllBytes(Path.Combine(directory, fileName), [0x01]);
                return new ArchiveTrack
                {
                    AudioPath = fileName,
                    OriginalFileName = fileName,
                    IsNormalizationOverrideEnabled = index == 0,
                    TargetIntegratedLufs = null,
                    TargetTruePeakDbtp = null
                };
            }).ToList(),
            Settings = new ArchiveProcessingSettings
            {
                ShortsMaximumSeconds = 60,
                OneMinuteShortsOffsetSeconds = 3,
                ThreeMinuteShortsOffsetSeconds = 3,
                NormalizationTargetIntegratedLufs = -14,
                NormalizationTargetTruePeakDbtp = -1,
                NormalTextOverlayEnabled = false,
                SkipLoudnessNormalization = skipNormalization
            }
        };
        ArchiveProjectService.Save(directory, project);
        return directory;
    }

    private static ArchiveProject CaptureArchiveProject(MainViewModel viewModel)
    {
        var method = typeof(MainViewModel).GetMethod("CaptureArchiveProject", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<ArchiveProject>(method!.Invoke(viewModel, null));
    }

    private static async Task WaitForAudioAnalysisAsync(IReadOnlyList<AudioTrackItem> tracks)
    {
        if (tracks.All(track => track.IsLoudnessAnalysisComplete)) return;

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnTrackPropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(AudioTrackItem.IsLoudnessAnalysisComplete) &&
                tracks.All(track => track.IsLoudnessAnalysisComplete))
                completion.TrySetResult(true);
        }

        foreach (var track in tracks) track.PropertyChanged += OnTrackPropertyChanged;
        try
        {
            if (tracks.All(track => track.IsLoudnessAnalysisComplete)) completion.TrySetResult(true);
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            foreach (var track in tracks) track.PropertyChanged -= OnTrackPropertyChanged;
        }
    }

    private const string LegacySchemaVersionOneJson = """
        {
          "format": "movie-maker-project",
          "schemaVersion": 1,
          "origin": "app",
          "title": "legacy-project",
          "useDraftMode": false,
          "profile": "Standard",
          "orientation": "Horizontal",
          "draftAudioQuality": null,
          "imagePath": null,
          "tracks": [
            {
              "audioPath": "music/song.mp3",
              "originalFileName": "song.mp3",
              "isNormalizationOverrideEnabled": null,
              "targetIntegratedLufs": null,
              "targetTruePeakDbtp": null
            }
          ],
          "settings": {
            "shortsMaximumSeconds": null,
            "oneMinuteShortsOffsetSeconds": null,
            "threeMinuteShortsOffsetSeconds": null,
            "normalizationTargetIntegratedLufs": null,
            "normalizationTargetTruePeakDbtp": null,
            "normalTextOverlayEnabled": null,
            "textOverlayLayoutJson": null
          },
          "appVersion": null,
          "preset": null
        }
        """;
}
