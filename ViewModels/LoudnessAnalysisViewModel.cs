using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using MovieMaker.Infrastructure;
using MovieMaker.Models;
using MovieMaker.Services;

namespace MovieMaker.ViewModels;

public sealed class LoudnessAnalysisViewModel : INotifyPropertyChanged
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mov", ".mkv", ".avi", ".webm", ".wmv", ".mpeg", ".mpg", ".ts", ".m2ts", ".flv"
    };

    private readonly Func<string, Task<AudioLoudnessResult?>> _analyzer;
    private bool _isFileDragOver;
    private string _statusMessage = "動画を追加してください";
    private int _operationVersion;

    public LoudnessAnalysisViewModel(Func<string, Task<AudioLoudnessResult?>>? analyzer = null)
    {
        _analyzer = analyzer ?? (path => EncodingService.GetAudioLoudnessAsync(path));
        RemoveItemCommand = new RelayCommand(RemoveItem, parameter => parameter is LoudnessAnalysisItem);
        ClearCommand = new RelayCommand(_ => Clear(), _ => CanClear);
    }

    public ObservableCollection<LoudnessAnalysisItem> Items { get; } = [];

    public RelayCommand RemoveItemCommand { get; }
    public RelayCommand ClearCommand { get; }

    public bool IsFileDragOver
    {
        get => _isFileDragOver;
        private set
        {
            if (_isFileDragOver == value) return;
            _isFileDragOver = value;
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value) return;
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public bool HasItems => Items.Count > 0;
    public bool CanClear => HasItems;
    public bool HasWarnings => Items.Any(item => item.IsWarning);

    public string SummaryText
    {
        get
        {
            if (!HasItems)
            {
                return "解析対象の動画はありません";
            }

            var completed = Items.Count(item => item.IsAnalysisComplete);
            var warnings = Items.Count(item => item.IsWarning);
            var unavailable = Items.Count(item => item.IsAnalysisComplete && !item.IsAnalysisAvailable);
            if (completed < Items.Count)
            {
                return $"{Items.Count}件中 {completed}件を解析済み（解析中...）";
            }

            if (warnings > 0)
            {
                return $"解析完了: {Items.Count}件 / 要確認: {warnings}件";
            }

            if (unavailable > 0)
            {
                return $"解析完了: {Items.Count}件 / 解析不可: {unavailable}件";
            }

            return $"解析完了: {Items.Count}件 / 警告なし";
        }
    }

    public static bool IsSupportedVideoFile(string path)
    {
        return VideoExtensions.Contains(Path.GetExtension(path));
    }

    public void SetFileDragOver(bool isFileDragOver)
    {
        IsFileDragOver = isFileDragOver;
    }

    public async Task AddFilesAsync(IEnumerable<string> paths)
    {
        var operationVersion = ++_operationVersion;
        var addedItems = new List<LoudnessAnalysisItem>();
        var duplicateCount = 0;
        var unsupportedCount = 0;

        foreach (var path in paths ?? Array.Empty<string>())
        {
            if (!TryNormalizePath(path, out var normalizedPath) || !File.Exists(normalizedPath) ||
                !IsSupportedVideoFile(normalizedPath))
            {
                unsupportedCount++;
                continue;
            }

            if (Items.Any(item => string.Equals(item.Path, normalizedPath, StringComparison.OrdinalIgnoreCase)))
            {
                duplicateCount++;
                continue;
            }

            var item = new LoudnessAnalysisItem(normalizedPath);
            item.MarkAnalyzing();
            Items.Add(item);
            addedItems.Add(item);
        }

        NotifyStatusChanged();

        if (addedItems.Count == 0)
        {
            StatusMessage = BuildDropStatusMessage(0, duplicateCount, unsupportedCount);
            return;
        }

        var dropStatusMessage = BuildDropStatusMessage(addedItems.Count, duplicateCount, unsupportedCount);
        StatusMessage = dropStatusMessage;
        await Task.WhenAll(addedItems.Select(AnalyzeItemAsync));
        if (operationVersion == _operationVersion)
        {
            StatusMessage = $"{dropStatusMessage} / 解析完了";
        }
    }

    private async Task AnalyzeItemAsync(LoudnessAnalysisItem item)
    {
        AudioLoudnessResult? result = null;
        try
        {
            result = await _analyzer(item.Path);
        }
        catch
        {
            // Individual analysis failures are displayed as unavailable.
        }

        if (!Items.Contains(item))
        {
            return;
        }

        if (result == null)
        {
            item.MarkUnavailable();
        }
        else
        {
            var isLoudnessWarning = result.Status is AudioLoudnessStatus.TooQuiet or AudioLoudnessStatus.TooLoud;
            item.ApplyResult(
                FormatIntegratedLufs(result),
                isLoudnessWarning,
                FormatTruePeak(result),
                result.TruePeakStatus is AudioTruePeakStatus.TooHigh,
                FormatLoudnessRange(result));
        }

        NotifyStatusChanged();
    }

    private void RemoveItem(object? parameter)
    {
        if (parameter is not LoudnessAnalysisItem item || !Items.Remove(item))
        {
            return;
        }

        _operationVersion++;
        StatusMessage = $"動画を削除しました: {item.FileName}";
        NotifyStatusChanged();
    }

    private void Clear()
    {
        _operationVersion++;
        Items.Clear();
        StatusMessage = "動画を追加してください";
        NotifyStatusChanged();
    }

    private void NotifyStatusChanged()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(CanClear));
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(SummaryText));
        ClearCommand.RaiseCanExecuteChanged();
    }

    private static string BuildDropStatusMessage(int addedCount, int duplicateCount, int unsupportedCount)
    {
        var messages = new List<string>();
        if (addedCount > 0)
        {
            messages.Add($"動画{addedCount}件を追加しました");
        }

        if (duplicateCount > 0)
        {
            messages.Add($"重複動画{duplicateCount}件をスキップしました");
        }

        if (unsupportedCount > 0)
        {
            messages.Add($"非対応または存在しないファイル{unsupportedCount}件をスキップしました");
        }

        return messages.Count == 0 ? "動画を追加してください" : string.Join(" / ", messages);
    }

    private static string FormatIntegratedLufs(AudioLoudnessResult result)
    {
        var value = double.IsNegativeInfinity(result.IntegratedLufs)
            ? "-∞"
            : result.IntegratedLufs.ToString("0.0", CultureInfo.InvariantCulture);

        return result.Status switch
        {
            AudioLoudnessStatus.TooQuiet => $"{value} LUFS（小さすぎます）",
            AudioLoudnessStatus.TooLoud => $"{value} LUFS（大きすぎます）",
            _ => $"{value} LUFS（適正）"
        };
    }

    private static string FormatTruePeak(AudioLoudnessResult result)
    {
        if (result.TruePeakDbtp is not double truePeakDbtp)
        {
            return "解析不可";
        }

        var value = double.IsNegativeInfinity(truePeakDbtp)
            ? "-∞"
            : truePeakDbtp.ToString("0.0", CultureInfo.InvariantCulture);

        return result.TruePeakStatus switch
        {
            AudioTruePeakStatus.TooHigh => $"{value} dBTP（高すぎます）",
            _ => $"{value} dBTP（適正）"
        };
    }

    private static string FormatLoudnessRange(AudioLoudnessResult result)
    {
        return result.LoudnessRangeLu is double loudnessRangeLu
            ? loudnessRangeLu.ToString("0.0", CultureInfo.InvariantCulture) + " LU"
            : "解析不可";
    }

    private static bool TryNormalizePath(string? path, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            normalizedPath = Path.GetFullPath(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
