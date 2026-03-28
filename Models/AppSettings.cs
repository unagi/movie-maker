namespace MovieMaker.Models;

public sealed class AppSettings
{
    public string OutputDirectory { get; set; } = string.Empty;
    public string ArchiveDirectory { get; set; } = string.Empty;
    public double OneMinuteShortsOffsetSeconds { get; set; } = 3.0;
    public double ThreeMinuteShortsOffsetSeconds { get; set; } = 3.0;
}
