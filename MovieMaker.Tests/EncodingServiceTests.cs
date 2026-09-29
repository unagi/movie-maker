using System.Reflection;
using System.Text.Json;
using MovieMaker.Models;
using MovieMaker.Services;
using Xunit;

namespace MovieMaker.Tests;

public class EncodingServiceTests
{
    [Fact]
    public void ValidateProductionInput_RejectsContradictoryImageDirection()
    {
        Assert.False(EncodingService.ValidateProductionInput(1080, 1920, 1,
            EncodeProfile.Standard, 60, 50, out _));
        Assert.False(EncodingService.ValidateProductionInput(1920, 1080, 1,
            EncodeProfile.CopyrightCheckProduction, 60, 50, out _));
        Assert.False(EncodingService.ValidateProductionInput(1080, 1920, 2,
            EncodeProfile.CopyrightCheckProduction, 60, 50, out _));
        Assert.True(EncodingService.ValidateProductionInput(1080, 1920, 1,
            EncodeProfile.CopyrightCheckProduction, 60, 50, out _));
    }

    [Fact]
    public void ShortsOutputLimit_RejectsStartTimeOffsetBeyondMaximum()
    {
        const string json = "{\"format\":{\"start_time\":\"0\",\"duration\":\"60\"}," +
            "\"streams\":[{\"codec_type\":\"video\",\"start_time\":\"2\",\"duration\":\"59\"}," +
            "{\"codec_type\":\"audio\",\"start_time\":\"0\",\"duration\":\"60\"}]}";

        Assert.False(CheckShortsProbeJson(json, 60));
    }

    [Fact]
    public void ShortsOutputLimit_RejectsContainerEndBeyondMaximum()
    {
        const string json = "{\"format\":{\"start_time\":\"2\",\"duration\":\"59\"}," +
            "\"streams\":[{\"codec_type\":\"video\",\"start_time\":\"0\",\"duration\":\"60\"}," +
            "{\"codec_type\":\"audio\",\"start_time\":\"0\",\"duration\":\"60\"}]}";

        Assert.False(CheckShortsProbeJson(json, 60));
    }

    [Theory]
    [InlineData("{\"duration\":\"60\"}", "{\"start_time\":\"0\",\"duration\":\"60\"}")]
    [InlineData("{\"start_time\":\"0\",\"duration\":\"60\"}", "{\"start_time\":\"0\"}")]
    public void ShortsOutputLimit_RejectsMissingTimingData(string format, string video)
    {
        var videoWithCodec = video.Insert(1, "\"codec_type\":\"video\",");
        var json = "{\"format\":" + format + ",\"streams\":[" + videoWithCodec + "," +
            "{\"codec_type\":\"audio\",\"start_time\":\"0\",\"duration\":\"60\"}]}";

        Assert.False(CheckShortsProbeJson(json, 60));
    }

    [Fact]
    public void ShortsOutputLimit_AcceptsKnownEndTimesAtMaximum()
    {
        const string json = "{\"format\":{\"end_time\":\"60\"}," +
            "\"streams\":[{\"codec_type\":\"video\",\"end_time\":\"60\"}," +
            "{\"codec_type\":\"audio\",\"start_time\":\"0\",\"duration\":\"60\"}]}";

        Assert.True(CheckShortsProbeJson(json, 60));
    }

    private static bool CheckShortsProbeJson(string json, int limit)
    {
        using var document = JsonDocument.Parse(json);
        var method = typeof(EncodingService).GetMethod("IsShortsOutputWithinLimit",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (bool)method!.Invoke(null, [document.RootElement, limit])!;
    }
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
    public void BuildAudioFilter_PreservesSubMillisecondSafeTarget()
    {
        var filter = EncodingService.BuildAudioFilter(trimTargetSeconds: 60.0001);

        Assert.Contains("afade=t=out:st=59.0001:d=1", filter);
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
