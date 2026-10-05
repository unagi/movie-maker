using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using MovieMaker.Models;
using MovieMaker.Services;
using Xunit;

namespace MovieMaker.Tests;

public class EncodingServiceTests
{
    [Theory]
    [InlineData(EncodeProfile.Standard, 1, false, false)]
    [InlineData(EncodeProfile.Standard, 2, false, true)]
    [InlineData(EncodeProfile.Standard, 3, true, false)]
    [InlineData(EncodeProfile.CopyrightCheckProduction, 2, false, false)]
    [InlineData(EncodeProfile.DraftPreview, 2, false, false)]
    public void ShouldApplyTrackNormalization_UsesProfileCountAndSkipFlag(
        EncodeProfile profile, int trackCount, bool skip, bool expected)
    {
        Assert.Equal(expected, EncodingService.ShouldApplyTrackNormalization(profile, trackCount, skip));
    }

    [Theory]
    [InlineData(-20, -10, 6)]
    [InlineData(-20, -3, 2)]
    [InlineData(-20, -7, 6)]
    [InlineData(-20, -7.1, 6)]
    [InlineData(-20, -6.9, 5.9)]
    [InlineData(-10, -1, -4)]
    [InlineData(-10, 4, -5)]
    [InlineData(-14, -3, 0)]
    [InlineData(-14, -1, 0)]
    [InlineData(-14, 0, -1)]
    public void CalculateNormalizationGain_UsesTheMoreRestrictiveTarget(
        double inputLufs, double inputTruePeakDbtp, double expectedGain)
    {
        var gain = InvokePrivate<double>("CalculateNormalizationGain",
            new LoudnessNormalizationMeasurements(inputLufs, inputTruePeakDbtp, 0, 0),
            new LoudnessNormalizationTarget(-14, -1));

        Assert.Equal(expectedGain, gain, 6);
    }

    [Theory]
    [InlineData(double.NaN, -1, -14, -1)]
    [InlineData(-20, double.PositiveInfinity, -14, -1)]
    [InlineData(-20, -3, double.NegativeInfinity, -1)]
    [InlineData(-20, -3, -14, double.NaN)]
    public void CalculateNormalizationGain_RejectsNonFiniteMeasurementsAndTargets(
        double inputLufs, double inputTruePeakDbtp, double targetLufs, double targetTruePeakDbtp)
    {
        var exception = Assert.Throws<TargetInvocationException>(() => InvokePrivate<double>(
            "CalculateNormalizationGain",
            new LoudnessNormalizationMeasurements(inputLufs, inputTruePeakDbtp, 0, 0),
            new LoudnessNormalizationTarget(targetLufs, targetTruePeakDbtp)));

        Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
    }

    [Theory]
    [InlineData(-1.5, 0)]
    [InlineData(-1.01, 0)]
    [InlineData(-1, 0)]
    [InlineData(-0.99, 0.01)]
    [InlineData(-0.6, 0.4)]
    [InlineData(0, 1)]
    [InlineData(0.3, 1.3)]
    public void CalculatePeakCorrection_OnlyReducesGainForTargetExceeded(
        double measuredTruePeakDbtp, double expectedCorrection)
    {
        var correction = InvokePrivate<double>("CalculatePeakCorrection", measuredTruePeakDbtp, -1d);

        Assert.Equal(expectedCorrection, correction, 6);
    }

    [Theory]
    [InlineData(double.NaN, -1)]
    [InlineData(-1, double.PositiveInfinity)]
    public void CalculatePeakCorrection_RejectsNonFiniteValues(
        double measuredTruePeakDbtp, double targetTruePeakDbtp)
    {
        var exception = Assert.Throws<TargetInvocationException>(() => InvokePrivate<double>(
            "CalculatePeakCorrection", measuredTruePeakDbtp, targetTruePeakDbtp));

        Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
    }

