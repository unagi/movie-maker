using System.Text.Json.Serialization;
using MovieMaker.Services;

namespace MovieMaker.Models;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ArchiveProject
{
    [JsonRequired] public string Format { get; set; } = "movie-maker-project";
    [JsonRequired] public int SchemaVersion { get; set; } = 1;
    [JsonRequired] public string Origin { get; set; } = "app";
    [JsonRequired] public string? Title { get; set; }
    [JsonRequired] public bool? UseDraftMode { get; set; }
    [JsonRequired] public EncodeProfile? Profile { get; set; }
    [JsonRequired] public VideoOrientation? Orientation { get; set; }
    [JsonRequired] public DraftAudioQuality? DraftAudioQuality { get; set; }
    [JsonRequired] public string? ImagePath { get; set; }
    [JsonRequired] public List<ArchiveTrack> Tracks { get; set; } = [];
    [JsonRequired] public ArchiveProcessingSettings Settings { get; set; } = new();
    [JsonRequired] public string? AppVersion { get; set; }
    [JsonRequired] public EncodingOptions? Preset { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ArchiveTrack
{
    [JsonRequired] public string AudioPath { get; set; } = string.Empty;
    [JsonRequired] public string OriginalFileName { get; set; } = string.Empty;
    [JsonRequired] public bool? IsNormalizationOverrideEnabled { get; set; }
    [JsonRequired] public double? TargetIntegratedLufs { get; set; }
    [JsonRequired] public double? TargetTruePeakDbtp { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ArchiveProcessingSettings
{
    [JsonRequired] public int? ShortsMaximumSeconds { get; set; }
    [JsonRequired] public double? OneMinuteShortsOffsetSeconds { get; set; }
    [JsonRequired] public double? ThreeMinuteShortsOffsetSeconds { get; set; }
    [JsonRequired] public double? NormalizationTargetIntegratedLufs { get; set; }
    [JsonRequired] public double? NormalizationTargetTruePeakDbtp { get; set; }
    [JsonRequired] public bool? NormalTextOverlayEnabled { get; set; }
    [JsonRequired] public string? TextOverlayLayoutJson { get; set; }
}
