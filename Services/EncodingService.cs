using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.Drawing;
using MovieMaker.Models;

namespace MovieMaker.Services;

public sealed record EncodeRequest(
    string FfmpegPath,
    string ImagePath,
    IReadOnlyList<string> AudioPaths,
    string OutputPath,
    VideoOrientation Orientation,
    EncodeProfile Profile,
    string LogPath,
    DraftAudioQuality DraftAudioQuality,
    IReadOnlyList<string>? TrackImagePaths = null,
    IReadOnlyList<LoudnessNormalizationTarget>? TrackNormalizationTargets = null,
    int ShortsMaximumSeconds = 60,
    bool NormalTextOverlayEnabled = true,
    double OneMinuteShortsOffsetSeconds = 3,
    double ThreeMinuteShortsOffsetSeconds = 3,
    string? SourceImagePath = null,
    bool SkipLoudnessNormalization = false);

public sealed record EncodeResult(
    bool Success,
    string OutputPath,
    string LogPath,
    string? ErrorMessage,
    string Encoder);

public sealed record AudioInfo(
    string? CodecName,
    int? SampleRate,
    int? BitDepth,
    int? Channels,
    int? BitRate,
    string? SampleFormat,
    double? DurationSeconds);

public enum AudioLoudnessStatus
{
    WithinTarget,
    TooQuiet,
    TooLoud
}

public enum AudioTruePeakStatus
{
    Unknown,
    WithinTarget,
    TooHigh
}

public sealed record AudioLoudnessResult(
    double IntegratedLufs,
    AudioLoudnessStatus Status,
    double? TruePeakDbtp = null,
    double? LoudnessRangeLu = null,
    AudioTruePeakStatus TruePeakStatus = AudioTruePeakStatus.Unknown);

public sealed record LoudnessNormalizationMeasurements(
    double IntegratedLufs,
    double TruePeakDbtp,
    double LoudnessRangeLu,
    double ThresholdLufs);

public sealed record LoudnessNormalizationTarget(double IntegratedLufs, double TruePeakDbtp);

public static class EncodingService
{
    public static bool ShouldApplyTrackNormalization(EncodeProfile profile, int audioTrackCount,
        bool skipLoudnessNormalization) =>
        profile == EncodeProfile.Standard && audioTrackCount >= 2 && !skipLoudnessNormalization;

    public static bool ValidateProductionInput(int imageWidth, int imageHeight, int audioCount,
        EncodeProfile profile, int maximumSeconds, double audioDurationSeconds, out string error)
    {
        error = string.Empty;
        if (imageWidth <= 0 || imageHeight <= 0 || audioCount < 1 ||
            maximumSeconds is < 1 or > 180 || !double.IsFinite(audioDurationSeconds) || audioDurationSeconds <= 0)
        {
            error = "入力画像・音声・Shorts上限を確認してください。";
            return false;
        }
        if (profile is not (EncodeProfile.CopyrightCheckProduction or EncodeProfile.Standard))
        {
            error = "出力種別が不正です。";
            return false;
        }
        if (profile == EncodeProfile.CopyrightCheckProduction &&
            (imageHeight <= imageWidth || audioCount != 1 || audioDurationSeconds > maximumSeconds))
        {
            error = "Shortsには縦画像1枚と上限以内の音声1本が必要です。";
            return false;
        }
        if (profile == EncodeProfile.Standard && imageWidth < imageHeight)
        {
            error = "通常動画には横向きまたは正方形の画像が必要です。";
            return false;
        }
        return true;
    }
    public const double TargetIntegratedLufs = -14.0;
    public const double TargetTruePeakDbtp = -1.0;
    public const double LoudnessWarningLowerBoundLufs = -16.0;
    public const double LoudnessWarningUpperBoundLufs = -12.0;
    public const double TruePeakWarningLimitDbtp = -1.0;
    public const double MinimumTargetIntegratedLufs = -70.0;
    public const double MaximumTargetIntegratedLufs = -5.0;
    public const double MinimumTargetTruePeakDbtp = -8.0;
    public const double MaximumTargetTruePeakDbtp = 0.0;
    private const double TargetLoudnessRangeLu = 11.0;
    private const double LossyEncodingTruePeakHeadroomDb = 1.0;

    private sealed record VideoEncoder(string Name, string DisplayName);

    private static readonly VideoEncoder LibX264 = new("libx264", "CPU (libx264)");
    private static readonly VideoEncoder Nvenc = new("h264_nvenc", "NVIDIA (NVENC)");
    private static readonly VideoEncoder Qsv = new("h264_qsv", "Intel (QSV)");
    private static readonly VideoEncoder Amf = new("h264_amf", "AMD (AMF)");

