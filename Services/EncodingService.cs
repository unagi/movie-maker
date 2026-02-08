using System;
using System.Diagnostics;
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
    string? ErrorMessage);

public static class EncodingService
{
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
            return new EncodeResult(false, request.OutputPath, request.LogPath, "ffmpegが見つかりません。");
        }

        try
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
            psi.ArgumentList.Add("-c:v");
            psi.ArgumentList.Add("libx264");
            psi.ArgumentList.Add("-tune");
            psi.ArgumentList.Add("stillimage");
            psi.ArgumentList.Add("-preset");
            psi.ArgumentList.Add("medium");
            psi.ArgumentList.Add("-crf");
            psi.ArgumentList.Add("18");
            psi.ArgumentList.Add("-vf");
            psi.ArgumentList.Add($"scale={request.Width}:{request.Height},setsar=1");
            psi.ArgumentList.Add("-r");
            psi.ArgumentList.Add("30");
            psi.ArgumentList.Add("-pix_fmt");
            psi.ArgumentList.Add("yuv420p");
            psi.ArgumentList.Add("-c:a");
            psi.ArgumentList.Add("aac");
            psi.ArgumentList.Add("-b:a");
            psi.ArgumentList.Add("320k");
            psi.ArgumentList.Add("-ar");
            psi.ArgumentList.Add("48000");
            psi.ArgumentList.Add("-shortest");
            psi.ArgumentList.Add("-movflags");
            psi.ArgumentList.Add("+faststart");
            psi.ArgumentList.Add(request.OutputPath);

            var logDirectory = Path.GetDirectoryName(request.LogPath);
            if (!string.IsNullOrWhiteSpace(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            using var logWriter = new StreamWriter(request.LogPath, false, Encoding.UTF8);
            var logLock = new object();

            using var process = new Process { StartInfo = psi };
            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data == null) return;
                lock (logLock)
                {
                    logWriter.WriteLine(args.Data);
                }
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data == null) return;
                lock (logLock)
                {
                    logWriter.WriteLine(args.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            lock (logLock)
            {
                logWriter.Flush();
            }

            if (process.ExitCode != 0 || !File.Exists(request.OutputPath))
            {
                return new EncodeResult(false, request.OutputPath, request.LogPath,
                    $"ffmpegが失敗しました (ExitCode: {process.ExitCode})");
            }

            return new EncodeResult(true, request.OutputPath, request.LogPath, null);
        }
        catch (Exception ex)
        {
            return new EncodeResult(false, request.OutputPath, request.LogPath, ex.Message);
        }
    }
}
