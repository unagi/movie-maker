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

    [Fact]
    public void BuildAudioFilterComplex_ForMultipleInputs_ConcatsOnly()
    {
        var filter = EncodingService.BuildAudioFilterComplex(audioInputCount: 3, trimTargetSeconds: null);

        Assert.Equal(
            "[1:a][2:a][3:a]concat=n=3:v=0:a=1[aout]",
            filter);
    }

    [Fact]
    public void BuildAudioFilterComplex_ForMultipleInputsWithTrim_ConcatsThenAppliesFade()
    {
        var filter = EncodingService.BuildAudioFilterComplex(audioInputCount: 2, trimTargetSeconds: 57.0);

        Assert.Equal(
            "[1:a][2:a]concat=n=2:v=0:a=1[a_concat];[a_concat]afade=t=out:st=56:d=1,silenceremove=stop_periods=1:stop_duration=0.25:stop_threshold=-50dB[aout]",
            filter);
    }

    [Theory]
    [InlineData(-16.0, AudioLoudnessStatus.WithinTarget)]
    [InlineData(-14.0, AudioLoudnessStatus.WithinTarget)]
    [InlineData(-12.0, AudioLoudnessStatus.WithinTarget)]
    [InlineData(-16.1, AudioLoudnessStatus.TooQuiet)]
    [InlineData(-11.9, AudioLoudnessStatus.TooLoud)]
    public void ClassifyIntegratedLoudness_UsesMinus14LufsTarget(
        double integratedLufs,
        AudioLoudnessStatus expectedStatus)
    {
        var result = EncodingService.ClassifyIntegratedLoudness(integratedLufs);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(integratedLufs, result.IntegratedLufs);
    }

    [Fact]
    public void ClassifyIntegratedLoudness_TreatsNegativeInfinityAsTooQuiet()
    {
        var result = EncodingService.ClassifyIntegratedLoudness(double.NegativeInfinity);

        Assert.Equal(AudioLoudnessStatus.TooQuiet, result.Status);
        Assert.Equal(double.NegativeInfinity, result.IntegratedLufs);
    }

    [Theory]
    [InlineData(-1.0, AudioTruePeakStatus.WithinTarget)]
    [InlineData(-1.1, AudioTruePeakStatus.WithinTarget)]
    [InlineData(-0.9, AudioTruePeakStatus.TooHigh)]
    public void ClassifyTruePeak_UsesMinus1DbtpWarningLimit(
        double truePeakDbtp,
        AudioTruePeakStatus expectedStatus)
    {
        Assert.Equal(expectedStatus, EncodingService.ClassifyTruePeak(truePeakDbtp));
    }

    [Fact]
    public void ParseAudioLoudnessOutput_ReadsInputIntegratedLoudnessFromJson()
    {
        var result = EncodingService.ParseAudioLoudnessOutput(
            "ffmpeg output\n{\"input_i\":\"-14.25\",\"input_tp\":\"-1.00\",\"input_lra\":\"5.40\"}\n");

        Assert.NotNull(result);
        Assert.Equal(-14.25, result!.IntegratedLufs);
        Assert.Equal(AudioLoudnessStatus.WithinTarget, result.Status);
        Assert.Equal(-1.0, result.TruePeakDbtp);
        Assert.Equal(5.4, result.LoudnessRangeLu);
        Assert.Equal(AudioTruePeakStatus.WithinTarget, result.TruePeakStatus);
    }

    [Fact]
    public void ParseAudioLoudnessOutput_ReadsNegativeInfinityAsTooQuiet()
    {
        var result = EncodingService.ParseAudioLoudnessOutput(
            "{\"input_i\":\"-inf\",\"input_tp\":\"-2.00\",\"input_lra\":\"0.00\"}");

        Assert.NotNull(result);
        Assert.Equal(double.NegativeInfinity, result!.IntegratedLufs);
        Assert.Equal(AudioLoudnessStatus.TooQuiet, result.Status);
    }
}
