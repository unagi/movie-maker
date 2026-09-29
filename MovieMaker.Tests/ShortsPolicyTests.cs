using MovieMaker.Models;
using MovieMaker.Services;
using Xunit;

namespace MovieMaker.Tests;

public class ShortsPolicyTests
{
    [Fact]
    public void SafeTargetSeconds_IsUnderThreeMinutesWithMargin()
    {
        Assert.Equal(60.0, ShortsPolicy.StrongShortsLimitSeconds);
        Assert.Equal(180.0, ShortsPolicy.PlatformLimitSeconds);
        Assert.Equal(1.0, ShortsPolicy.FadeSeconds);
        Assert.Equal(57.0, ShortsPolicy.GetStrongShortsTrimTriggerSeconds(new AppSettings()));
        Assert.Equal(57.0, ShortsPolicy.GetStrongShortsSafeTargetSeconds(new AppSettings()));
        Assert.Equal(177.0, ShortsPolicy.GetTrimTriggerSeconds(new AppSettings()));
        Assert.Equal(177.0, ShortsPolicy.GetSafeTargetSeconds(new AppSettings()));
    }

    [Theory]
    [InlineData(59.0, 57.0)]
    [InlineData(60.0, 57.0)]
    [InlineData(177.0, 177.0)]
    [InlineData(200.0, 177.0)]
    public void TryGetTrimTargetSeconds_ReturnsExpectedTarget(double duration, double expectedTarget)
    {
        var result = ShortsPolicy.TryGetTrimTargetSeconds(duration, out var target, new AppSettings());

        Assert.True(result);
        Assert.Equal(expectedTarget, target);
    }

    [Theory]
    [InlineData(30.0)]
    [InlineData(56.9)]
    [InlineData(61.0)]
    [InlineData(120.0)]
    public void TryGetTrimTargetSeconds_ReturnsFalseOutsideTrimWindows(double duration)
    {
        var result = ShortsPolicy.TryGetTrimTargetSeconds(duration, out _, new AppSettings());

        Assert.False(result);
    }

    [Fact]
    public void CustomOffsets_ChangeTrimTargets()
    {
        var settings = new AppSettings
        {
            OneMinuteShortsOffsetSeconds = 5,
            ThreeMinuteShortsOffsetSeconds = 8
        };

        Assert.Equal(55.0, ShortsPolicy.GetStrongShortsTrimTriggerSeconds(settings));
        Assert.Equal(172.0, ShortsPolicy.GetTrimTriggerSeconds(settings));
        Assert.True(ShortsPolicy.TryGetTrimTargetSeconds(58.0, out var strongTarget, settings));
        Assert.Equal(55.0, strongTarget);
        Assert.True(ShortsPolicy.TryGetTrimTargetSeconds(179.0, out var regularTarget, settings));
        Assert.Equal(172.0, regularTarget);
    }

    [Fact]
    public void FractionalOffsets_ChangeTrimTargets()
    {
        var settings = new AppSettings
        {
            OneMinuteShortsOffsetSeconds = 2.5,
            ThreeMinuteShortsOffsetSeconds = 3.125
        };

        Assert.Equal(57.5, ShortsPolicy.GetStrongShortsTrimTriggerSeconds(settings));
        Assert.Equal(176.875, ShortsPolicy.GetTrimTriggerSeconds(settings));
        Assert.True(ShortsPolicy.TryGetTrimTargetSeconds(58.0, out var strongTarget, settings));
        Assert.Equal(57.5, strongTarget);
        Assert.True(ShortsPolicy.TryGetTrimTargetSeconds(177.0, out var regularTarget, settings));
        Assert.Equal(176.875, regularTarget);
    }

    [Theory]
    [InlineData(60, 57, true, 57)]
    [InlineData(60, 59, true, 57)]
    [InlineData(60, 60, true, 57)]
    [InlineData(60, 61, false, 0)]
    [InlineData(61, 59, true, 57)]
    [InlineData(61, 61, true, 0)]
    [InlineData(120, 117, true, 0)]
    [InlineData(180, 177, true, 177)]
    [InlineData(180, 180, true, 177)]
    public void HardMaximumAndLegacyBands_AreSeparateRules(int maximum, double duration,
        bool eligible, double expectedTrimTarget)
    {
        var classification = OutputClassificationService.Classify(1080, 1920, 1, duration, maximum);
        Assert.Equal(eligible, classification.CanEncodeInputs);
        if (!eligible) return;

        var hasTrim = ShortsPolicy.TryGetTrimTargetSeconds(duration, out var target, new AppSettings());
        Assert.Equal(expectedTrimTarget > 0, hasTrim);
        if (hasTrim) Assert.Equal(expectedTrimTarget, target);
    }

    [Theory]
    [InlineData(59, 119.999, true)]
    [InlineData(59.001, 3, false)]
    [InlineData(3, 120, false)]
    [InlineData(double.NaN, 3, false)]
    public void SettingsValidity_UsesExactSafeOffsetBoundaries(double oneMinute, double threeMinute, bool expected)
    {
        Assert.Equal(expected, ShortsPolicy.AreSettingsValid(new AppSettings
        {
            OneMinuteShortsOffsetSeconds = oneMinute,
            ThreeMinuteShortsOffsetSeconds = threeMinute
        }));
    }
}
