using MovieMaker.Services;
using MovieMaker.ViewModels;
using Xunit;

namespace MovieMaker.Tests;

public sealed class LoudnessAnalysisViewModelTests
{
    [Fact]
    public async Task AddFilesAsync_AddsMultipleVideosInDropOrder_AndSkipsDuplicatesAndUnsupportedFiles()
    {
        using var workspace = new TestWorkspace();
        var first = workspace.CreateFile("first.mp4");
        var second = workspace.CreateFile("second.mov");
        var unsupported = workspace.CreateFile("notes.txt");
        var loudness = new AudioLoudnessResult(
            -14.0,
            AudioLoudnessStatus.WithinTarget,
            -2.0,
            4.5,
            AudioTruePeakStatus.WithinTarget);
        var viewModel = new LoudnessAnalysisViewModel(_ => Task.FromResult<AudioLoudnessResult?>(loudness));

        await viewModel.AddFilesAsync(new[] { first, second, first, unsupported });

        Assert.Equal(new[] { first, second }, viewModel.Items.Select(item => item.Path));
        Assert.All(viewModel.Items, item => Assert.True(item.IsAnalysisComplete));
        Assert.False(viewModel.HasWarnings);
        Assert.Contains("非対応", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("重複", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddFilesAsync_MarksLoudnessAndTruePeakWarnings()
    {
        using var workspace = new TestWorkspace();
        var video = workspace.CreateFile("too-loud.mp4");
        var loudness = new AudioLoudnessResult(
            -10.5,
            AudioLoudnessStatus.TooLoud,
            -0.4,
            12.3,
            AudioTruePeakStatus.TooHigh);
        var viewModel = new LoudnessAnalysisViewModel(_ => Task.FromResult<AudioLoudnessResult?>(loudness));

        await viewModel.AddFilesAsync(new[] { video });

        var item = Assert.Single(viewModel.Items);
        Assert.True(item.IsWarning);
        Assert.True(item.IsTruePeakWarning);
        Assert.Contains("-10.5 LUFS", item.LoudnessText, StringComparison.Ordinal);
        Assert.Contains("-0.4 dBTP", item.TruePeakText, StringComparison.Ordinal);
        Assert.Contains("12.3 LU", item.LoudnessRangeText, StringComparison.Ordinal);
        Assert.Contains("要確認", viewModel.SummaryText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddFilesAsync_MarksTruePeakOnlyWarningInOverallJudgement()
    {
        using var workspace = new TestWorkspace();
        var video = workspace.CreateFile("true-peak-only.mp4");
        var loudness = new AudioLoudnessResult(
            -14.0,
            AudioLoudnessStatus.WithinTarget,
            -0.5,
            5.0,
            AudioTruePeakStatus.TooHigh);
        var viewModel = new LoudnessAnalysisViewModel(_ => Task.FromResult<AudioLoudnessResult?>(loudness));

        await viewModel.AddFilesAsync(new[] { video });

        var item = Assert.Single(viewModel.Items);
        Assert.True(item.IsWarning);
        Assert.True(item.IsTruePeakWarning);
        Assert.True(viewModel.HasWarnings);
        Assert.Contains("要確認", viewModel.SummaryText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddFilesAsync_MarksAnalysisUnavailableWhenAnalyzerReturnsNull()
    {
        using var workspace = new TestWorkspace();
        var video = workspace.CreateFile("no-audio.mp4");
        var viewModel = new LoudnessAnalysisViewModel(_ => Task.FromResult<AudioLoudnessResult?>(null));

        await viewModel.AddFilesAsync(new[] { video });

        var item = Assert.Single(viewModel.Items);
        Assert.True(item.IsAnalysisComplete);
        Assert.False(item.IsAnalysisAvailable);
        Assert.Contains("解析不可", item.StatusText, StringComparison.Ordinal);
        Assert.Contains("解析不可", viewModel.SummaryText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("movie.mp4", true)]
    [InlineData("movie.mkv", true)]
    [InlineData("movie.webm", true)]
    [InlineData("sound.mp3", false)]
    [InlineData("notes.txt", false)]
    public void IsSupportedVideoFile_RecognizesVideoExtensions(string fileName, bool expected)
    {
        Assert.Equal(expected, LoudnessAnalysisViewModel.IsSupportedVideoFile(fileName));
    }

    private sealed class TestWorkspace : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "MovieMakerTests", Guid.NewGuid().ToString("N"));

        public TestWorkspace()
        {
            Directory.CreateDirectory(_root);
        }

        public string CreateFile(string name)
        {
            var path = Path.Combine(_root, name);
            File.WriteAllText(path, "test");
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch
            {
                // ignore cleanup failures
            }
        }
    }
}
