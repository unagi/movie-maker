using MovieMaker.Models;
using MovieMaker.Services;
using MovieMaker.ViewModels;
using System.ComponentModel;
using System.Reflection;
using Xunit;

namespace MovieMaker.Tests;

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
    public void CopyrightCheckProductionMode_UsesUpdatedLabel()
    {
        var viewModel = new MainViewModel
        {
            SelectedProfile = EncodeProfile.CopyrightCheckProduction
        };

        Assert.True(viewModel.EncodingSettingsText.Contains("本番画質・軽量音声", StringComparison.Ordinal));
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

        Assert.False(viewModel.ClearInputsCommand.CanExecute(null));

        viewModel.HandleDrop(new[] { audio });
        Assert.True(viewModel.ClearInputsCommand.CanExecute(null));

        viewModel.ClearInputsCommand.Execute(null);
        Assert.Empty(viewModel.AudioTracks);
        Assert.False(viewModel.ClearInputsCommand.CanExecute(null));
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
        Assert.Contains("(0:55)", viewModel.OutputStatusText, StringComparison.Ordinal);
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
