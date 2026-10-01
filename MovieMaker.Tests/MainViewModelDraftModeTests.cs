using MovieMaker.Models;
using MovieMaker.Services;
using MovieMaker.ViewModels;
using System.ComponentModel;
using System.Reflection;
using Xunit;

namespace MovieMaker.Tests;

[CollectionDefinition("ProcessEnvironment", DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection;

[Collection("ProcessEnvironment")]
public class MainViewModelDraftModeTests
{
    [Fact]
    public void DraftMode_AllowsEncodingWithoutImage_WhenAudioAndPathsExist()
    {
        using var temp = new TestWorkspace();
        temp.PrepareSettings();
        temp.PrepareFakeFfmpeg();
        var audioPath = temp.CreateFile("sample.mp3");

        var viewModel = new MainViewModel
        {
            Title = "draft-check",
            UseDraftMode = true
        };

        viewModel.HandleDrop(new[] { audioPath });

        Assert.True(viewModel.IsImageReady);
        Assert.True(viewModel.IsOutputReady);
        Assert.True(viewModel.CanEncode);
    }

    [Fact]
    public void StandardMode_StillRequiresImage()
    {
        using var temp = new TestWorkspace();
        temp.PrepareSettings();
        temp.PrepareFakeFfmpeg();
        var audioPath = temp.CreateFile("sample.mp3");

        var viewModel = new MainViewModel
        {
            Title = "standard-check"
        };

        viewModel.HandleDrop(new[] { audioPath });

        Assert.False(viewModel.IsImageReady);
        Assert.False(viewModel.CanEncode);
    }

    [Fact]
    public void CopyrightCheckProductionMode_StillRequiresImage()
    {
        using var temp = new TestWorkspace();
        temp.PrepareSettings();
        temp.PrepareFakeFfmpeg();
        var audioPath = temp.CreateFile("sample.mp3");

        var viewModel = new MainViewModel
        {
            Title = "copyright-check",
            SelectedProfile = EncodeProfile.CopyrightCheckProduction
        };

        viewModel.HandleDrop(new[] { audioPath });

        Assert.False(viewModel.IsImageReady);
        Assert.False(viewModel.CanEncode);
    }

    [Fact]
    public void DraftMode_UsesHorizontalOutputByDefault()
    {
        var viewModel = new MainViewModel
        {
            UseDraftMode = true
        };

        Assert.True(viewModel.IsDraftOrientationHorizontal);
        Assert.False(viewModel.IsDraftOrientationVertical);
        Assert.Contains("横", viewModel.ImageStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void DraftMode_ExposesGeneratedPlaceholderAsEffectiveImage()
    {
        var viewModel = new MainViewModel
        {
            Title = "draft-preview",
            UseDraftMode = true
        };

        Assert.NotNull(viewModel.EffectiveImagePreview);
        Assert.Equal("仮画像（自動生成）", viewModel.EffectiveImageBadgeText);
        Assert.Contains("960x540", viewModel.EffectiveImageCaption, StringComparison.Ordinal);
    }

    [Fact]
    public void PortraitImage_AutoSelectsShortsProfile()
    {
        var viewModel = new MainViewModel();
        SetPrivateField(viewModel, "_imageWidth", 1080);
        SetPrivateField(viewModel, "_imageHeight", 1920);
        SetPrivateField(viewModel, "_orientation", VideoOrientation.Vertical);

        Assert.True(viewModel.EncodingSettingsText.Contains("本番画質・軽量音声", StringComparison.Ordinal));
    }

    [Fact]
    public void PortraitImageWithoutAudio_IsConfirmedShortsAndWaitsForAudio()
    {
        using var temp = new TestWorkspace();
        temp.PrepareSettings();
        var image = temp.CreateImage("portrait.png", 90, 160);
        var viewModel = new MainViewModel();

        viewModel.HandleDrop([image]);

        Assert.Equal(OutputKind.Shorts, viewModel.OutputClassification.Kind);
        Assert.Equal(EncodeProfile.CopyrightCheckProduction, viewModel.SelectedProfile);
        Assert.Contains("音声待ち", viewModel.InputStateMessage);
        Assert.False(viewModel.CanEncode);
    }

    [Fact]
    public void PortraitMultipleTracks_ErrorClearsWhenImageRemoved()
    {
        using var temp = new TestWorkspace();
        temp.PrepareSettings();
        var image = temp.CreateImage("portrait.png", 90, 160);
        var viewModel = new MainViewModel();
        viewModel.HandleDrop([image]);
        viewModel.AudioTracks.Add(new AudioTrackItem("one.wav"));
        viewModel.AudioTracks.Add(new AudioTrackItem("two.wav"));

        Assert.True(viewModel.IsInputError);
        Assert.Contains("1本だけ", viewModel.InputStateMessage);

        viewModel.RemoveImageCommand.Execute(null);

        Assert.Equal(OutputKind.AwaitingImage, viewModel.OutputClassification.Kind);
        Assert.False(viewModel.IsInputError);
    }

    [Fact]
    public void AnalyzedInvalidAudioDurationIsErrorWithoutImage()
    {
        var viewModel = new MainViewModel();
        var track = new AudioTrackItem("invalid.wav");
        track.ApplyAnalysis("解析済み", double.NaN);
        viewModel.AudioTracks.Add(track);

        Assert.True(viewModel.IsInputError);
        Assert.Equal(OutputKind.InputError, viewModel.OutputClassification.Kind);
    }

    [Fact]
    public void SettingsLoad_InvalidLegacyShortsOffset_RemainsVisibleUntilExplicitSave()
    {
        using var temp = new TestWorkspace();
        temp.PrepareSettings();
        File.WriteAllText(SettingsService.SettingsFilePath,
            "{\"OneMinuteShortsOffsetSeconds\":59.5,\"ThreeMinuteShortsOffsetSeconds\":3}");

        SettingsService.Load();

        Assert.Equal(59.5, SettingsService.Current.OneMinuteShortsOffsetSeconds);
        Assert.NotNull(SettingsService.LoadError);
        var viewModel = new MainViewModel();
        Assert.True(viewModel.IsInputError);
        Assert.False(viewModel.CanEncode);

        temp.PrepareSettings();
        Assert.Null(SettingsService.LoadError);
    }

    [Fact]
    public void DraftMode_InvalidShortsSettingsDoNotBlockEncoding()
    {
        using var temp = new TestWorkspace();
        temp.PrepareSettings();
        temp.PrepareFakeFfmpeg();
        File.WriteAllText(SettingsService.SettingsFilePath,
            "{\"OutputDirectory\":\"output\",\"ArchiveDirectory\":\"archive\"," +
            "\"OneMinuteShortsOffsetSeconds\":59.5}");
        SettingsService.Load();
        var viewModel = new MainViewModel
        {
            Title = "draft-check",
            UseDraftMode = true
        };
        viewModel.HandleDrop([temp.CreateFile("song.wav")]);

        Assert.False(viewModel.IsInputError);
        Assert.True(viewModel.CanEncode);
        temp.PrepareSettings();
    }

    [Fact]
    public void SettingsLoad_OldJsonUsesDefaultMaximumAndOverlayChoice()
    {
        using var temp = new TestWorkspace();
        temp.PrepareSettings();
        File.WriteAllText(SettingsService.SettingsFilePath,
            "{\"OutputDirectory\":\"output\",\"ArchiveDirectory\":\"archive\"}");

        SettingsService.Load();

        Assert.Null(SettingsService.LoadError);
        Assert.Equal(60, SettingsService.Current.ShortsMaximumSeconds);
        Assert.True(SettingsService.Current.NormalTextOverlayEnabled);
    }

    [Fact]
    public void DraftMode_AcceptsMultipleAudioFilesInDropOrder()
    {
        using var temp = new TestWorkspace();
        temp.PrepareSettings();
        temp.PrepareFakeFfmpeg();
        var later = temp.CreateFile("z-last.mp3");
        var earlier = temp.CreateFile("a-first.wav");

        var viewModel = new MainViewModel
        {
            Title = "draft-check",
            UseDraftMode = true
        };

        viewModel.HandleDrop(new[] { later, earlier });

        Assert.Equal(new[] { later, earlier }, viewModel.AudioTracks.Select(track => track.Path));
        Assert.Equal("音楽: 2ファイル", viewModel.AudioFileLabel);
        Assert.Contains("2ファイル", viewModel.AudioStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void DraftMode_DroppingFolderAddsAllSupportedAudioFiles()
    {
        using var temp = new TestWorkspace();
        temp.PrepareSettings();
        temp.PrepareFakeFfmpeg();
        var folder = Path.Combine(temp.RootPath, "album");
        var nestedFolder = Path.Combine(folder, "disc-2");
        Directory.CreateDirectory(nestedFolder);
        var first = Path.Combine(folder, "01-first.mp3");
        var second = Path.Combine(folder, "02-second.wav");
        var third = Path.Combine(nestedFolder, "03-third.m4a");
        File.WriteAllText(first, "test");
        File.WriteAllText(second, "test");
        File.WriteAllText(third, "test");
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "test");

        var viewModel = new MainViewModel
        {
            Title = "folder-drop",
            UseDraftMode = true
        };

        viewModel.HandleDrop(new[] { folder });

        Assert.Equal(new[] { first, second, third }, viewModel.AudioTracks.Select(track => track.Path));
        Assert.Contains("音声3件を末尾へ追加", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("未対応ファイル1件", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void DraftMode_SequentialDropsAppendAudioTracks()
    {
        using var temp = new TestWorkspace();
        temp.PrepareSettings();
        temp.PrepareFakeFfmpeg();
        var first = temp.CreateFile("first.mp3");
        var second = temp.CreateFile("second.m4a");

        var viewModel = new MainViewModel
        {
            Title = "draft-check",
            UseDraftMode = true
        };

        viewModel.HandleDrop(new[] { first });
        viewModel.HandleDrop(new[] { second });

        Assert.Equal(new[] { first, second }, viewModel.AudioTracks.Select(track => track.Path));
        Assert.Equal(new[] { 1, 2 }, viewModel.AudioTracks.Select(track => track.Position));
        Assert.Contains("2ファイル", viewModel.AudioQueueSummaryText, StringComparison.Ordinal);
        Assert.Contains("追加", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void DraftMode_RedroppingAudioSkipsDuplicateWithoutChangingOrder()
    {
        using var temp = new TestWorkspace();
        temp.PrepareSettings();
        temp.PrepareFakeFfmpeg();
        var first = temp.CreateFile("first.mp3");
        var second = temp.CreateFile("second.wav");

        var viewModel = new MainViewModel
        {
            Title = "draft-check",
            UseDraftMode = true
        };

        viewModel.HandleDrop(new[] { first, second });
        viewModel.HandleDrop(new[] { first });

        Assert.Equal(new[] { first, second }, viewModel.AudioTracks.Select(track => track.Path));
        Assert.Contains("重複", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void DraftMode_MoveAndRemoveAudioTracksUpdatesQueue()
    {
        using var temp = new TestWorkspace();
        temp.PrepareSettings();
        temp.PrepareFakeFfmpeg();
        var first = temp.CreateFile("first.mp3");
        var second = temp.CreateFile("second.wav");
        var third = temp.CreateFile("third.m4a");

        var viewModel = new MainViewModel
        {
            Title = "draft-check",
            UseDraftMode = true
        };

        viewModel.HandleDrop(new[] { first, second, third });
        viewModel.MoveAudioTrack(2, 0);
        viewModel.RemoveAudioTrackCommand.Execute(viewModel.AudioTracks[1]);

        Assert.Equal(new[] { third, second }, viewModel.AudioTracks.Select(track => track.Path));
        Assert.Equal("音楽: 2ファイル", viewModel.AudioFileLabel);
    }

    [Fact]
    public void SelectedAudioTrackCommands_MoveToEveryBoundaryAndKeepSelection()
    {
        using var temp = new TestWorkspace();
        temp.PrepareSettings();
        temp.PrepareFakeFfmpeg();
        var first = temp.CreateFile("first.mp3");
        var second = temp.CreateFile("second.wav");
        var third = temp.CreateFile("third.m4a");
        var viewModel = new MainViewModel { UseDraftMode = true };
        viewModel.HandleDrop(new[] { first, second, third });

        Assert.False(viewModel.MoveAudioTrackUpCommand.CanExecute(null));
        viewModel.SelectedAudioTrack = viewModel.AudioTracks[1];
        var selected = viewModel.SelectedAudioTrack;
        viewModel.MoveAudioTrackFirstCommand.Execute(null);
        Assert.Same(selected, viewModel.SelectedAudioTrack);
        Assert.Equal(new[] { second, first, third }, viewModel.AudioTracks.Select(track => track.Path));
        Assert.False(viewModel.MoveAudioTrackUpCommand.CanExecute(null));
        Assert.False(viewModel.MoveAudioTrackFirstCommand.CanExecute(null));
        viewModel.MoveAudioTrackDownCommand.Execute(null);
        Assert.Equal(new[] { first, second, third }, viewModel.AudioTracks.Select(track => track.Path));
        viewModel.MoveAudioTrackLastCommand.Execute(null);
        Assert.Equal(new[] { first, third, second }, viewModel.AudioTracks.Select(track => track.Path));
        Assert.False(viewModel.MoveAudioTrackDownCommand.CanExecute(null));
        Assert.False(viewModel.MoveAudioTrackLastCommand.CanExecute(null));
        viewModel.MoveAudioTrackUpCommand.Execute(null);
        Assert.Equal(new[] { first, second, third }, viewModel.AudioTracks.Select(track => track.Path));
        Assert.Same(selected, viewModel.SelectedAudioTrack);
        Assert.Equal(new[] { 1, 2, 3 }, viewModel.AudioTracks.Select(track => track.Position));
    }

    [Fact]
    public void SelectedAudioTrackCommands_DisableForOneTrackAndClearSelectionWhenRemoved()
    {
        using var temp = new TestWorkspace();
        temp.PrepareSettings();
        temp.PrepareFakeFfmpeg();
        var viewModel = new MainViewModel { UseDraftMode = true };
        viewModel.HandleDrop(new[] { temp.CreateFile("only.mp3") });
        viewModel.SelectedAudioTrack = viewModel.AudioTracks[0];

        Assert.False(viewModel.MoveAudioTrackUpCommand.CanExecute(null));
        Assert.False(viewModel.MoveAudioTrackDownCommand.CanExecute(null));
        Assert.False(viewModel.MoveAudioTrackFirstCommand.CanExecute(null));
        Assert.False(viewModel.MoveAudioTrackLastCommand.CanExecute(null));

        viewModel.RemoveAudioTrackCommand.Execute(viewModel.SelectedAudioTrack);
        Assert.Null(viewModel.SelectedAudioTrack);
    }

    [Fact]
    public void SelectedAudioTrackCommands_NotifyAndDisableDuringEncodingAndAfterClear()
    {
        using var temp = new TestWorkspace();
        temp.PrepareSettings();
        temp.PrepareFakeFfmpeg();
        var viewModel = new MainViewModel { UseDraftMode = true };
        viewModel.HandleDrop(new[] { temp.CreateFile("first.mp3"), temp.CreateFile("second.mp3") });
        var notifications = 0;
        viewModel.MoveAudioTrackDownCommand.CanExecuteChanged += (_, _) => notifications++;
        viewModel.SelectedAudioTrack = viewModel.AudioTracks[0];
        Assert.True(viewModel.MoveAudioTrackDownCommand.CanExecute(null));
        Assert.True(notifications > 0);

        typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsEncoding))!.SetValue(viewModel, true);
        Assert.False(viewModel.MoveAudioTrackDownCommand.CanExecute(null));
        Assert.False(viewModel.MoveAudioTrackLastCommand.CanExecute(null));
        typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsEncoding))!.SetValue(viewModel, false);
        Assert.True(viewModel.MoveAudioTrackDownCommand.CanExecute(null));

        viewModel.ClearInputsCommand.Execute(null);
        Assert.Null(viewModel.SelectedAudioTrack);
        Assert.False(viewModel.MoveAudioTrackDownCommand.CanExecute(null));
        Assert.True(notifications >= 4);
    }

    [Fact]
    public void StandardMode_SequentialAudioDropsAlsoAppendTracks()
    {
        using var temp = new TestWorkspace();
        temp.PrepareSettings();
        temp.PrepareFakeFfmpeg();
        var first = temp.CreateFile("first.mp3");
        var second = temp.CreateFile("second.m4a");

        var viewModel = new MainViewModel
        {
            Title = "standard-check"
        };

        viewModel.HandleDrop(new[] { first });
        viewModel.HandleDrop(new[] { second });

        Assert.Equal(new[] { first, second }, viewModel.AudioTracks.Select(track => track.Path));
    }

    [Fact]
    public void DraftMode_ClearCommandReflectsActualInputs()
    {
        using var temp = new TestWorkspace();
        temp.PrepareSettings();
        temp.PrepareFakeFfmpeg();
        var audio = temp.CreateFile("sample.mp3");

        var viewModel = new MainViewModel
        {
            Title = "draft-check",
            UseDraftMode = true
        };

        Assert.True(viewModel.ClearInputsCommand.CanExecute(null));

        viewModel.HandleDrop(new[] { audio });
        Assert.True(viewModel.ClearInputsCommand.CanExecute(null));

        viewModel.ClearInputsCommand.Execute(null);
        Assert.Equal(string.Empty, viewModel.Title);
        Assert.Empty(viewModel.AudioTracks);
        Assert.False(viewModel.ClearInputsCommand.CanExecute(null));
    }

    [Fact]
    public void ClearInputsCommand_ClearsTitleEvenWhenNoMediaInputExists()
    {
        var viewModel = new MainViewModel
        {
            Title = "manual-title"
        };

        Assert.True(viewModel.ClearInputsCommand.CanExecute(null));

        viewModel.ClearInputsCommand.Execute(null);

        Assert.Equal(string.Empty, viewModel.Title);
        Assert.False(viewModel.ClearInputsCommand.CanExecute(null));
        Assert.Equal("準備してください", viewModel.StatusMessage);
    }

    [Fact]
    public void DraftMode_DroppingImageReportsThatItIsNotUsed()
    {
        using var temp = new TestWorkspace();
        temp.PrepareSettings();
        temp.PrepareFakeFfmpeg();
        var image = temp.CreateFile("cover.png");

        var viewModel = new MainViewModel
        {
            Title = "draft-check",
            UseDraftMode = true
        };

        viewModel.HandleDrop(new[] { image });

        Assert.Contains("仮動画", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("画像", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void DroppingUnsupportedFileReportsSkippedCount()
    {
        using var temp = new TestWorkspace();
        temp.PrepareSettings();
        temp.PrepareFakeFfmpeg();
        var unsupported = temp.CreateFile("notes.txt");

        var viewModel = new MainViewModel
        {
            Title = "drop-check"
        };

        viewModel.HandleDrop(new[] { unsupported });

        Assert.Contains("未対応ファイル1件", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void StandardMode_DroppingMultipleImagesDoesNotChooseOneSilently()
    {
        using var temp = new TestWorkspace();
        temp.PrepareSettings();
        temp.PrepareFakeFfmpeg();
        var first = temp.CreateFile("first.png");
        var second = temp.CreateFile("second.jpg");

        var viewModel = new MainViewModel
        {
            Title = "drop-check"
        };

        viewModel.HandleDrop(new[] { first, second });

        Assert.False(viewModel.IsImageReady);
        Assert.Contains("画像は1件ずつ", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void StandardMode_InvalidReplacementKeepsCurrentImage()
    {
        using var temp = new TestWorkspace();
        temp.PrepareSettings();
        temp.PrepareFakeFfmpeg();
        var current = temp.CreatePng("current.png");
        var broken = temp.CreateFile("broken.png");

        var viewModel = new MainViewModel
        {
            Title = "drop-check"
        };

        viewModel.HandleDrop(new[] { current });
        viewModel.HandleDrop(new[] { broken });

        Assert.Contains("current.png", viewModel.ImageStatusText, StringComparison.Ordinal);
        Assert.Contains("現在の画像を維持", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void DraftMode_DefaultsToHighAudioQuality()
    {
        var viewModel = new MainViewModel
        {
            UseDraftMode = true
        };

        Assert.True(viewModel.IsDraftAudioQualityHigh);
        Assert.False(viewModel.IsDraftAudioQualityLow);
        Assert.Contains("256k", viewModel.EncodingSettingsText, StringComparison.Ordinal);
        Assert.Contains("44.1 kHz", viewModel.EncodingSettingsText, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdatingSettingsLabels_RaisesStatusPropertiesForQueuedVideo()
    {
        using var temp = new TestWorkspace();
        temp.PrepareSettings();

        var viewModel = new MainViewModel
        {
            SelectedProfile = EncodeProfile.Standard
        };

        SetPrivateField(viewModel, "_orientation", VideoOrientation.Vertical);
        SetPrivateField(viewModel, "_imageWidth", 1080);
        SetPrivateField(viewModel, "_imageHeight", 1920);
        SetPrivateField(viewModel, "_aspectValid", true);
        SetPrivateField(viewModel, "_audioDurationSeconds", 58.0);

        SettingsService.Save(new AppSettings
        {
            OutputDirectory = Path.Combine(temp.RootPath, "output"),
            ArchiveDirectory = Path.Combine(temp.RootPath, "archive"),
            OneMinuteShortsOffsetSeconds = 5.0,
            ThreeMinuteShortsOffsetSeconds = 3.0
        });

        var changedProperties = new List<string>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                changedProperties.Add(args.PropertyName);
            }
        };

        InvokePrivateMethod(viewModel, "UpdateSettingsLabels");

        Assert.Contains(nameof(MainViewModel.OutputStatusText), changedProperties);
        Assert.Contains(nameof(MainViewModel.EncodingSettingsText), changedProperties);
        Assert.Equal("本番画質・軽量音声 (YouTube Short) (0:55)", viewModel.OutputStatusText);

        changedProperties.Clear();
        SetPrivateField(viewModel, "_imageWidth", 1920);
        SetPrivateField(viewModel, "_imageHeight", 1080);
        SetPrivateField(viewModel, "_orientation", VideoOrientation.Horizontal);
        InvokePrivateMethod(viewModel, "UpdateSettingsLabels");

        Assert.Contains(nameof(MainViewModel.OutputStatusText), changedProperties);
        Assert.Contains(nameof(MainViewModel.EncodingSettingsText), changedProperties);
        Assert.Equal("YouTube (0:58)", viewModel.OutputStatusText);
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private static T GetPrivateField<T>(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<T>(field!.GetValue(target));
    }

    private static void InvokePrivateMethod(object target, string methodName)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(target, null);
    }

    private sealed class TestWorkspace : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "MovieMakerTests", Guid.NewGuid().ToString("N"));
        private readonly string _originalPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        private readonly string? _originalSettingsDir = Environment.GetEnvironmentVariable("MOVIEMAKER_SETTINGS_DIR");

        public string RootPath => _root;

        public TestWorkspace()
        {
            Directory.CreateDirectory(_root);
        }

        public void PrepareSettings()
        {
            var output = Path.Combine(_root, "output");
            var archive = Path.Combine(_root, "archive");
            var settings = Path.Combine(_root, "settings");
            Directory.CreateDirectory(output);
            Directory.CreateDirectory(archive);
            Directory.CreateDirectory(settings);
            Environment.SetEnvironmentVariable("MOVIEMAKER_SETTINGS_DIR", settings);
            SettingsService.Save(new AppSettings
            {
                OutputDirectory = output,
                ArchiveDirectory = archive
            });
        }

        public void PrepareFakeFfmpeg()
        {
            var ffmpegDir = Path.Combine(_root, "ffmpeg");
            Directory.CreateDirectory(ffmpegDir);
            File.WriteAllText(Path.Combine(ffmpegDir, "ffmpeg.exe"), string.Empty);
            Environment.SetEnvironmentVariable("PATH", $"{ffmpegDir};{_originalPath}");
        }

        public string CreateFile(string name)
        {
            var path = Path.Combine(_root, name);
            File.WriteAllText(path, "test");
            return path;
        }

        public string CreatePng(string name)
        {
            const string onePixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";
            var path = Path.Combine(_root, name);
            File.WriteAllBytes(path, Convert.FromBase64String(onePixelPng));
            return path;
        }

        public string CreateImage(string name, int width, int height)
        {
            var path = Path.Combine(_root, name);
            using var bitmap = new System.Drawing.Bitmap(width, height);
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            return path;
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("PATH", _originalPath);
            Environment.SetEnvironmentVariable("MOVIEMAKER_SETTINGS_DIR", _originalSettingsDir);
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch
            {
                // ignore cleanup failures
            }
        }
    }
}
