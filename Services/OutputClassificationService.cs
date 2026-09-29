using System;

namespace MovieMaker.Services;

public enum OutputKind
{
    AwaitingImage,
    Normal,
    Shorts,
    InputError
}

public sealed record OutputClassification(OutputKind Kind, bool CanEncodeInputs,
    bool HasNormalLengthWarning, string Message);

public static class OutputClassificationService
{
    public static OutputClassification Classify(int imageWidth, int imageHeight, int trackCount,
        double? totalDurationSeconds, int shortsMaximumSeconds, bool audioAnalysisFailed = false)
    {
        if (shortsMaximumSeconds is < 1 or > 180)
            return new(OutputKind.InputError, false, false, "ショート動画の最大秒数は1～180秒に設定してください");

        if (imageWidth < 0 || imageHeight < 0 || trackCount < 0)
            return new(OutputKind.InputError, false, false, "入力情報が不正です");

        if (trackCount > 0 && (audioAnalysisFailed ||
            totalDurationSeconds.HasValue && !IsValidDuration(totalDurationSeconds)))
            return new(OutputKind.InputError, false, false, "音声の長さを取得できませんでした");

        if (imageWidth == 0 || imageHeight == 0)
        {
            var message = trackCount switch
            {
                0 => "入力待ち",
                1 when !totalDurationSeconds.HasValue => "判定中",
                1 when totalDurationSeconds <= shortsMaximumSeconds => "Shorts候補・画像待ち",
                _ => "通常動画候補・画像待ち"
            };
            return new(OutputKind.AwaitingImage, false, false, message);
        }

        if (imageHeight > imageWidth)
        {
            if (trackCount > 1)
                return new(OutputKind.InputError, false, false, "縦画像には音声を1本だけ指定してください");
            if (trackCount == 0)
                return new(OutputKind.Shorts, false, false, "Shorts確定・音声待ち");
            if (!IsValidDuration(totalDurationSeconds))
                return new(OutputKind.Shorts, false, false, "Shorts確定・音声時間を確認中");
            if (totalDurationSeconds > shortsMaximumSeconds)
                return new(OutputKind.InputError, false, false, "音声がショート動画の最大秒数を超えています");
            return new(OutputKind.Shorts, true, false, "Shorts");
        }

        if (trackCount == 0)
            return new(OutputKind.Normal, false, false, "通常動画・音声待ち");
        if (!IsValidDuration(totalDurationSeconds))
            return new(OutputKind.Normal, false, false, "通常動画・音声時間を確認中");

        var shortNormal = totalDurationSeconds <= shortsMaximumSeconds;
        return new(OutputKind.Normal, true, shortNormal,
            shortNormal ? "通常動画としては尺が短い" : "通常動画");
    }

    private static bool IsValidDuration(double? duration) => duration is > 0 &&
        double.IsFinite(duration.Value);
}
