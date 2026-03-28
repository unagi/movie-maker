using MovieMaker.Models;

namespace MovieMaker.Services;

public static class ShortsPolicy
{
    public const double StrongShortsLimitSeconds = 60.0;
    public const double PlatformLimitSeconds = 180.0;
    public const double FadeSeconds = 1.0;
    public const double TrailingSilenceDurationSeconds = 0.25;
    public const string TrailingSilenceThreshold = "-50dB";

    public static double GetStrongShortsTrimTriggerSeconds(AppSettings? settings = null)
    {
        return StrongShortsLimitSeconds - GetOneMinuteOffsetSeconds(settings);
    }

    public static double GetStrongShortsSafeTargetSeconds(AppSettings? settings = null)
    {
        return GetStrongShortsTrimTriggerSeconds(settings);
    }

    public static double GetTrimTriggerSeconds(AppSettings? settings = null)
    {
        return PlatformLimitSeconds - GetThreeMinuteOffsetSeconds(settings);
    }

    public static double GetSafeTargetSeconds(AppSettings? settings = null)
    {
        return GetTrimTriggerSeconds(settings);
    }

    public static double GetOneMinuteOffsetSeconds(AppSettings? settings = null)
    {
        var value = (settings ?? SettingsService.Current).OneMinuteShortsOffsetSeconds;
        return Math.Clamp(Math.Round(value, 3), 0.0, 59.999);
    }

    public static double GetThreeMinuteOffsetSeconds(AppSettings? settings = null)
    {
        var value = (settings ?? SettingsService.Current).ThreeMinuteShortsOffsetSeconds;
        return Math.Clamp(Math.Round(value, 3), 0.0, 179.999);
    }

    public static bool TryGetTrimTargetSeconds(double durationSeconds, out double targetSeconds, AppSettings? settings = null)
    {
        var strongShortsTarget = GetStrongShortsSafeTargetSeconds(settings);
        var regularShortsTarget = GetSafeTargetSeconds(settings);

        if (durationSeconds >= regularShortsTarget)
        {
            targetSeconds = regularShortsTarget;
            return true;
        }

        if (durationSeconds >= strongShortsTarget && durationSeconds <= StrongShortsLimitSeconds)
        {
            targetSeconds = strongShortsTarget;
            return true;
        }

        targetSeconds = 0;
        return false;
    }
}
