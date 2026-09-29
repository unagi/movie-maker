using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using MovieMaker.Models;
using MovieMaker.Services;
using Xunit;

namespace MovieMaker.Tests;

[Collection("ProcessEnvironment")]
public sealed class EncodingServiceIntegrationTests
{
    [Fact]
    public async Task Shorts_UsesOnePortraitImageAndPublishesOnlyVerifiedOutput()
    {
        var ffmpeg = EncodingService.ResolveFfmpegPath();
        Assert.NotNull(ffmpeg);
        using var workspace = new Workspace();
        var audio = await workspace.CreateAudioAsync(ffmpeg!, "audio.wav", 1.5);
        var image = workspace.CreateImage("portrait.png", 90, 160);
        var output = Path.Combine(workspace.Root, "short.mp4");
        var request = new EncodeRequest(ffmpeg, image, [audio], output,
            VideoOrientation.Vertical, EncodeProfile.CopyrightCheckProduction,
            Path.Combine(workspace.Root, "short.log"), DraftAudioQuality.High,
            ShortsMaximumSeconds: 4, NormalTextOverlayEnabled: false);

        var result = await EncodingService.EncodeAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(File.Exists(output));
        Assert.Equal(32000, (await EncodingService.GetAudioInfoAsync(output, ffmpeg))?.SampleRate);
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.Root, ".movie-maker-*"));
    }

    [Fact]
    public async Task Shorts_RejectsAudioBeyondMaximumWithoutPublishingOutput()
    {
        var ffmpeg = EncodingService.ResolveFfmpegPath();
        Assert.NotNull(ffmpeg);
        using var workspace = new Workspace();
        var audio = await workspace.CreateAudioAsync(ffmpeg!, "audio.wav", 2.0);
        var image = workspace.CreateImage("portrait.png", 90, 160);
        var output = Path.Combine(workspace.Root, "short.mp4");
        var request = new EncodeRequest(ffmpeg, image, [audio], output,
            VideoOrientation.Vertical, EncodeProfile.CopyrightCheckProduction,
            Path.Combine(workspace.Root, "short.log"), DraftAudioQuality.High,
            ShortsMaximumSeconds: 1, NormalTextOverlayEnabled: false);

        var result = await EncodingService.EncodeAsync(request);

        Assert.False(result.Success);
        Assert.False(File.Exists(output));
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.Root, ".movie-maker-*"));
    }

    [Fact]
    public async Task Shorts_ExistingOutputIsPreservedWhenPublishingFails()
    {
        var ffmpeg = EncodingService.ResolveFfmpegPath();
        Assert.NotNull(ffmpeg);
        using var workspace = new Workspace();
        var audio = await workspace.CreateAudioAsync(ffmpeg!, "audio.wav", 1.5);
        var image = workspace.CreateImage("portrait.png", 90, 160);
        var output = Path.Combine(workspace.Root, "short.mp4");
        var existing = new byte[] { 1, 2, 3, 4 };
        await File.WriteAllBytesAsync(output, existing);
        var request = new EncodeRequest(ffmpeg, image, [audio], output,
            VideoOrientation.Vertical, EncodeProfile.CopyrightCheckProduction,
            Path.Combine(workspace.Root, "short.log"), DraftAudioQuality.High,
            ShortsMaximumSeconds: 4, NormalTextOverlayEnabled: false);

        var result = await EncodingService.EncodeAsync(request);

        Assert.False(result.Success);
        Assert.Equal(existing, await File.ReadAllBytesAsync(output));
        Assert.DoesNotContain("Success:", await File.ReadAllTextAsync(request.LogPath));
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.Root, ".movie-maker-*"));
    }

    [Fact]
    public async Task NormalWithoutOverlay_UsesStandardPerTrackPath()
    {
        var ffmpeg = EncodingService.ResolveFfmpegPath();
        Assert.NotNull(ffmpeg);
        using var workspace = new Workspace();
        var audio = await workspace.CreateAudioAsync(ffmpeg!, "audio.wav", 1.5);
        var image = workspace.CreateImage("square.png", 90, 90);
        var output = Path.Combine(workspace.Root, "normal.mp4");
        var request = new EncodeRequest(ffmpeg, image, [audio], output,
            VideoOrientation.Horizontal, EncodeProfile.Standard,
            Path.Combine(workspace.Root, "normal.log"), DraftAudioQuality.High,
            TrackNormalizationTargets: [new LoudnessNormalizationTarget(-14, -1)],
            NormalTextOverlayEnabled: false);

        var result = await EncodingService.EncodeAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(File.Exists(output));
        Assert.Equal(48000, (await EncodingService.GetAudioInfoAsync(output, ffmpeg))?.SampleRate);
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.Root, ".movie-maker-*"));
    }

    [Fact]
    public async Task NormalOverlay_RejectsPortraitReplacementOfRegisteredBackground()
    {
        var ffmpeg = EncodingService.ResolveFfmpegPath();
        Assert.NotNull(ffmpeg);
        using var workspace = new Workspace();
        var audio = await workspace.CreateAudioAsync(ffmpeg!, "audio.wav", 1.5);
        var sourceImage = workspace.CreateImage("source.png", 160, 90);
        var renderedTrackImage = workspace.CreateImage("track.png", 1920, 1080);
        var replacement = workspace.CreateImage("replacement.png", 90, 160);
        File.Copy(replacement, sourceImage, overwrite: true);
        var output = Path.Combine(workspace.Root, "normal.mp4");
        var request = new EncodeRequest(ffmpeg, renderedTrackImage, [audio], output,
            VideoOrientation.Horizontal, EncodeProfile.Standard,
            Path.Combine(workspace.Root, "normal.log"), DraftAudioQuality.High,
            TrackImagePaths: [renderedTrackImage],
            TrackNormalizationTargets: [new LoudnessNormalizationTarget(-14, -1)],
            NormalTextOverlayEnabled: true,
            SourceImagePath: sourceImage);

        var result = await EncodingService.EncodeAsync(request);

        Assert.False(result.Success);
        Assert.Contains("通常動画", result.ErrorMessage);
        Assert.False(File.Exists(output));
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.Root, ".movie-maker-*"));
    }

    [Fact]
    public async Task Draft_InvalidShortsSettingsDoNotBlockEncoding()
    {
        var ffmpeg = EncodingService.ResolveFfmpegPath();
        Assert.NotNull(ffmpeg);
        using var workspace = new Workspace();
        var audio = await workspace.CreateAudioAsync(ffmpeg!, "audio.wav", 1.5);
        var image = workspace.CreateImage("landscape.png", 160, 90);
        var output = Path.Combine(workspace.Root, "draft.mp4");
        var request = new EncodeRequest(ffmpeg, image, [audio], output,
            VideoOrientation.Horizontal, EncodeProfile.DraftPreview,
            Path.Combine(workspace.Root, "draft.log"), DraftAudioQuality.High,
            ShortsMaximumSeconds: 181, NormalTextOverlayEnabled: false,
            OneMinuteShortsOffsetSeconds: 59.5);

        var result = await EncodingService.EncodeAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(File.Exists(output));
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.Root, ".movie-maker-*"));
    }

    private sealed class Workspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "MovieMakerEncodingTests", Guid.NewGuid().ToString("N"));

        public Workspace() => Directory.CreateDirectory(Root);

        public string CreateImage(string name, int width, int height)
        {
            var path = Path.Combine(Root, name);
            using var bitmap = new Bitmap(width, height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.DarkBlue);
            bitmap.Save(path, ImageFormat.Png);
            return path;
        }

        public async Task<string> CreateAudioAsync(string ffmpeg, string name, double duration)
        {
            var path = Path.Combine(Root, name);
            var psi = new ProcessStartInfo(ffmpeg)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            foreach (var argument in new[] { "-y", "-f", "lavfi", "-i",
                         $"sine=frequency=440:duration={duration.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                         "-c:a", "pcm_s16le", path })
                psi.ArgumentList.Add(argument);
            using var process = Process.Start(psi)!;
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, await stderr);
            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (IOException) { }
        }
    }
}
