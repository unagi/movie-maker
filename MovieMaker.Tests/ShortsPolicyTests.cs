using MovieMaker.Services;
using Xunit;

namespace MovieMaker.Tests;

public class ShortsPolicyTests
{
    [Fact]
    public void SafeTargetSeconds_IsUnderThreeMinutesWithMargin()
    {
        Assert.Equal(60.0, ShortsPolicy.StrongShortsLimitSeconds);
        Assert.Equal(57.0, ShortsPolicy.StrongShortsTrimTriggerSeconds);
        Assert.Equal(57.0, ShortsPolicy.StrongShortsSafeTargetSeconds);
        Assert.Equal(180.0, ShortsPolicy.PlatformLimitSeconds);
        Assert.Equal(177.0, ShortsPolicy.TrimTriggerSeconds);
        Assert.Equal(177.0, ShortsPolicy.SafeTargetSeconds);
        Assert.Equal(1.0, ShortsPolicy.FadeSeconds);
    }

    [Theory]
    [InlineData(59.0, 57.0)]
    [InlineData(60.0, 57.0)]
    [InlineData(177.0, 177.0)]
    [InlineData(200.0, 177.0)]
    public void TryGetTrimTargetSeconds_ReturnsExpectedTarget(double duration, double expectedTarget)
    {
        var result = ShortsPolicy.TryGetTrimTargetSeconds(duration, out var target);

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
        var result = ShortsPolicy.TryGetTrimTargetSeconds(duration, out _);

        Assert.False(result);
    }
}
