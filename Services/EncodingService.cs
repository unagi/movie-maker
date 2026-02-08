using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using MovieMaker.Models;

namespace MovieMaker.Services;

public sealed record EncodeRequest(
    string FfmpegPath,
    string ImagePath,
    string AudioPath,
    string OutputPath,
    VideoOrientation Orientation,
    int Width,
    int Height,
    string LogPath);

public sealed record EncodeResult(
    bool Success,
    string OutputPath,
    string LogPath,
    string? ErrorMessage,
    string Encoder);

public static class EncodingService
{
    private const double ShortsLimitSeconds = 180.0;
    private const double ShortsTargetSeconds = 179.0;
    private const double ShortsFadeSeconds = 1.0;

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
            var trimShorts = await ShouldTrimShortsAsync(request, logWriter, logLock);
            foreach (var encoder in candidates)
            {
                var psi = BuildStartInfo(request, encoder, trimShorts);
                WriteLog(logWriter, logLock, $"Encoder: {encoder.DisplayName} ({encoder.Name})");

                var (exitCode, succeeded) = await RunProcessAsync(psi, logWriter, logLock);
                if (succeeded && File.Exists(request.OutputPath))
                {
                    WriteLog(logWriter, logLock, $"Success: {encoder.Name}");
                    return new EncodeResult(true, request.OutputPath, request.LogPath, null, encoder.DisplayName);
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

            return new EncodeResult(false, request.OutputPath, request.LogPath,
                "ffmpegが失敗しました (全てのエンコーダで失敗)", string.Empty);
        }
        catch (Exception ex)
        {
            return new EncodeResult(false, request.OutputPath, request.LogPath, ex.Message, string.Empty);
        }
    }

