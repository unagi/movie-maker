using MovieMaker.Models;
using MovieMaker.ViewModels;
using Xunit;

namespace MovieMaker.Tests;

public sealed class AudioTrackItemUiStateTests
{
    [Fact]
    public void AnalysisState_UsesOneMessageUntilMeasurementsAreAvailable()
    {
        var track = new AudioTrackItem("song.wav");

        Assert.Equal("音量解析中", track.AudioMeasurementStateText);
        Assert.False(track.HasAudioMeasurements);
        Assert.Equal(string.Empty, track.LoudnessText);
        Assert.Equal(string.Empty, track.TruePeakText);
        Assert.Equal(string.Empty, track.LoudnessRangeText);

        track.ApplyLoudnessAnalysis(null, null, null);

        Assert.Equal("音量解析不可", track.AudioMeasurementStateText);
        Assert.False(track.HasAudioMeasurements);
        Assert.False(track.HasAudioReviewWarning);
        Assert.Equal(string.Empty, track.LoudnessText);
        Assert.Equal(string.Empty, track.TruePeakText);
        Assert.Equal(string.Empty, track.LoudnessRangeText);
    }

    [Theory]
    [InlineData(-15.1, -1.0, "音量低め", "-15 LUFS未満")]
    [InlineData(-15.0, -1.0, "", "")]
    [InlineData(-12.0, -1.0, "", "")]
    [InlineData(-11.9, -1.0, "音量高め", "-12 LUFS超")]
    [InlineData(-14.0, -0.9, "TP超", "-1 dBTP超")]
    public void InputAudioReview_UsesReviewThresholdsWithoutMarkingNormalValues(
        double lufs, double truePeak, string expectedReason, string expectedDetail)
    {
        var track = new AudioTrackItem("song.wav");

        track.ApplyLoudnessAnalysis(lufs, truePeak, 5.0);

        Assert.Equal(expectedReason, track.AudioReviewReasonText);
        Assert.Equal(expectedReason.Length > 0, track.HasAudioReviewWarning);
        if (expectedDetail.Length > 0)
        {
            Assert.Contains(expectedDetail, track.AudioReviewDetailText, StringComparison.Ordinal);
        }
        Assert.True(track.HasAudioMeasurements);
        Assert.Equal(string.Empty, track.AudioMeasurementStateText);
        Assert.Contains("5.0 LU", track.LoudnessRangeText, StringComparison.Ordinal);
    }

    [Fact]
    public void InputAudioReview_CombinesDifferentReasonsWithoutRepeatingMeasurements()
    {
        var track = new AudioTrackItem("song.wav");

        track.ApplyLoudnessAnalysis(-16.0, -0.4, 3.0);

        Assert.Equal("音量低め・TP超", track.AudioReviewReasonText);
        Assert.Contains("-16.0 LUFS", track.AudioReviewDetailText, StringComparison.Ordinal);
        Assert.Contains("-0.4 dBTP", track.AudioReviewDetailText, StringComparison.Ordinal);
        Assert.Equal("-16.0 LUFS", track.LoudnessText);
        Assert.Equal("-0.4 dBTP", track.TruePeakText);
    }

    [Fact]
    public void NormalizationTargetSummary_ChangesWithOverrideState()
    {
        var track = new AudioTrackItem("song.wav");
        Assert.Equal("目標：共通", track.NormalizationTargetSummaryText);

        track.IsNormalizationOverrideEnabled = true;
        Assert.Equal("目標：個別", track.NormalizationTargetSummaryText);
    }

    [Fact]
    public void Guidance_DescribesProcessingForEachOutputModeWithoutGenericWarning()
    {
        var viewModel = new MainViewModel();
        Assert.Contains("出力時", viewModel.AudioLoudnessGuidanceText, StringComparison.Ordinal);
        Assert.Contains("各曲", viewModel.AudioLoudnessGuidanceText, StringComparison.Ordinal);
        Assert.DoesNotContain("注意", viewModel.AudioLoudnessGuidanceText, StringComparison.Ordinal);

        viewModel.UseDraftMode = true;
        Assert.Contains("音量調整なし", viewModel.AudioLoudnessGuidanceText, StringComparison.Ordinal);
    }
}
