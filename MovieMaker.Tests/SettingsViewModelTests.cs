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
        Assert.Equal("1分Shortsオフセットは 0〜59 の数値で入力してください。", errorMessage);
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

    [Theory]
    [InlineData("0", "119.999", true)]
    [InlineData("59", "0", true)]
    [InlineData("59.001", "3", false)]
    [InlineData("3", "120", false)]
    public void TryCreateSettings_EnforcesMeaningfulOffsetRange(string first, string third, bool valid)
    {
        var viewModel = new SettingsViewModel(new AppSettings())
        {
            OneMinuteShortsOffsetSeconds = first,
            ThreeMinuteShortsOffsetSeconds = third
        };

        Assert.Equal(valid, viewModel.TryCreateSettings(out _, out _));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("180", true)]
    [InlineData("0", false)]
    [InlineData("181", false)]
    public void TryCreateSettings_ValidatesShortsMaximum(string maximum, bool valid)
    {
        var viewModel = new SettingsViewModel(new AppSettings())
        {
            ShortsMaximumSeconds = maximum
        };

        Assert.Equal(valid, viewModel.TryCreateSettings(out var settings, out _));
        if (valid) Assert.Equal(int.Parse(maximum), settings.ShortsMaximumSeconds);
    }

    [Fact]
    public void ShortsSettingsError_IsVisibleOnOpenAndClearsAfterCorrection()
    {
        var viewModel = new SettingsViewModel(new AppSettings
        {
            OneMinuteShortsOffsetSeconds = 59.5
        });

        Assert.Contains("1分Shortsオフセット", viewModel.ShortsSettingsError);
        viewModel.OneMinuteShortsOffsetSeconds = "59";
        Assert.Equal(string.Empty, viewModel.ShortsSettingsError);
    }
}
