using MovieMaker.Models;
using MovieMaker.ViewModels;
using Xunit;

namespace MovieMaker.Tests;

public class SettingsViewModelTests
{
    [Fact]
    public void TryCreateSettings_PersistsShortsOffsets()
    {
        var viewModel = new SettingsViewModel(new AppSettings())
        {
            OutputDirectory = @"C:\out",
            ArchiveDirectory = @"C:\archive",
            OneMinuteShortsOffsetSeconds = "4",
            ThreeMinuteShortsOffsetSeconds = "6"
        };

        var result = viewModel.TryCreateSettings(out var settings, out var errorMessage);

        Assert.True(result);
        Assert.Null(errorMessage);
        Assert.Equal(4, settings.OneMinuteShortsOffsetSeconds);
        Assert.Equal(6, settings.ThreeMinuteShortsOffsetSeconds);
    }

    [Fact]
    public void TryCreateSettings_RejectsInvalidOneMinuteOffset()
    {
        var viewModel = new SettingsViewModel(new AppSettings())
        {
            OutputDirectory = @"C:\out",
            ArchiveDirectory = @"C:\archive",
            OneMinuteShortsOffsetSeconds = "60",
            ThreeMinuteShortsOffsetSeconds = "3"
        };

        var result = viewModel.TryCreateSettings(out _, out var errorMessage);

        Assert.False(result);
        Assert.Equal("1分Shortsオフセットは 0〜60 未満の数値で入力してください。", errorMessage);
    }

    [Fact]
    public void TryCreateSettings_AllowsFractionalOffsets()
    {
        var viewModel = new SettingsViewModel(new AppSettings())
        {
            OutputDirectory = @"C:\out",
            ArchiveDirectory = @"C:\archive",
            OneMinuteShortsOffsetSeconds = "2.5",
            ThreeMinuteShortsOffsetSeconds = "3.125"
        };

        var result = viewModel.TryCreateSettings(out var settings, out var errorMessage);

        Assert.True(result);
        Assert.Null(errorMessage);
        Assert.Equal(2.5, settings.OneMinuteShortsOffsetSeconds);
        Assert.Equal(3.125, settings.ThreeMinuteShortsOffsetSeconds);
    }
}
