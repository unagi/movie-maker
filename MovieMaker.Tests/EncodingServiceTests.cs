using MovieMaker.Services;
using Xunit;

namespace MovieMaker.Tests;

public class EncodingServiceTests
{
    [Fact]
    public void BuildAudioFilter_ForShortsTrim_AddsFadeAndTrailingSilenceRemoval()
    {
        var filter = EncodingService.BuildAudioFilter(trimTargetSeconds: 177.0);

        Assert.Equal(
            "afade=t=out:st=176:d=1,silenceremove=stop_periods=1:stop_duration=0.25:stop_threshold=-50dB",
            filter);
    }

    [Fact]
    public void BuildAudioFilter_ForNonShorts_IsNull()
    {
        Assert.Null(EncodingService.BuildAudioFilter(trimTargetSeconds: null));
    }

    [Fact]
    public void BuildAudioFilter_ForStrongShortsTrim_Uses57SecondTarget()
    {
        var filter = EncodingService.BuildAudioFilter(trimTargetSeconds: 57.0);

        Assert.Equal(
            "afade=t=out:st=56:d=1,silenceremove=stop_periods=1:stop_duration=0.25:stop_threshold=-50dB",
            filter);
    }
}
