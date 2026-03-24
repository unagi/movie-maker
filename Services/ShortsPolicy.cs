namespace MovieMaker.Services;

public static class ShortsPolicy
{
    public const double StrongShortsLimitSeconds = 60.0;
    public const double StrongShortsTrimTriggerSeconds = 57.0;
    public const double StrongShortsSafeTargetSeconds = 57.0;
    public const double PlatformLimitSeconds = 180.0;
    public const double TrimTriggerSeconds = 177.0;
    public const double SafeTargetSeconds = 177.0;
    public const double FadeSeconds = 1.0;
    public const double TrailingSilenceDurationSeconds = 0.25;
    public const string TrailingSilenceThreshold = "-50dB";

    public static bool TryGetTrimTargetSeconds(double durationSeconds, out double targetSeconds)
    {
        if (durationSeconds >= TrimTriggerSeconds)
        {
            targetSeconds = SafeTargetSeconds;
            return true;
        }

        if (durationSeconds >= StrongShortsTrimTriggerSeconds && durationSeconds <= StrongShortsLimitSeconds)
        {
            targetSeconds = StrongShortsSafeTargetSeconds;
            return true;
        }

        targetSeconds = 0;
        return false;
    }
}
