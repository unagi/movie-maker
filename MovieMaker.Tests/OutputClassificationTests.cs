using MovieMaker.Services;
using Xunit;

namespace MovieMaker.Tests;

public sealed class OutputClassificationTests
{
    [Theory]
    [InlineData(1080, 1920, 0, null, OutputKind.Shorts, false)]
    [InlineData(1080, 1920, 1, 60.0, OutputKind.Shorts, true)]
    [InlineData(1080, 1920, 1, 60.001, OutputKind.InputError, false)]
    [InlineData(1080, 1920, 2, 20.0, OutputKind.InputError, false)]
    [InlineData(1080, 1080, 1, 40.0, OutputKind.Normal, true)]
    [InlineData(1920, 1080, 2, 40.0, OutputKind.Normal, true)]
    [InlineData(0, 0, 1, 40.0, OutputKind.AwaitingImage, false)]
    public void Classify_UsesImageDirectionAndShortsLimit(int width, int height, int tracks,
        double? duration, OutputKind expectedKind, bool expectedCanEncode)
    {
        var result = OutputClassificationService.Classify(width, height, tracks, duration, 60);

        Assert.Equal(expectedKind, result.Kind);
        Assert.Equal(expectedCanEncode, result.CanEncodeInputs);
    }

    [Fact]
    public void Classify_ShortHorizontalTotal_ShowsNormalLengthWarning()
    {
        var result = OutputClassificationService.Classify(1920, 1080, 2, 59, 60);

        Assert.True(result.HasNormalLengthWarning);
    }

    [Fact]
    public void Classify_InvalidMaximum_DisablesEncoding()
    {
        var result = OutputClassificationService.Classify(1080, 1920, 1, 40, 181);

        Assert.Equal(OutputKind.InputError, result.Kind);
        Assert.False(result.CanEncodeInputs);
    }

    [Fact]
    public void Classify_AnalysisFailure_IsErrorButPendingAnalysisIsNot()
    {
        var pending = OutputClassificationService.Classify(1080, 1920, 1, null, 60);
        var failed = OutputClassificationService.Classify(1080, 1920, 1, null, 60, audioAnalysisFailed: true);

        Assert.Equal(OutputKind.Shorts, pending.Kind);
        Assert.False(pending.CanEncodeInputs);
        Assert.Equal(OutputKind.InputError, failed.Kind);
        Assert.False(failed.CanEncodeInputs);
    }

    [Fact]
    public void Classify_PortraitConflictResolvesImmediatelyWhenTrackCountFalls()
    {
        Assert.Equal(OutputKind.InputError,
            OutputClassificationService.Classify(1080, 1920, 2, 30, 60).Kind);
        Assert.Equal(OutputKind.Shorts,
            OutputClassificationService.Classify(1080, 1920, 1, 30, 60).Kind);
    }

    [Theory]
    [InlineData(0, null, "入力待ち")]
    [InlineData(1, null, "判定中")]
    [InlineData(1, 60.0, "Shorts候補・画像待ち")]
    [InlineData(1, 60.001, "通常動画候補・画像待ち")]
    [InlineData(2, null, "通常動画候補・画像待ち")]
    public void Classify_WithoutImageReportsCandidateFromAudio(int tracks, double? duration, string message)
    {
        var result = OutputClassificationService.Classify(0, 0, tracks, duration, 60);

        Assert.Equal(OutputKind.AwaitingImage, result.Kind);
        Assert.False(result.CanEncodeInputs);
        Assert.Equal(message, result.Message);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Classify_AnalyzedInvalidDurationIsInputError(double duration)
    {
        foreach (var (width, height) in new[] { (0, 0), (1080, 1920), (1920, 1080) })
        {
            var result = OutputClassificationService.Classify(width, height, 1, duration, 60);
            Assert.Equal(OutputKind.InputError, result.Kind);
            Assert.False(result.CanEncodeInputs);
            Assert.Contains("音声の長さ", result.Message);
        }
    }
}