    private static ProcessStartInfo BuildStartInfo(EncodeRequest request, VideoEncoder encoder, bool trimShorts)
    {
        var psi = new ProcessStartInfo
        {
            FileName = request.FfmpegPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("-loop");
        psi.ArgumentList.Add("1");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(request.ImagePath);
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(request.AudioPath);

        AddVideoEncoderArgs(psi, encoder, request);

        var filter = BuildVideoFilter(request, trimShorts);
        psi.ArgumentList.Add("-vf");
        psi.ArgumentList.Add(filter);
        psi.ArgumentList.Add("-r");
        psi.ArgumentList.Add("30");
        psi.ArgumentList.Add("-pix_fmt");
        psi.ArgumentList.Add("yuv420p");

        if (trimShorts)
        {
            var fadeStart = ShortsTargetSeconds - ShortsFadeSeconds;
            psi.ArgumentList.Add("-af");
            psi.ArgumentList.Add($"afade=t=out:st={FormatSeconds(fadeStart)}:d={FormatSeconds(ShortsFadeSeconds)}");
        }

        psi.ArgumentList.Add("-c:a");
        psi.ArgumentList.Add("aac");
        psi.ArgumentList.Add("-b:a");
        psi.ArgumentList.Add("320k");
        psi.ArgumentList.Add("-ar");
        psi.ArgumentList.Add("48000");

        if (trimShorts)
        {
            psi.ArgumentList.Add("-t");
            psi.ArgumentList.Add(FormatSeconds(ShortsTargetSeconds));
        }

        psi.ArgumentList.Add("-shortest");
        psi.ArgumentList.Add("-movflags");
        psi.ArgumentList.Add("+faststart");
        psi.ArgumentList.Add(request.OutputPath);

        return psi;
    }

    private static string BuildVideoFilter(EncodeRequest request, bool trimShorts)
    {
        var filter = $"scale={request.Width}:{request.Height},setsar=1";
        if (trimShorts)
        {
            var fadeStart = ShortsTargetSeconds - ShortsFadeSeconds;
            filter += $",fade=t=out:st={FormatSeconds(fadeStart)}:d={FormatSeconds(ShortsFadeSeconds)}";
        }

        return filter;
    }

    private static async Task<bool> ShouldTrimShortsAsync(
        EncodeRequest request,
        StreamWriter logWriter,
        object logLock)
    {
        if (request.Orientation != VideoOrientation.Vertical)
        {
            return false;
        }

        var duration = await GetAudioDurationSecondsAsync(request, logWriter, logLock);
        if (!duration.HasValue)
        {
            return false;
        }

        if (duration.Value >= ShortsLimitSeconds)
        {
            WriteLog(logWriter, logLock, $"Shorts trim enabled. Duration={FormatSeconds(duration.Value)}s");
            return true;
        }

        WriteLog(logWriter, logLock, $"Shorts trim skipped. Duration={FormatSeconds(duration.Value)}s");
        return false;
    }

    private static async Task<double?> GetAudioDurationSecondsAsync(
        EncodeRequest request,
        StreamWriter logWriter,
        object logLock)
    {
        var ffprobePath = ResolveFfprobePath(request.FfmpegPath);
        if (ffprobePath == null)
        {
            WriteLog(logWriter, logLock, "ffprobe not found. Skip duration check.");
            return null;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffprobePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-v");
            psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-show_entries");
            psi.ArgumentList.Add("format=duration");
            psi.ArgumentList.Add("-of");
            psi.ArgumentList.Add("default=noprint_wrappers=1:nokey=1");
            psi.ArgumentList.Add(request.AudioPath);

            using var process = Process.Start(psi);
            if (process == null)
            {
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                WriteLog(logWriter, logLock, $"ffprobe failed: {error}");
                return null;
            }

            if (double.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            {
                return seconds;
            }

            WriteLog(logWriter, logLock, $"ffprobe duration parse failed: {output}");
            return null;
        }
        catch (Exception ex)
        {
            WriteLog(logWriter, logLock, $"ffprobe error: {ex.Message}");
            return null;
        }
    }

    private static string? ResolveFfprobePath(string ffmpegPath)
    {
        try
        {
            var ffmpegDir = Path.GetDirectoryName(ffmpegPath);
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

    private static string FormatSeconds(double seconds)
    {
        return seconds.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static void AddVideoEncoderArgs(ProcessStartInfo psi, VideoEncoder encoder, EncodeRequest request)
    {
        psi.ArgumentList.Add("-c:v");
        psi.ArgumentList.Add(encoder.Name);

        if (encoder == LibX264)
        {
            psi.ArgumentList.Add("-tune");
            psi.ArgumentList.Add("stillimage");
            psi.ArgumentList.Add("-preset");
            psi.ArgumentList.Add("medium");
            psi.ArgumentList.Add("-crf");
            psi.ArgumentList.Add("18");
            return;
        }

        if (encoder == Nvenc)
        {
            psi.ArgumentList.Add("-preset");
            psi.ArgumentList.Add("p5");
            psi.ArgumentList.Add("-rc");
            psi.ArgumentList.Add("vbr");
            psi.ArgumentList.Add("-cq");
            psi.ArgumentList.Add("19");
            psi.ArgumentList.Add("-b:v");
            psi.ArgumentList.Add("0");
            return;
        }

        if (encoder == Qsv)
        {
            psi.ArgumentList.Add("-preset");
            psi.ArgumentList.Add("medium");
            psi.ArgumentList.Add("-global_quality");
            psi.ArgumentList.Add("19");
            return;
        }

        if (encoder == Amf)
        {
            psi.ArgumentList.Add("-quality");
            psi.ArgumentList.Add("quality");
            psi.ArgumentList.Add("-rc");
            psi.ArgumentList.Add("cqp");
            psi.ArgumentList.Add("-qp_i");
            psi.ArgumentList.Add("19");
            psi.ArgumentList.Add("-qp_p");
            psi.ArgumentList.Add("19");
            psi.ArgumentList.Add("-qp_b");
            psi.ArgumentList.Add("19");
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
