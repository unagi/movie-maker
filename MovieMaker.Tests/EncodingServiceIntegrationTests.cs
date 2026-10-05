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
        var audio = await workspace.CreateFlacAudioAsync(ffmpeg!, "audio.flac", 4.0);
        var image = workspace.CreateImage("portrait.png", 90, 160);
        var output = Path.Combine(workspace.Root, "short.mp4");
        var request = new EncodeRequest(ffmpeg, image, [audio], output,
            VideoOrientation.Vertical, EncodeProfile.CopyrightCheckProduction,
            Path.Combine(workspace.Root, "short.log"), DraftAudioQuality.High,
            ShortsMaximumSeconds: 10, NormalTextOverlayEnabled: false);

        var result = await EncodingService.EncodeAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(File.Exists(output));
        var outputAudio = await EncodingService.GetAudioInfoAsync(output, ffmpeg);
        Assert.Equal("aac", outputAudio?.CodecName);
        Assert.Equal(48000, outputAudio?.SampleRate);
        var sourceLoudness = await EncodingService.GetAudioLoudnessAsync(audio, ffmpeg);
        var outputLoudness = await EncodingService.GetAudioLoudnessAsync(output, ffmpeg);
        Assert.NotNull(sourceLoudness);
        Assert.NotNull(outputLoudness);
        Assert.InRange(Math.Abs(outputLoudness!.IntegratedLufs - sourceLoudness!.IntegratedLufs), 0, 1.5);
        Assert.DoesNotContain("Loudness normalization input:", await File.ReadAllTextAsync(request.LogPath));
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
    public async Task NormalSingleTrack_DoesNotRequireNormalizationTargets()
    {
        var ffmpeg = EncodingService.ResolveFfmpegPath();
        Assert.NotNull(ffmpeg);
        using var workspace = new Workspace();
        var audio = await workspace.CreateAudioAsync(ffmpeg!, "audio.wav", 1.5);
        var image = workspace.CreateImage("square.png", 90, 90);
        var output = Path.Combine(workspace.Root, "normal-single.mp4");
        var request = new EncodeRequest(ffmpeg, image, [audio], output,
            VideoOrientation.Horizontal, EncodeProfile.Standard,
            Path.Combine(workspace.Root, "normal-single.log"), DraftAudioQuality.High,
            NormalTextOverlayEnabled: false);

        var result = await EncodingService.EncodeAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(48000, (await EncodingService.GetAudioInfoAsync(output, ffmpeg))?.SampleRate);
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.Root, ".movie-maker-*"));
    }

    [Fact]
    public async Task NormalMultipleTracks_SkipNormalizationAllowsSilentTrackWithoutTargets()
    {
        var ffmpeg = EncodingService.ResolveFfmpegPath();
        Assert.NotNull(ffmpeg);
        using var workspace = new Workspace();
        var silentAudio = await workspace.CreateSilenceAudioAsync(ffmpeg!, "silent.wav", 1.0);
        var secondAudio = await workspace.CreateAudioAsync(ffmpeg!, "second.wav", 1.0);
        var image = workspace.CreateImage("landscape.png", 160, 90);
        var output = Path.Combine(workspace.Root, "normal-no-normalization.mp4");
        var request = new EncodeRequest(ffmpeg, image, [silentAudio, secondAudio], output,
            VideoOrientation.Horizontal, EncodeProfile.Standard,
            Path.Combine(workspace.Root, "normal-no-normalization.log"), DraftAudioQuality.High,
            NormalTextOverlayEnabled: false,
            SkipLoudnessNormalization: true);

        var result = await EncodingService.EncodeAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        var outputAudio = await EncodingService.GetAudioInfoAsync(output, ffmpeg);
        Assert.Equal(48000, outputAudio?.SampleRate);
        Assert.InRange(outputAudio!.DurationSeconds!.Value, 1.9, 2.2);
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.Root, ".movie-maker-*"));
    }

    [Fact]
    public async Task NormalMultipleTracks_InvalidTargetsFailOnlyWhenNormalizationIsEnabled()
    {
        var ffmpeg = EncodingService.ResolveFfmpegPath();
        Assert.NotNull(ffmpeg);
        using var workspace = new Workspace();
        var firstAudio = await workspace.CreateAudioAsync(ffmpeg!, "first.wav", 1.0);
        var secondAudio = await workspace.CreateAudioAsync(ffmpeg!, "second.wav", 1.0);
        var image = workspace.CreateImage("landscape.png", 160, 90);
        var invalidTargets = new[]
        {
            new LoudnessNormalizationTarget(-14, -1),
            new LoudnessNormalizationTarget(1, -1)
        };
        var rejectedRequest = new EncodeRequest(ffmpeg, image, [firstAudio, secondAudio],
            Path.Combine(workspace.Root, "invalid-targets.mp4"), VideoOrientation.Horizontal,
            EncodeProfile.Standard, Path.Combine(workspace.Root, "invalid-targets.log"), DraftAudioQuality.High,
            TrackNormalizationTargets: invalidTargets, NormalTextOverlayEnabled: false);

        var rejected = await EncodingService.EncodeAsync(rejectedRequest);

        Assert.False(rejected.Success);
        Assert.Contains("ノーマライズ目標値", rejected.ErrorMessage);

        var skippedRequest = rejectedRequest with
        {
            OutputPath = Path.Combine(workspace.Root, "invalid-targets-skipped.mp4"),
            LogPath = Path.Combine(workspace.Root, "invalid-targets-skipped.log"),
            SkipLoudnessNormalization = true
        };
        var skipped = await EncodingService.EncodeAsync(skippedRequest);

        Assert.True(skipped.Success, skipped.ErrorMessage);
        Assert.True(File.Exists(skippedRequest.OutputPath));
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

    [Fact]
    public async Task Draft_RejectsVerticalOrientation()
    {
        var ffmpeg = EncodingService.ResolveFfmpegPath();
        Assert.NotNull(ffmpeg);
        using var workspace = new Workspace();
        var audio = await workspace.CreateAudioAsync(ffmpeg!, "audio.wav", 1.5);
        var image = workspace.CreateImage("portrait.png", 90, 160);
        var output = Path.Combine(workspace.Root, "draft-vertical.mp4");
        var request = new EncodeRequest(ffmpeg, image, [audio], output,
            VideoOrientation.Vertical, EncodeProfile.DraftPreview,
            Path.Combine(workspace.Root, "draft-vertical.log"), DraftAudioQuality.High,
            NormalTextOverlayEnabled: false);

        var result = await EncodingService.EncodeAsync(request);

        Assert.False(result.Success);
        Assert.Contains("横向き", result.ErrorMessage);
        Assert.False(File.Exists(output));
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

        public async Task<string> CreateFlacAudioAsync(string ffmpeg, string name, double duration)
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
                         "-ar", "48000", "-c:a", "flac", path })
                psi.ArgumentList.Add(argument);
            using var process = Process.Start(psi)!;
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, await stderr);
            return path;
        }

        public async Task<string> CreateSilenceAudioAsync(string ffmpeg, string name, double duration)
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
                         $"anullsrc=r=48000:cl=stereo:d={duration.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
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
