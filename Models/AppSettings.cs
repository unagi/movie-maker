namespace MovieMaker.Models;

public sealed class AppSettings
{
    public string OutputDirectory { get; set; } = string.Empty;
    public string ArchiveDirectory { get; set; } = string.Empty;
    public double OneMinuteShortsOffsetSeconds { get; set; } = 3.0;
    public double ThreeMinuteShortsOffsetSeconds { get; set; } = 3.0;
    public int ShortsMaximumSeconds { get; set; } = 60;
    public bool NormalTextOverlayEnabled { get; set; } = true;
    public double NormalizationTargetIntegratedLufs { get; set; } = -14.0;
    public double NormalizationTargetTruePeakDbtp { get; set; } = -1.0;
    public string TextOverlayLayoutJson { get; set; } = string.Empty;
}