    [Fact]
    public void BuildStandardTrackFilter_UsesOnlyFixedPerTrackVolumeWhenEnabled()
    {
        var enabled = InvokePrivate<string>("BuildStandardTrackFilter",
            new double[] { 1, 2, 3 }, CreateEncodingOptions(), new double[] { 2, -4, 0 }, true);
        var disabled = InvokePrivate<string>("BuildStandardTrackFilter",
            new double[] { 1, 2, 3 }, CreateEncodingOptions(), new double[] { 2, -4, 0 }, false);

        Assert.Contains("volume=2dB", enabled);
        Assert.Contains("volume=-4dB", enabled);
        Assert.Contains("volume=0dB", enabled);
        Assert.DoesNotContain("loudnorm", enabled, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("acompressor", enabled, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("limiter", enabled, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dynaudnorm", enabled, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("volume=", disabled, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildStartInfo_ReusesOriginalAudioInputsForInitialAndCorrectedFixedGainPasses()
    {
        var request = new EncodeRequest("ffmpeg.exe", "cover.png", ["original-1.flac", "original-2.flac"],
            "encoded.mp4", VideoOrientation.Horizontal, EncodeProfile.Standard, "encode.log",
            DraftAudioQuality.High,
            TrackNormalizationTargets: [new LoudnessNormalizationTarget(-14, -1),
                new LoudnessNormalizationTarget(-18, -1)],
            NormalTextOverlayEnabled: false);

        AssertBuildArgs([2, 5], ["volume=2dB", "volume=5dB"]);
        AssertBuildArgs([1.6, 4.6], ["volume=1.6dB", "volume=4.6dB"]);

        void AssertBuildArgs(IReadOnlyList<double> gains, IReadOnlyList<string> expectedVolumes)
        {
            var args = InvokeBuildStartInfo(request, gains).ArgumentList.ToArray();
            var inputPaths = args.Select((argument, index) => (argument, index))
                .Where(item => item.argument == "-i")
                .Select(item => args[item.index + 1])
                .ToArray();
            var filterIndex = Array.IndexOf(args, "-filter_complex");
            var logLevelIndex = Array.IndexOf(args, "-loglevel");

            Assert.Equal(new[] { "cover.png", "original-1.flac", "cover.png", "original-2.flac" }, inputPaths);
            Assert.Equal("encoded.mp4", args[^1]);
            Assert.DoesNotContain("encoded.mp4", inputPaths);
            var audioCodecIndex = Array.IndexOf(args, "-c:a");
            Assert.True(audioCodecIndex >= 0);
            Assert.Equal("aac", args[audioCodecIndex + 1]);
            Assert.True(logLevelIndex >= 0 && logLevelIndex < filterIndex);
            Assert.Equal("verbose", args[logLevelIndex + 1]);
            Assert.True(filterIndex >= 0);
            var filter = args[filterIndex + 1];
            foreach (var volume in expectedVolumes)
            {
                Assert.Contains(volume, filter);
            }
            Assert.DoesNotContain("loudnorm", filter, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(-1.5, false, 0)]
    [InlineData(-1.01, false, 0)]
    [InlineData(-1, false, 0)]
    [InlineData(-0.99, true, 0.01)]
    [InlineData(-0.6, true, 0.4)]
    [InlineData(0, true, 1)]
    [InlineData(0.3, true, 1.3)]
    public async Task ApplyPeakCorrectionAsync_RegeneratesOnlyWhenPeakExceedsTarget(
        double measuredPeak, bool shouldRegenerate, double expectedCorrection)
    {
        var generatedGains = new List<IReadOnlyList<double>>();
        var measureCount = 0;
        var logs = new List<string>();

        var error = await InvokePeakCorrectionAsync([2], [new LoudnessNormalizationTarget(-14, -1)],
            gains =>
            {
                generatedGains.Add(gains.ToArray());
                return Task.FromResult(true);
            },
            () =>
            {
                measureCount++;
                var peak = measureCount == 1 ? measuredPeak : -1;
                return Task.FromResult<IReadOnlyList<AudioLoudnessResult>?>([CreateLoudness(-14, peak)]);
            }, logs.Add);

        Assert.Null(error);
        Assert.Equal(shouldRegenerate ? 2 : 1, generatedGains.Count);
        Assert.Equal(shouldRegenerate ? 2 : 1, measureCount);
        if (shouldRegenerate)
        {
            Assert.Equal(2 - expectedCorrection, generatedGains[1][0], 6);
        }
    }

    [Fact]
    public async Task ApplyPeakCorrectionAsync_UsesCorrectedGainsForSingleRetryAndLogsResidualPeak()
    {
        var generatedGains = new List<IReadOnlyList<double>>();
        var measuredPeaks = new Queue<double>([-0.6, 0.3]);
        var logs = new List<string>();

        var error = await InvokePeakCorrectionAsync([2], [new LoudnessNormalizationTarget(-14, -1)],
            gains =>
            {
                generatedGains.Add(gains.ToArray());
                return Task.FromResult(true);
            },
            () => Task.FromResult<IReadOnlyList<AudioLoudnessResult>?>(
                [CreateLoudness(-14, measuredPeaks.Dequeue())]), logs.Add);

        Assert.Null(error);
        Assert.Equal(2, generatedGains.Count);
        Assert.Equal(2, generatedGains[0][0], 6);
        Assert.Equal(1.6, generatedGains[1][0], 6);
        Assert.Contains(logs, line => line.Contains("0.3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplyPeakCorrectionAsync_CorrectsOnlyTracksThatExceedTheirIndividualTargets()
    {
        var generatedGains = new List<IReadOnlyList<double>>();
        var measureCount = 0;
        var targets = new[]
        {
            new LoudnessNormalizationTarget(-14, -1),
            new LoudnessNormalizationTarget(-18, -1)
        };

        var error = await InvokePeakCorrectionAsync([2, 5], targets,
            gains =>
            {
                generatedGains.Add(gains.ToArray());
                return Task.FromResult(true);
            },
            () =>
            {
                measureCount++;
                return Task.FromResult<IReadOnlyList<AudioLoudnessResult>?>(measureCount == 1
                    ? [CreateLoudness(-14, -1), CreateLoudness(-18, -0.6)]
                    : [CreateLoudness(-14, -1), CreateLoudness(-18, -1)]);
            }, _ => { });

        Assert.Null(error);
        Assert.Equal(2, generatedGains.Count);
        Assert.Equal(new[] { 2d, 5d }, generatedGains[0]);
        Assert.Equal(new[] { 2d, 4.6d }, generatedGains[1]);
    }

    [Fact]
    public async Task ApplyPeakCorrectionAsync_DoesNotIncreaseGainToReachIntegratedLoudnessTarget()
    {
        var generatedGains = new List<IReadOnlyList<double>>();

        var error = await InvokePeakCorrectionAsync([0], [new LoudnessNormalizationTarget(-14, -1)],
            gains =>
            {
                generatedGains.Add(gains.ToArray());
                return Task.FromResult(true);
            },
            () => Task.FromResult<IReadOnlyList<AudioLoudnessResult>?>(
                [CreateLoudness(-20, -10)]), _ => { });

        Assert.Null(error);
        Assert.Single(generatedGains);
        Assert.Equal(0, generatedGains[0][0]);
    }

    [Fact]
    public async Task ApplyPeakCorrectionAsync_GenerationFailureReturnsErrorWithoutMeasuring()
    {
        var measureCount = 0;

        var error = await InvokePeakCorrectionAsync([0], [new LoudnessNormalizationTarget(-14, -1)],
            _ => Task.FromResult(false),
            () =>
            {
                measureCount++;
                return Task.FromResult<IReadOnlyList<AudioLoudnessResult>?>([CreateLoudness(-14, -1)]);
            }, _ => { });

        Assert.NotNull(error);
        Assert.Equal(0, measureCount);
    }

    [Fact]
    public async Task ApplyPeakCorrectionAsync_InvalidOrIncompleteMeasurementsReturnError()
    {
        var nullMeasurement = await InvokePeakCorrectionAsync([0], [new LoudnessNormalizationTarget(-14, -1)],
            _ => Task.FromResult(true),
            () => Task.FromResult<IReadOnlyList<AudioLoudnessResult>?>(null), _ => { });
        var wrongCount = await InvokePeakCorrectionAsync([0, 0],
            [new LoudnessNormalizationTarget(-14, -1), new LoudnessNormalizationTarget(-14, -1)],
            _ => Task.FromResult(true),
            () => Task.FromResult<IReadOnlyList<AudioLoudnessResult>?>([CreateLoudness(-14, -1)]), _ => { });
        var invalidValue = await InvokePeakCorrectionAsync([0], [new LoudnessNormalizationTarget(-14, -1)],
            _ => Task.FromResult(true),
            () => Task.FromResult<IReadOnlyList<AudioLoudnessResult>?>(
                [CreateLoudness(double.NaN, -1)]), _ => { });
        var invalidTruePeak = await InvokePeakCorrectionAsync([0],
            [new LoudnessNormalizationTarget(-14, -1)],
            _ => Task.FromResult(true),
            () => Task.FromResult<IReadOnlyList<AudioLoudnessResult>?>(
                [CreateLoudness(-14, double.PositiveInfinity)]), _ => { });

        Assert.NotNull(nullMeasurement);
        Assert.NotNull(wrongCount);
        Assert.NotNull(invalidValue);
        Assert.NotNull(invalidTruePeak);
    }

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

    private static T InvokePrivate<T>(string methodName, params object?[] arguments)
    {
        var method = typeof(EncodingService).GetMethod(methodName,
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (T)method!.Invoke(null, arguments)!;
    }

    private static ProcessStartInfo InvokeBuildStartInfo(EncodeRequest request,
        IReadOnlyList<double> gains)
    {
        var encoderField = typeof(EncodingService).GetField("LibX264",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(encoderField);
        var buildStartInfo = typeof(EncodingService).GetMethod("BuildStartInfo",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(buildStartInfo);

        return (ProcessStartInfo)buildStartInfo!.Invoke(null,
            [request, encoderField!.GetValue(null), null, new double[] { 4, 6 }, gains])!;
    }

    private static EncodingOptions CreateEncodingOptions() => new(
        1920, 1080, 30, "192k", "48000", "medium", 20, "p4", 20,
        "balanced", 20, "quality", 20);

    private static AudioLoudnessResult CreateLoudness(double integratedLufs, double truePeakDbtp) =>
        new(integratedLufs, EncodingService.ClassifyIntegratedLoudness(integratedLufs).Status, truePeakDbtp);

    private static async Task<string?> InvokePeakCorrectionAsync(
        IReadOnlyList<double> initialGainsDb,
        IReadOnlyList<LoudnessNormalizationTarget> targets,
        Func<IReadOnlyList<double>, Task<bool>> generate,
        Func<Task<IReadOnlyList<AudioLoudnessResult>?>> measure,
        Action<string> log)
    {
        var method = typeof(EncodingService).GetMethod("ApplyPeakCorrectionAsync",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = (Task<string?>)method!.Invoke(null,
            [initialGainsDb, targets, generate, measure, log])!;
        return await task;
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