    public static string? ResolveFfmpegPath()
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var paths = pathVariable.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var trimmed = path.Trim('"');
            var candidate = Path.Combine(trimmed, "ffmpeg.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static string GetLogDirectory()
    {
        var primary = Path.Combine(AppContext.BaseDirectory, "logs");
        try
        {
            Directory.CreateDirectory(primary);
            return primary;
        }
        catch
        {
            var fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MovieMaker",
                "logs");
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }

    public static async Task<EncodeResult> EncodeAsync(EncodeRequest request)
    {
        if (!File.Exists(request.FfmpegPath))
        {
            return new EncodeResult(false, request.OutputPath, request.LogPath, "ffmpegが見つかりません。", string.Empty);
        }

        if (request.Profile == EncodeProfile.DraftPreview && request.Orientation != VideoOrientation.Horizontal)
        {
            return new EncodeResult(false, request.OutputPath, request.LogPath,
                "仮動画は横向きでのみ出力できます。", string.Empty);
        }

        var applyTrackNormalization = ShouldApplyTrackNormalization(request.Profile,
            request.AudioPaths.Count, request.SkipLoudnessNormalization);

        if (request.Profile == EncodeProfile.Standard && request.NormalTextOverlayEnabled &&
            (request.TrackImagePaths == null || request.TrackImagePaths.Count != request.AudioPaths.Count ||
             request.TrackImagePaths.Count == 0 || request.TrackImagePaths.Any(path => !File.Exists(path))))
        {
            return new EncodeResult(false, request.OutputPath, request.LogPath, "通常モードの音声トラック画像が不足しています。", string.Empty);
        }

        if (request.Profile == EncodeProfile.Standard && request.NormalTextOverlayEnabled &&
            (string.IsNullOrWhiteSpace(request.SourceImagePath) || !File.Exists(request.SourceImagePath)))
        {
            return new EncodeResult(false, request.OutputPath, request.LogPath,
                "通常動画の元背景画像が見つかりません。", string.Empty);
        }

        if (request.Profile == EncodeProfile.Standard && !request.NormalTextOverlayEnabled &&
            request.TrackImagePaths != null)
        {
            return new EncodeResult(false, request.OutputPath, request.LogPath,
                "文字入れなしの通常動画には共通画像1枚だけを指定してください。", string.Empty);
        }

        if (applyTrackNormalization &&
            (request.TrackNormalizationTargets == null || request.TrackNormalizationTargets.Count != request.AudioPaths.Count ||
             request.TrackNormalizationTargets.Any(target => !AreValidNormalizationTargets(target.IntegratedLufs, target.TruePeakDbtp))))
        {
            return new EncodeResult(false, request.OutputPath, request.LogPath, "ノーマライズ目標値が範囲外です。", string.Empty);
        }

        try
        {
            var policySettings = new AppSettings
            {
                ShortsMaximumSeconds = request.ShortsMaximumSeconds,
                OneMinuteShortsOffsetSeconds = request.OneMinuteShortsOffsetSeconds,
                ThreeMinuteShortsOffsetSeconds = request.ThreeMinuteShortsOffsetSeconds
            };
            if (request.Profile != EncodeProfile.DraftPreview && !ShortsPolicy.AreSettingsValid(policySettings))
                return new EncodeResult(false, request.OutputPath, request.LogPath, "Shorts設定を修正してください。", string.Empty);
            if (request.Profile != EncodeProfile.DraftPreview && request.ShortsMaximumSeconds is < 1 or > 180)
                return new EncodeResult(false, request.OutputPath, request.LogPath, "Shorts上限が範囲外です。", string.Empty);

            var outputDirectory = Path.GetDirectoryName(request.OutputPath);
            if (string.IsNullOrWhiteSpace(outputDirectory))
                return new EncodeResult(false, request.OutputPath, request.LogPath, "出力先が不正です。", string.Empty);
            Directory.CreateDirectory(outputDirectory);
            var snapshotDirectory = Path.Combine(outputDirectory, ".movie-maker-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(snapshotDirectory);
            try
            {
                var imageCopy = Path.Combine(snapshotDirectory, "image" + Path.GetExtension(request.ImagePath));
                File.Copy(request.ImagePath, imageCopy);
                string? sourceImageCopy = null;
                if (request.Profile == EncodeProfile.Standard && request.NormalTextOverlayEnabled)
                {
                    sourceImageCopy = Path.Combine(snapshotDirectory,
                        "source-image" + Path.GetExtension(request.SourceImagePath!));
                    File.Copy(request.SourceImagePath!, sourceImageCopy);
                }
                var audioCopies = request.AudioPaths.Select((path, index) =>
                {
                    var copy = Path.Combine(snapshotDirectory, $"audio-{index}" + Path.GetExtension(path));
                    File.Copy(path, copy);
                    return copy;
                }).ToArray();
                IReadOnlyList<string>? trackImageCopies = request.TrackImagePaths?.Select((path, index) =>
                {
                    var copy = Path.Combine(snapshotDirectory, $"overlay-{index}" + Path.GetExtension(path));
                    File.Copy(path, copy);
                    return copy;
                }).ToArray();
                using var bitmap = Image.FromFile(sourceImageCopy ?? imageCopy);
                if (request.Profile == EncodeProfile.Standard && request.NormalTextOverlayEnabled &&
                    trackImageCopies!.Any(path => !HasNonPortraitDimensions(path)))
                    return new EncodeResult(false, request.OutputPath, request.LogPath,
                        "文字入れ画像の実寸が通常動画に対応していません。", string.Empty);
                var durations = await GetTrackDurationsAsync(audioCopies, request.FfmpegPath);
                if (durations == null)
                    return new EncodeResult(false, request.OutputPath, request.LogPath, "音声の長さを取得できませんでした。", string.Empty);
                var totalDuration = durations.Sum();
                if (request.Profile != EncodeProfile.DraftPreview &&
                    !ValidateProductionInput(bitmap.Width, bitmap.Height, audioCopies.Length, request.Profile,
                        request.ShortsMaximumSeconds, totalDuration, out var inputError))
                    return new EncodeResult(false, request.OutputPath, request.LogPath, inputError, string.Empty);
                if (request.Profile == EncodeProfile.CopyrightCheckProduction &&
                    (request.Orientation != VideoOrientation.Vertical || request.NormalTextOverlayEnabled || trackImageCopies != null))
                    return new EncodeResult(false, request.OutputPath, request.LogPath, "Shorts要求が入力条件と矛盾しています。", string.Empty);
                if (request.Profile == EncodeProfile.Standard && request.Orientation != VideoOrientation.Horizontal)
                    return new EncodeResult(false, request.OutputPath, request.LogPath, "通常動画要求が入力条件と矛盾しています。", string.Empty);
                var stagedOutput = Path.Combine(outputDirectory, ".movie-maker-" + Guid.NewGuid().ToString("N") + ".mp4");
                var executionRequest = request with
                {
                    ImagePath = imageCopy,
                    SourceImagePath = sourceImageCopy,
                    AudioPaths = audioCopies,
                    TrackImagePaths = trackImageCopies,
                    OutputPath = stagedOutput
                };
                try
                {
                    return await EncodeSnapshotAsync(executionRequest, request.OutputPath, durations, policySettings);
                }
                finally
                {
                    try { if (File.Exists(stagedOutput)) File.Delete(stagedOutput); }
                    catch (IOException) { /* Keep the result; cleanup is best effort. */ }
                    catch (UnauthorizedAccessException) { /* Keep the result; cleanup is best effort. */ }
                }
            }
            finally
            {
                try { Directory.Delete(snapshotDirectory, recursive: true); }
                catch (IOException) { /* temporary cleanup is best effort */ }
                catch (UnauthorizedAccessException) { /* temporary cleanup is best effort */ }
            }
        }
        catch (Exception ex)
        {
            return new EncodeResult(false, request.OutputPath, request.LogPath, ex.Message, string.Empty);
        }
    }

    private static bool HasNonPortraitDimensions(string path)
    {
        using var image = Image.FromFile(path);
        return image.Width >= image.Height;
    }

    private static async Task<EncodeResult> EncodeSnapshotAsync(EncodeRequest request, string finalOutputPath,
        IReadOnlyList<double> sourceDurations, AppSettings policySettings)
    {
        try
        {
            var logDirectory = Path.GetDirectoryName(request.LogPath);
            if (!string.IsNullOrWhiteSpace(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            using var logWriter = new StreamWriter(request.LogPath, false, Encoding.UTF8);
            var logLock = new object();

            var candidates = await GetEncoderCandidatesAsync(request.FfmpegPath, logWriter, logLock);
            var trimTargetSeconds = request.Profile == EncodeProfile.CopyrightCheckProduction &&
                ShortsPolicy.TryGetTrimTargetSeconds(sourceDurations[0], out var targetSeconds, policySettings)
                ? targetSeconds : (double?)null;
            var segmentDurations = request.Profile == EncodeProfile.Standard ? sourceDurations : null;
            if (request.Profile == EncodeProfile.Standard && segmentDurations == null)
            {
                return new EncodeResult(false, request.OutputPath, request.LogPath,
                    "音声トラックの長さを取得できませんでした。", string.Empty);
            }
            var applyTrackNormalization = ShouldApplyTrackNormalization(request.Profile,
                request.AudioPaths.Count, request.SkipLoudnessNormalization);
            var normalizationMeasurements = applyTrackNormalization
                ? await GetNormalizationMeasurementsAsync(request.AudioPaths, request.FfmpegPath,
                    request.TrackNormalizationTargets!, logWriter, logLock)
                : null;
            if (applyTrackNormalization && normalizationMeasurements == null)
            {
                return new EncodeResult(false, request.OutputPath, request.LogPath,
                    "音声トラックのノーマライズ解析に失敗しました。入力音声とFFmpegログを確認してください。", string.Empty);
            }
            foreach (var encoder in candidates)
            {
                var psi = BuildStartInfo(request, encoder, trimTargetSeconds, segmentDurations, normalizationMeasurements);
                WriteLog(logWriter, logLock, $"Encoder: {encoder.DisplayName} ({encoder.Name})");

                var (exitCode, succeeded) = await RunProcessAsync(psi, logWriter, logLock);
                if (succeeded && File.Exists(request.OutputPath))
                {
                    if (request.Profile == EncodeProfile.CopyrightCheckProduction &&
                        !await IsEncodedDurationWithinLimitAsync(request.OutputPath, request.FfmpegPath,
                            request.ShortsMaximumSeconds))
                    {
                        WriteLog(logWriter, logLock, "Shorts completed duration is unavailable or exceeds limit.");
                        File.Delete(request.OutputPath);
                        return new EncodeResult(false, finalOutputPath, request.LogPath,
                            "完成動画の長さを確認できないか、Shorts上限を超えました。", string.Empty);
                    }
                    WriteLog(logWriter, logLock, $"Encoder completed: {encoder.Name}; publishing output.");
                    logWriter.Dispose();
                    File.Move(request.OutputPath, finalOutputPath, overwrite: false);
                    return new EncodeResult(true, finalOutputPath, request.LogPath, null, encoder.DisplayName);
                }

                WriteLog(logWriter, logLock, $"Failed: {encoder.Name} (ExitCode: {exitCode})");

                try
                {
                    if (File.Exists(request.OutputPath))
                    {
                        File.Delete(request.OutputPath);
                    }
                }
                catch
                {
                    // ignore cleanup errors
                }
            }

            return new EncodeResult(false, finalOutputPath, request.LogPath,
                "ffmpegが失敗しました (全てのエンコーダで失敗)", string.Empty);
        }
        catch (Exception ex)
        {
            return new EncodeResult(false, finalOutputPath, request.LogPath, ex.Message, string.Empty);
        }
    }

    private static ProcessStartInfo BuildStartInfo(EncodeRequest request, VideoEncoder encoder, double? trimTargetSeconds,
        IReadOnlyList<double>? segmentDurations, IReadOnlyList<LoudnessNormalizationMeasurements>? normalizationMeasurements)
    {
        var options = EncodingOptionsResolver.Resolve(request.Orientation, request.Profile, request.DraftAudioQuality);
        var psi = new ProcessStartInfo
        {
            FileName = request.FfmpegPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (request.Profile == EncodeProfile.Standard && segmentDurations != null)
        {
            AddStandardTrackInputs(psi, request, segmentDurations, options);
            AddVideoEncoderArgs(psi, encoder, request);
            psi.ArgumentList.Add("-filter_complex");
            var applyTrackNormalization = ShouldApplyTrackNormalization(request.Profile,
                request.AudioPaths.Count, request.SkipLoudnessNormalization);
            psi.ArgumentList.Add(BuildStandardTrackFilter(segmentDurations, options,
                applyTrackNormalization ? normalizationMeasurements : null,
                applyTrackNormalization ? request.TrackNormalizationTargets : null,
                applyTrackNormalization));
            psi.ArgumentList.Add("-map");
            psi.ArgumentList.Add("[vout]");
            psi.ArgumentList.Add("-map");
            psi.ArgumentList.Add("[aout]");
            psi.ArgumentList.Add("-r");
            psi.ArgumentList.Add(options.FrameRate.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("-pix_fmt");
            psi.ArgumentList.Add("yuv420p");
            AddOutputAudioArgs(psi, options);
            psi.ArgumentList.Add("-movflags");
            psi.ArgumentList.Add("+faststart");
            psi.ArgumentList.Add(request.OutputPath);
            return psi;
        }

        psi.ArgumentList.Add("-loop");
        psi.ArgumentList.Add("1");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(request.ImagePath);
        foreach (var audioPath in request.AudioPaths)
        {
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(audioPath);
        }

        AddVideoEncoderArgs(psi, encoder, request);

        var filter = BuildVideoFilter(request, trimTargetSeconds);
        psi.ArgumentList.Add("-vf");
        psi.ArgumentList.Add(filter);
        psi.ArgumentList.Add("-r");
        psi.ArgumentList.Add(options.FrameRate.ToString(CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("-pix_fmt");
        psi.ArgumentList.Add("yuv420p");

        var audioFilterComplex = BuildAudioFilterComplex(request.AudioPaths.Count, trimTargetSeconds);
        psi.ArgumentList.Add("-map");
        psi.ArgumentList.Add("0:v:0");

        if (!string.IsNullOrWhiteSpace(audioFilterComplex))
        {
            psi.ArgumentList.Add("-filter_complex");
            psi.ArgumentList.Add(audioFilterComplex);
            psi.ArgumentList.Add("-map");
            psi.ArgumentList.Add("[aout]");
        }
        else
        {
            psi.ArgumentList.Add("-map");
            psi.ArgumentList.Add("1:a:0");
        }

        AddOutputAudioArgs(psi, options);

        if (trimTargetSeconds.HasValue || request.Profile == EncodeProfile.CopyrightCheckProduction)
        {
            psi.ArgumentList.Add("-t");
            psi.ArgumentList.Add(FormatSeconds(trimTargetSeconds ?? request.ShortsMaximumSeconds));
        }

        psi.ArgumentList.Add("-shortest");
        psi.ArgumentList.Add("-movflags");
        psi.ArgumentList.Add("+faststart");
        psi.ArgumentList.Add(request.OutputPath);

        return psi;
    }

    private static void AddOutputAudioArgs(ProcessStartInfo psi, EncodingOptions options)
    {
        psi.ArgumentList.Add("-c:a");
        psi.ArgumentList.Add("aac");
        psi.ArgumentList.Add("-b:a");
        psi.ArgumentList.Add(options.AudioBitrate);
        psi.ArgumentList.Add("-ar");
        psi.ArgumentList.Add(options.AudioSampleRate);
    }

    private static void AddStandardTrackInputs(ProcessStartInfo psi, EncodeRequest request,
        IReadOnlyList<double> durations, EncodingOptions options)
    {
        for (var index = 0; index < request.AudioPaths.Count; index++)
        {
            psi.ArgumentList.Add("-loop");
            psi.ArgumentList.Add("1");
            psi.ArgumentList.Add("-framerate");
            psi.ArgumentList.Add(options.FrameRate.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("-t");
            psi.ArgumentList.Add(FormatSeconds(durations[index]));
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(request.NormalTextOverlayEnabled ? request.TrackImagePaths![index] : request.ImagePath);
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(request.AudioPaths[index]);
        }
    }

    private static string BuildStandardTrackFilter(IReadOnlyList<double> durations, EncodingOptions options,
        IReadOnlyList<LoudnessNormalizationMeasurements>? measurements,
        IReadOnlyList<LoudnessNormalizationTarget>? targets, bool applyNormalization)
    {
        var filters = new List<string>();
        var concatInputs = new StringBuilder();
        for (var index = 0; index < durations.Count; index++)
        {
            var videoInput = index * 2;
            var audioInput = videoInput + 1;
            var duration = FormatSeconds(durations[index]);
            filters.Add($"[{videoInput}:v]scale={options.Width}:{options.Height}:force_original_aspect_ratio=decrease," +
                $"pad={options.Width}:{options.Height}:(ow-iw)/2:(oh-ih)/2:black,setsar=1," +
                $"trim=duration={duration},setpts=PTS-STARTPTS[v{index}]");
            var audioFilter = $"[{audioInput}:a]atrim=duration={duration},asetpts=PTS-STARTPTS";
            if (applyNormalization)
            {
                var measured = measurements![index];
                var target = targets![index];
                var filterTruePeakTarget = GetFilterTruePeakTarget(target.TruePeakDbtp);
                audioFilter += $",loudnorm=I={FormatMetric(target.IntegratedLufs)}:TP={FormatMetric(filterTruePeakTarget)}:" +
                    $"LRA={FormatMetric(TargetLoudnessRangeLu)}:measured_I={FormatMetric(measured.IntegratedLufs)}:" +
                    $"measured_TP={FormatMetric(measured.TruePeakDbtp)}:measured_LRA={FormatMetric(measured.LoudnessRangeLu)}:" +
                    $"measured_thresh={FormatMetric(measured.ThresholdLufs)}:linear=true";
            }
            audioFilter += $",aresample={options.AudioSampleRate},aformat=channel_layouts=stereo[a{index}]";
            filters.Add(audioFilter);
            concatInputs.Append($"[v{index}][a{index}]");
        }

        filters.Add($"{concatInputs}concat=n={durations.Count}:v=1:a=1[vout][aout]");
        return string.Join(';', filters);
    }

    private static async Task<IReadOnlyList<double>?> GetTrackDurationsAsync(
        IReadOnlyList<string> audioPaths, string? ffmpegPath)
    {
        var durations = new List<double>(audioPaths.Count);
        foreach (var audioPath in audioPaths)
        {
            var info = await GetAudioInfoAsync(audioPath, ffmpegPath);
            if (info?.DurationSeconds is not double duration || !double.IsFinite(duration) || duration <= 0)
            {
                return null;
            }
            durations.Add(duration);
        }
        return durations;
    }

    private static async Task<IReadOnlyList<LoudnessNormalizationMeasurements>?> GetNormalizationMeasurementsAsync(
        IReadOnlyList<string> audioPaths, string ffmpegPath, IReadOnlyList<LoudnessNormalizationTarget> targets,
        StreamWriter logWriter, object logLock)
    {
        var measurements = new List<LoudnessNormalizationMeasurements>(audioPaths.Count);
        foreach (var audioPath in audioPaths)
        {
            var measurement = await AnalyzeLoudnessForNormalizationAsync(
                audioPath, ffmpegPath, targets[measurements.Count].IntegratedLufs, targets[measurements.Count].TruePeakDbtp);
            if (measurement == null)
            {
                WriteLog(logWriter, logLock, $"Loudness normalization analysis failed: {audioPath}");
                return null;
            }

            measurements.Add(measurement);
            WriteLog(logWriter, logLock,
                $"Loudness normalization input: {audioPath} I={FormatMetric(measurement.IntegratedLufs)} LUFS " +
                $"TP={FormatMetric(measurement.TruePeakDbtp)} dBTP LRA={FormatMetric(measurement.LoudnessRangeLu)} LU");
        }

        return measurements;
    }

    private static async Task<LoudnessNormalizationMeasurements?> AnalyzeLoudnessForNormalizationAsync(
        string audioPath, string ffmpegPath, double targetIntegratedLufs, double targetTruePeakDbtp)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-nostdin");
        psi.ArgumentList.Add("-nostats");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(audioPath);
        psi.ArgumentList.Add("-map");
        psi.ArgumentList.Add("0:a:0");
        psi.ArgumentList.Add("-af");
        psi.ArgumentList.Add($"loudnorm=I={FormatMetric(targetIntegratedLufs)}:TP={FormatMetric(GetFilterTruePeakTarget(targetTruePeakDbtp))}:" +
            $"LRA={FormatMetric(TargetLoudnessRangeLu)}:print_format=json");
        psi.ArgumentList.Add("-vn");
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("null");
        psi.ArgumentList.Add("NUL");

        using var process = Process.Start(psi);
        if (process == null)
        {
            return null;
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(outputTask, errorTask);
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            return null;
        }

        return ParseLoudnessNormalizationMeasurements($"{outputTask.Result}\n{errorTask.Result}");
    }

    private static LoudnessNormalizationMeasurements? ParseLoudnessNormalizationMeasurements(string output)
    {
        var jsonStart = output.IndexOf('{');
        var jsonEnd = output.LastIndexOf('}');
        if (jsonStart < 0 || jsonEnd <= jsonStart)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(output[jsonStart..(jsonEnd + 1)]);
            var root = document.RootElement;
            if (!root.TryGetProperty("input_i", out var inputLoudness) ||
                !root.TryGetProperty("input_tp", out var inputTruePeak) ||
                !root.TryGetProperty("input_lra", out var inputLoudnessRange) ||
                !root.TryGetProperty("input_thresh", out var inputThreshold))
            {
                return null;
            }

            var integrated = ParseLoudnessMetric(inputLoudness);
            var truePeak = ParseLoudnessMetric(inputTruePeak);
            var loudnessRange = ParseLoudnessMetric(inputLoudnessRange);
            var threshold = ParseLoudnessMetric(inputThreshold);
            if (!integrated.HasValue || !truePeak.HasValue || !loudnessRange.HasValue || !threshold.HasValue ||
                !double.IsFinite(integrated.Value) || !double.IsFinite(truePeak.Value) ||
                !double.IsFinite(loudnessRange.Value) || !double.IsFinite(threshold.Value))
            {
                return null;
            }

            return new LoudnessNormalizationMeasurements(integrated.Value, truePeak.Value,
                loudnessRange.Value, threshold.Value);
        }
        catch
        {
            return null;
        }
    }

    private static bool AreValidNormalizationTargets(double integratedLufs, double truePeakDbtp) =>
        double.IsFinite(integratedLufs) && integratedLufs >= MinimumTargetIntegratedLufs &&
        integratedLufs <= MaximumTargetIntegratedLufs && double.IsFinite(truePeakDbtp) &&
        truePeakDbtp >= MinimumTargetTruePeakDbtp && truePeakDbtp <= MaximumTargetTruePeakDbtp;

    private static double GetFilterTruePeakTarget(double requestedTruePeakDbtp) =>
        requestedTruePeakDbtp - LossyEncodingTruePeakHeadroomDb;

    private static string FormatMetric(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string BuildVideoFilter(EncodeRequest request, double? trimTargetSeconds)
    {
        var options = EncodingOptionsResolver.Resolve(request.Orientation, request.Profile);
        var filter = $"scale={options.Width}:{options.Height}:force_original_aspect_ratio=decrease," +
            $"pad={options.Width}:{options.Height}:(ow-iw)/2:(oh-ih)/2:black,setsar=1";
        if (trimTargetSeconds.HasValue)
        {
            var fadeStart = trimTargetSeconds.Value - ShortsPolicy.FadeSeconds;
            filter += $",fade=t=out:st={FormatSeconds(fadeStart)}:d={FormatSeconds(ShortsPolicy.FadeSeconds)}";
        }

        return filter;
    }

    public static string? BuildAudioFilter(double? trimTargetSeconds)
    {
        if (!trimTargetSeconds.HasValue)
        {
            return null;
        }

        var fadeStart = trimTargetSeconds.Value - ShortsPolicy.FadeSeconds;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"afade=t=out:st={FormatSeconds(fadeStart)}:d={FormatSeconds(ShortsPolicy.FadeSeconds)}," +
            $"silenceremove=stop_periods=1:stop_duration={FormatSeconds(ShortsPolicy.TrailingSilenceDurationSeconds)}:" +
            $"stop_threshold={ShortsPolicy.TrailingSilenceThreshold}");
    }

    public static string? BuildAudioFilterComplex(int audioInputCount, double? trimTargetSeconds)
    {
        if (audioInputCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(audioInputCount));
        }

        var audioFilter = BuildAudioFilter(trimTargetSeconds);
        if (audioInputCount == 1)
        {
            if (string.IsNullOrWhiteSpace(audioFilter))
            {
                return null;
            }

            return $"[1:a]{audioFilter}[aout]";
        }

        var concatInputs = new StringBuilder();
        for (var i = 1; i <= audioInputCount; i++)
        {
            concatInputs.Append('[').Append(i).Append(":a]");
        }

        var concat = string.Create(
            CultureInfo.InvariantCulture,
            $"{concatInputs}concat=n={audioInputCount}:v=0:a=1[a_concat]");

        if (string.IsNullOrWhiteSpace(audioFilter))
        {
            return $"{concatInputs}concat=n={audioInputCount}:v=0:a=1[aout]";
        }

        return $"{concat};[a_concat]{audioFilter}[aout]";
    }

    public static async Task<AudioInfo?> GetAudioInfoAsync(string audioPath, string? ffmpegPath = null)
    {
        if (!File.Exists(audioPath))
        {
            return null;
        }

        var ffprobePath = ResolveFfprobePath(ffmpegPath);
        if (ffprobePath == null)
        {
            return null;
        }

        var document = await RunFfprobeJsonAsync(ffprobePath, new[]
        {
            "-v", "error",
            "-select_streams", "a:0",
            "-show_entries", "stream=codec_name,sample_rate,bits_per_sample,bits_per_raw_sample,channels,bit_rate,sample_fmt:format=duration",
            "-of", "json",
            audioPath
        });

        if (document == null)
        {
            return null;
        }

        try
        {
            var streams = document.RootElement.GetProperty("streams");
            if (streams.GetArrayLength() == 0)
            {
                return null;
            }

            var stream = streams[0];
            var codecName = ParseStringProperty(stream, "codec_name");
            var sampleRate = ParseIntProperty(stream, "sample_rate");
            var bitDepth = ParseIntProperty(stream, "bits_per_sample")
                           ?? ParseIntProperty(stream, "bits_per_raw_sample");
            var channels = ParseIntProperty(stream, "channels");
            var bitRate = ParseIntProperty(stream, "bit_rate");
            var sampleFormat = ParseStringProperty(stream, "sample_fmt");
            var durationSeconds = ParseDurationSeconds(document.RootElement);

            if (!bitDepth.HasValue && !string.IsNullOrWhiteSpace(sampleFormat))
            {
                bitDepth = ParseBitDepthFromSampleFormat(sampleFormat);
            }

            if (codecName == null && sampleRate == null && channels == null && bitDepth == null && bitRate == null &&
                sampleFormat == null && durationSeconds == null)
            {
                return null;
            }

            return new AudioInfo(codecName, sampleRate, bitDepth, channels, bitRate, sampleFormat, durationSeconds);
        }
        catch
        {
            return null;
        }
        finally
        {
            document.Dispose();
        }
    }

    public static async Task<AudioLoudnessResult?> GetAudioLoudnessAsync(string audioPath, string? ffmpegPath = null)
    {
        if (!File.Exists(audioPath))
        {
            return null;
        }

        var executablePath = string.IsNullOrWhiteSpace(ffmpegPath) ? ResolveFfmpegPath() : ffmpegPath;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return null;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = executablePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-nostdin");
            psi.ArgumentList.Add("-nostats");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(audioPath);
            psi.ArgumentList.Add("-map");
            psi.ArgumentList.Add("0:a:0");
            psi.ArgumentList.Add("-af");
            psi.ArgumentList.Add("loudnorm=I=-14:TP=-1.5:LRA=11:print_format=json");
            psi.ArgumentList.Add("-vn");
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add("null");
            psi.ArgumentList.Add("NUL");

            using var process = Process.Start(psi);
            if (process == null)
            {
                return null;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await Task.WhenAll(outputTask, errorTask);
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                return null;
            }

            return ParseAudioLoudnessOutput($"{outputTask.Result}\n{errorTask.Result}");
        }
        catch
        {
            return null;
        }
    }

    public static AudioLoudnessResult ClassifyIntegratedLoudness(double integratedLufs)
    {
        var status = integratedLufs < LoudnessWarningLowerBoundLufs
            ? AudioLoudnessStatus.TooQuiet
            : integratedLufs > LoudnessWarningUpperBoundLufs
                ? AudioLoudnessStatus.TooLoud
                : AudioLoudnessStatus.WithinTarget;

        return new AudioLoudnessResult(integratedLufs, status);
    }

    public static AudioTruePeakStatus ClassifyTruePeak(double truePeakDbtp)
    {
        if (double.IsNaN(truePeakDbtp))
        {
            return AudioTruePeakStatus.Unknown;
        }

        return truePeakDbtp > TruePeakWarningLimitDbtp
            ? AudioTruePeakStatus.TooHigh
            : AudioTruePeakStatus.WithinTarget;
    }

    public static AudioLoudnessResult? ParseAudioLoudnessOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var jsonStart = output.IndexOf('{');
        var jsonEnd = output.LastIndexOf('}');
        if (jsonStart < 0 || jsonEnd <= jsonStart)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(output[jsonStart..(jsonEnd + 1)]);
            if (!document.RootElement.TryGetProperty("input_i", out var inputLoudness) ||
                !document.RootElement.TryGetProperty("input_tp", out var inputTruePeak) ||
                !document.RootElement.TryGetProperty("input_lra", out var inputLoudnessRange))
            {
                return null;
            }

            var integratedLufs = ParseLoudnessMetric(inputLoudness);
            var truePeakDbtp = ParseLoudnessMetric(inputTruePeak);
            var loudnessRangeLu = ParseLoudnessMetric(inputLoudnessRange);
            if (!integratedLufs.HasValue || !truePeakDbtp.HasValue || !loudnessRangeLu.HasValue)
            {
                return null;
            }

            var result = ClassifyIntegratedLoudness(integratedLufs.Value);
            return result with
            {
                TruePeakDbtp = truePeakDbtp.Value,
                LoudnessRangeLu = loudnessRangeLu.Value,
                TruePeakStatus = ClassifyTruePeak(truePeakDbtp.Value)
            };
        }
        catch
        {
            return null;
        }
    }

    private static double? ParseLoudnessMetric(JsonElement element)
    {
        var text = element.ValueKind == JsonValueKind.Number
            ? element.GetRawText()
            : element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : null;

        if (string.Equals(text, "-inf", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "-infinity", StringComparison.OrdinalIgnoreCase))
        {
            return double.NegativeInfinity;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static async Task<bool> IsEncodedDurationWithinLimitAsync(string path, string? ffmpegPath, int limit)
    {
        var ffprobePath = ResolveFfprobePath(ffmpegPath);
        if (ffprobePath == null) return false;
        using var document = await RunFfprobeJsonAsync(ffprobePath, new[]
        {
            "-v", "error", "-show_entries", "format=start_time,duration,end_time:stream=codec_type,start_time,duration,end_time",
            "-of", "json", path
        });
        return document != null && IsShortsOutputWithinLimit(document.RootElement, limit);
    }

    private static bool IsShortsOutputWithinLimit(JsonElement root, int limit)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("format", out var format) ||
            !TryGetActualEndTime(format, out var containerEnd) || containerEnd > limit ||
            !root.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array)
            return false;
        var hasVideo = false;
        var hasAudio = false;
        foreach (var stream in streams.EnumerateArray())
        {
            if (stream.ValueKind != JsonValueKind.Object ||
                !stream.TryGetProperty("codec_type", out var codec) || codec.ValueKind != JsonValueKind.String)
                return false;
            var codecType = codec.GetString();
            if (codecType is not ("video" or "audio")) continue;
            if (!TryGetActualEndTime(stream, out var streamEnd) || streamEnd > limit) return false;
            if (codecType == "video") hasVideo = true;
            if (codecType == "audio") hasAudio = true;
        }
        return hasVideo && hasAudio;
    }

    private static bool TryGetActualEndTime(JsonElement element, out double endTime)
    {
        endTime = 0;
        if (element.ValueKind != JsonValueKind.Object) return false;
        var hasEnd = element.TryGetProperty("end_time", out var endElement);
        var hasStart = element.TryGetProperty("start_time", out var startElement);
        var hasDuration = element.TryGetProperty("duration", out var durationElement);
        if (!hasEnd && (!hasStart || !hasDuration)) return false;

        if (hasEnd)
        {
            if (!TryReadFiniteTime(endElement, out endTime) || endTime <= 0) return false;
        }

        if (hasStart && hasDuration)
        {
            if (!TryReadFiniteTime(startElement, out var start) ||
                !TryReadFiniteTime(durationElement, out var duration) || duration <= 0 ||
                !double.IsFinite(start + duration) || start + duration <= 0)
                return false;
            endTime = Math.Max(endTime, start + duration);
        }

        return endTime > 0;
    }

    private static bool TryReadFiniteTime(JsonElement element, out double time)
    {
        var text = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            _ => null
        };
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out time) &&
            double.IsFinite(time);
    }

    private static async Task<double?> ResolveShortsTrimTargetSecondsAsync(
        EncodeRequest request,
        StreamWriter logWriter,
        object logLock)
    {
        if (request.Orientation != VideoOrientation.Vertical)
        {
            return null;
        }

        var duration = await GetAudioDurationSecondsAsync(request.AudioPaths, request.FfmpegPath, logWriter, logLock);
        if (!duration.HasValue)
        {
            return null;
        }

        if (ShortsPolicy.TryGetTrimTargetSeconds(duration.Value, out var targetSeconds))
        {
            WriteLog(logWriter, logLock, $"Shorts trim enabled. Duration={FormatSeconds(duration.Value)}s Target={FormatSeconds(targetSeconds)}s");
            return targetSeconds;
        }

        WriteLog(logWriter, logLock, $"Shorts trim skipped. Duration={FormatSeconds(duration.Value)}s");
        return null;
    }

    private static async Task<double?> GetAudioDurationSecondsAsync(
        IReadOnlyList<string> audioPaths,
        string? ffmpegPath,
        StreamWriter? logWriter,
        object? logLock)
    {
        if (audioPaths.Count == 0)
        {
            return null;
        }

        var ffprobePath = ResolveFfprobePath(ffmpegPath);
        if (ffprobePath == null)
        {
            TryLog(logWriter, logLock, "ffprobe not found. Skip duration check.");
            return null;
        }

        try
        {
            double totalSeconds = 0;
            foreach (var audioPath in audioPaths)
            {
                var document = await RunFfprobeJsonAsync(ffprobePath, new[]
                {
                    "-v", "error",
                    "-show_entries", "format=duration",
                    "-of", "json",
                    audioPath
                });

                if (document == null)
                {
                    return null;
                }

                try
                {
                    var format = document.RootElement.GetProperty("format");
                    if (format.TryGetProperty("duration", out var durationElement))
                    {
                        var durationText = durationElement.GetString();
                        if (double.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                        {
                            totalSeconds += seconds;
                            continue;
                        }
                    }

                    TryLog(logWriter, logLock, "ffprobe duration parse failed.");
                    return null;
                }
                finally
                {
                    document.Dispose();
                }
            }

            return totalSeconds;
        }
        catch (Exception ex)
        {
            TryLog(logWriter, logLock, $"ffprobe error: {ex.Message}");
            return null;
        }
    }

    private static string? ResolveFfprobePath(string? ffmpegPath)
    {
        try
        {
            var ffmpegDir = string.IsNullOrWhiteSpace(ffmpegPath) ? null : Path.GetDirectoryName(ffmpegPath);
            if (!string.IsNullOrWhiteSpace(ffmpegDir))
            {
                var local = Path.Combine(ffmpegDir, "ffprobe.exe");
                if (File.Exists(local))
                {
                    return local;
                }
            }
        }
        catch
        {
            // ignore
        }

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var paths = pathVariable.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var trimmed = path.Trim('"');
            var candidate = Path.Combine(trimmed, "ffprobe.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static async Task<JsonDocument?> RunFfprobeJsonAsync(string ffprobePath, IEnumerable<string> arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffprobePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi);
        if (process == null)
        {
            return null;
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(output);
        }
        catch
        {
            return null;
        }
    }

    private static int? ParseIntProperty(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var numeric))
        {
            return numeric;
        }

        var text = value.GetString();
        if (!string.IsNullOrWhiteSpace(text) &&
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        return null;
    }

    private static string? ParseStringProperty(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.GetString();
    }

    private static double? ParseDurationSeconds(JsonElement root)
    {
        if (!root.TryGetProperty("format", out var format))
        {
            return null;
        }

        if (!format.TryGetProperty("duration", out var duration))
        {
            return null;
        }

        var text = duration.GetString();
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return seconds;
        }

        return null;
    }

    private static int? ParseBitDepthFromSampleFormat(string sampleFormat)
    {
        if (string.IsNullOrWhiteSpace(sampleFormat))
        {
            return null;
        }

        // sample_fmt examples: s16, s16p, s24, s24p, s32, s32p, flt, fltp, dbl, dblp
        if (sampleFormat.StartsWith("s16", StringComparison.OrdinalIgnoreCase))
        {
            return 16;
        }
        if (sampleFormat.StartsWith("s24", StringComparison.OrdinalIgnoreCase))
        {
            return 24;
        }
        if (sampleFormat.StartsWith("s32", StringComparison.OrdinalIgnoreCase))
        {
            return 32;
        }
        if (sampleFormat.StartsWith("flt", StringComparison.OrdinalIgnoreCase))
        {
            return 32;
        }
        if (sampleFormat.StartsWith("dbl", StringComparison.OrdinalIgnoreCase))
        {
            return 64;
        }

        return null;
    }

    private static void TryLog(StreamWriter? logWriter, object? logLock, string message)
    {
        if (logWriter == null || logLock == null)
        {
            return;
        }

        lock (logLock)
        {
            logWriter.WriteLine(message);
        }
    }

    private static string FormatSeconds(double seconds)
    {
        return seconds.ToString("R", CultureInfo.InvariantCulture);
    }

    private static void AddVideoEncoderArgs(ProcessStartInfo psi, VideoEncoder encoder, EncodeRequest request)
    {
        var options = EncodingOptionsResolver.Resolve(request.Orientation, request.Profile, request.DraftAudioQuality);
        psi.ArgumentList.Add("-c:v");
        psi.ArgumentList.Add(encoder.Name);

        if (encoder == LibX264)
        {
            psi.ArgumentList.Add("-tune");
            psi.ArgumentList.Add("stillimage");
            psi.ArgumentList.Add("-preset");
            psi.ArgumentList.Add(options.LibX264Preset);
            psi.ArgumentList.Add("-crf");
            psi.ArgumentList.Add(options.LibX264Crf.ToString(CultureInfo.InvariantCulture));
            return;
        }

        if (encoder == Nvenc)
        {
            psi.ArgumentList.Add("-preset");
            psi.ArgumentList.Add(options.NvencPreset);
            psi.ArgumentList.Add("-rc");
            psi.ArgumentList.Add("vbr");
            psi.ArgumentList.Add("-cq");
            psi.ArgumentList.Add(options.NvencCq.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("-b:v");
            psi.ArgumentList.Add("0");
            return;
        }

        if (encoder == Qsv)
        {
            psi.ArgumentList.Add("-preset");
            psi.ArgumentList.Add(options.QsvPreset);
            psi.ArgumentList.Add("-global_quality");
            psi.ArgumentList.Add(options.QsvGlobalQuality.ToString(CultureInfo.InvariantCulture));
            return;
        }

        if (encoder == Amf)
        {
            psi.ArgumentList.Add("-quality");
            psi.ArgumentList.Add(options.AmfQuality);
            psi.ArgumentList.Add("-rc");
            psi.ArgumentList.Add("cqp");
            psi.ArgumentList.Add("-qp_i");
            psi.ArgumentList.Add(options.AmfQp.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("-qp_p");
            psi.ArgumentList.Add(options.AmfQp.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("-qp_b");
            psi.ArgumentList.Add(options.AmfQp.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static async Task<(int ExitCode, bool Succeeded)> RunProcessAsync(
        ProcessStartInfo psi,
        StreamWriter logWriter,
        object logLock)
    {
        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data == null) return;
            WriteLog(logWriter, logLock, args.Data);
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data == null) return;
            WriteLog(logWriter, logLock, args.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        lock (logLock)
        {
            logWriter.Flush();
        }

        return (process.ExitCode, process.ExitCode == 0);
    }

    private static async Task<List<VideoEncoder>> GetEncoderCandidatesAsync(
        string ffmpegPath,
        StreamWriter logWriter,
        object logLock)
    {
        var available = await GetAvailableEncodersAsync(ffmpegPath, logWriter, logLock);
        var list = new List<VideoEncoder>();

        if (available.Contains(Nvenc.Name))
        {
            list.Add(Nvenc);
        }
        if (available.Contains(Qsv.Name))
        {
            list.Add(Qsv);
        }
        if (available.Contains(Amf.Name))
        {
            list.Add(Amf);
        }

        list.Add(LibX264);
        return list;
    }

    private static async Task<HashSet<string>> GetAvailableEncodersAsync(
        string ffmpegPath,
        StreamWriter logWriter,
        object logLock)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-encoders");

            using var process = Process.Start(psi);
            if (process == null)
            {
                return result;
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var combined = output + "\n" + error;
            foreach (var line in combined.Split('\n'))
            {
                if (line.Contains(" h264_nvenc", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(Nvenc.Name);
                }
                if (line.Contains(" h264_qsv", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(Qsv.Name);
                }
                if (line.Contains(" h264_amf", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(Amf.Name);
                }
                if (line.Contains(" libx264", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(LibX264.Name);
                }
            }

            WriteLog(logWriter, logLock, $"Available encoders: {string.Join(", ", result)}");
        }
        catch (Exception ex)
        {
            WriteLog(logWriter, logLock, $"Encoder detection failed: {ex.Message}");
        }

        return result;
    }

    private static void WriteLog(StreamWriter logWriter, object logLock, string message)
    {
        lock (logLock)
        {
            logWriter.WriteLine(message);
        }
    }
}
