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
    public void CopyrightCheckProductionMode_UsesUpdatedLabel()
    {
        var viewModel = new MainViewModel
        {
            SelectedProfile = EncodeProfile.CopyrightCheckProduction
        };

        Assert.True(viewModel.EncodingSettingsText.Contains("本番画質・軽量音声", StringComparison.Ordinal));
    }

    [Fact]
    public void DraftMode_AcceptsMultipleAudioFilesInFileNameOrder()
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

        var audioPaths = GetPrivateField<List<string>>(viewModel, "_audioPaths");

        Assert.Equal(new[] { earlier, later }, audioPaths);
        Assert.Equal("音楽: 2ファイル", viewModel.AudioFileLabel);
        Assert.Contains("2ファイル", viewModel.AudioStatusText, StringComparison.Ordinal);
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
