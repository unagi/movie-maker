using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MovieMaker.Models;

namespace MovieMaker.Services;

public sealed record LoadedArchiveProject(ArchiveProject Project, string DirectoryPath,
    string? ImageFullPath, IReadOnlyList<string> AudioFullPaths);

public static class ArchiveProjectService
{
    public const string FileName = "movie-maker-project.json";
    private const int MaximumBytes = 16 * 1024 * 1024;
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".wav", ".m4a", ".flac", ".aac" };
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp" };
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = true, MaxDepth = 32
        };
        options.Converters.Add(new JsonStringEnumConverter<EncodeProfile>(allowIntegerValues: false));
        options.Converters.Add(new JsonStringEnumConverter<VideoOrientation>(allowIntegerValues: false));
        options.Converters.Add(new JsonStringEnumConverter<DraftAudioQuality>(allowIntegerValues: false));
        return options;
    }

    public static LoadedArchiveProject Load(string jsonPath)
    {
        if (new FileInfo(jsonPath).Length > MaximumBytes)
            throw new InvalidDataException("復元用設定が大きすぎます。");
        var bytes = File.ReadAllBytes(jsonPath);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("復元用設定が大きすぎます。");
        // Accept UTF-8 BOM, while rejecting duplicate properties before typed deserialization.
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) bytes = bytes[3..];
        using (var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 }))
        {
            RejectDuplicates(document.RootElement);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("preset", out var preset) && preset.ValueKind != JsonValueKind.Null)
            {
                string[] requiredNames = ["width", "height", "frameRate", "audioBitrate", "audioSampleRate",
                    "libX264Preset", "libX264Crf", "nvencPreset", "nvencCq", "qsvPreset", "qsvGlobalQuality", "amfQuality", "amfQp"];
                if (preset.ValueKind != JsonValueKind.Object || requiredNames.Any(name => !preset.TryGetProperty(name, out _)))
                    throw new InvalidDataException("保存時の品質設定に必須項目がありません。");
            }
        }
        var project = JsonSerializer.Deserialize<ArchiveProject>(bytes, Options)
            ?? throw new InvalidDataException("復元用設定が空です。");
        Validate(project);
        var directory = CanonicalPath(Path.GetDirectoryName(Path.GetFullPath(jsonPath))!);
        var image = project.ImagePath == null ? null : ResolveAsset(directory, project.ImagePath, ImageExtensions);
        var audio = project.Tracks.Select(track => ResolveAsset(directory, track.AudioPath, AudioExtensions)).ToArray();
        if (audio.Distinct(StringComparer.OrdinalIgnoreCase).Count() != audio.Length)
            throw new InvalidDataException("同じ音声素材が重複しています。");
        return new(project, directory, image, audio);
    }

    public static ArchiveProject Clone(ArchiveProject project) =>
        JsonSerializer.Deserialize<ArchiveProject>(JsonSerializer.SerializeToUtf8Bytes(project, Options), Options)!;

    public static void Save(string directory, ArchiveProject project, bool overwrite = false)
    {
        Validate(project);
        Directory.CreateDirectory(directory);
        directory = CanonicalPath(directory);
        if (project.ImagePath != null) ResolveAsset(directory, project.ImagePath, ImageExtensions);
        var audio = project.Tracks.Select(track => ResolveAsset(directory, track.AudioPath, AudioExtensions)).ToArray();
        if (audio.Distinct(StringComparer.OrdinalIgnoreCase).Count() != audio.Length)
            throw new InvalidDataException("同じ音声素材が重複しています。");
        var target = Path.Combine(directory, FileName);
        if (File.Exists(target) && !overwrite) throw new IOException("復元用設定は既に存在します。");
        if (File.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("リンクされた復元用設定には保存できません。");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(project, Options);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("復元用設定が大きすぎます。");
        var temporary = Path.Combine(directory, ".movie-maker-project-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(flushToDisk: true); }
            File.Move(temporary, target, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static IReadOnlyList<string> GetMissingFields(ArchiveProject project)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(project.Title)) missing.Add("タイトル");
        if (project.UseDraftMode == null) missing.Add("画像モード／Draft");
        if (project.Profile == null) missing.Add("処理モード");
        if (project.Orientation == null) missing.Add("画像の向き");
        if (project.UseDraftMode == true || project.Profile == EncodeProfile.DraftPreview)
        {
            if (project.DraftAudioQuality == null) missing.Add("Draft音声品質");
            return missing;
        }
        if (project.ImagePath == null) missing.Add("背景画像");
        var settings = project.Settings;
        if (settings.ShortsMaximumSeconds is not (>= 1 and <= 180)) missing.Add("Shorts上限秒数（1～180）");
        if (!InRange(settings.OneMinuteShortsOffsetSeconds, 0, 59)) missing.Add("1分Shorts余裕秒数（0～59）");
        if (!InRange(settings.ThreeMinuteShortsOffsetSeconds, 0, 120, exclusiveMaximum: true)) missing.Add("3分Shorts余裕秒数（0以上120未満）");
        if (project.Profile == EncodeProfile.CopyrightCheckProduction)
        {
            if (project.Tracks.Count != 1) missing.Add("Shortsは音声1曲");
            if (project.Orientation != VideoOrientation.Vertical) missing.Add("Shortsは縦画像");
        }
        if (project.Profile == EncodeProfile.Standard)
        {
            if (project.Orientation != VideoOrientation.Horizontal) missing.Add("通常モードは横画像");
            if (!InRange(settings.NormalizationTargetIntegratedLufs, -70, -5)) missing.Add("共通LUFS（-70～-5）");
            if (!InRange(settings.NormalizationTargetTruePeakDbtp, -8, 0)) missing.Add("共通True Peak（-8～0）");
            if (settings.NormalTextOverlayEnabled == null) missing.Add("文字合成の有効／無効");
            if (settings.NormalTextOverlayEnabled == true &&
                (string.IsNullOrWhiteSpace(settings.TextOverlayLayoutJson) ||
                 !TextOverlayService.TryParse(settings.TextOverlayLayoutJson, out _, out _))) missing.Add("文字合成レイアウト");
            for (var index = 0; index < project.Tracks.Count; index++)
            {
                var track = project.Tracks[index];
                if (track.IsNormalizationOverrideEnabled == null) missing.Add($"音声{index + 1}の共通／個別目標");
                else if (track.IsNormalizationOverrideEnabled == true &&
                    (!InRange(track.TargetIntegratedLufs, -70, -5) || !InRange(track.TargetTruePeakDbtp, -8, 0)))
                    missing.Add($"音声{index + 1}の個別目標");
            }
        }
        return missing;
    }

    private static bool InRange(double? value, double minimum, double maximum, bool exclusiveMaximum = false) =>
        value is double number && double.IsFinite(number) && number >= minimum &&
        (exclusiveMaximum ? number < maximum : number <= maximum);

    private static void Validate(ArchiveProject project)
    {
        if (project.Format != "movie-maker-project" || project.SchemaVersion != 1 ||
            project.Origin is not ("app" or "archive-scan")) throw new InvalidDataException("未対応の復元用設定です。");
        if (project.Settings == null || project.Tracks == null || project.Tracks.Count == 0 || project.Tracks.Any(track => track == null))
            throw new InvalidDataException("音声一覧または処理設定が不正です。");
        if (project.Title != null && project.Title.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("タイトルに使用できない文字があります。");
        if (project.Profile.HasValue && !Enum.IsDefined(project.Profile.Value) ||
            project.Orientation.HasValue && !Enum.IsDefined(project.Orientation.Value) ||
            project.DraftAudioQuality.HasValue && !Enum.IsDefined(project.DraftAudioQuality.Value))
            throw new InvalidDataException("処理モードの値が不正です。");
        if (project.Preset is { } preset && (preset.Width <= 0 || preset.Height <= 0 || preset.FrameRate <= 0 ||
            string.IsNullOrWhiteSpace(preset.AudioBitrate) || string.IsNullOrWhiteSpace(preset.AudioSampleRate) ||
            string.IsNullOrWhiteSpace(preset.LibX264Preset) || string.IsNullOrWhiteSpace(preset.NvencPreset) ||
            string.IsNullOrWhiteSpace(preset.QsvPreset) || string.IsNullOrWhiteSpace(preset.AmfQuality)))
            throw new InvalidDataException("保存時の品質設定が不正です。");
        if (project.UseDraftMode == true && project.Profile.HasValue && project.Profile != EncodeProfile.DraftPreview ||
            project.UseDraftMode == false && project.Profile == EncodeProfile.DraftPreview ||
            project.Profile == EncodeProfile.DraftPreview && project.Orientation == VideoOrientation.Vertical ||
            project.Profile == EncodeProfile.Standard && project.Orientation == VideoOrientation.Vertical ||
            project.Profile == EncodeProfile.CopyrightCheckProduction && project.Orientation == VideoOrientation.Horizontal)
            throw new InvalidDataException("処理モードと画像の向きが矛盾しています。");
        foreach (var track in project.Tracks)
        {
            if (string.IsNullOrWhiteSpace(track.OriginalFileName) || track.OriginalFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                !AudioExtensions.Contains(Path.GetExtension(track.OriginalFileName))) throw new InvalidDataException("元の音声ファイル名が不正です。");
        }
        // Missing values are intentional in scanned archives; known active values must be valid.
        if (project.UseDraftMode == false)
        {
            var s = project.Settings;
            if (s.ShortsMaximumSeconds.HasValue && s.ShortsMaximumSeconds is not (>= 1 and <= 180) ||
                s.OneMinuteShortsOffsetSeconds.HasValue && !InRange(s.OneMinuteShortsOffsetSeconds, 0, 59) ||
                s.ThreeMinuteShortsOffsetSeconds.HasValue && !InRange(s.ThreeMinuteShortsOffsetSeconds, 0, 120, true))
                throw new InvalidDataException("Shorts設定値が範囲外です。");
            if (project.Profile == EncodeProfile.Standard)
            {
                if (s.NormalizationTargetIntegratedLufs.HasValue && !InRange(s.NormalizationTargetIntegratedLufs, -70, -5) ||
                    s.NormalizationTargetTruePeakDbtp.HasValue && !InRange(s.NormalizationTargetTruePeakDbtp, -8, 0))
                    throw new InvalidDataException("共通の音声目標値が範囲外です。");
                foreach (var track in project.Tracks.Where(track => track.IsNormalizationOverrideEnabled == true))
                    if (track.TargetIntegratedLufs.HasValue && !InRange(track.TargetIntegratedLufs, -70, -5) ||
                        track.TargetTruePeakDbtp.HasValue && !InRange(track.TargetTruePeakDbtp, -8, 0)) throw new InvalidDataException("個別の音声目標値が範囲外です。");
                if (s.NormalTextOverlayEnabled == true && s.TextOverlayLayoutJson != null &&
                    !TextOverlayService.TryParse(s.TextOverlayLayoutJson, out _, out var error)) throw new InvalidDataException(error);
            }
        }
    }

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("JSONのキーが重複しています。");
                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicates(item);
    }

    private static string ResolveAsset(string directory, string relativePath, HashSet<string> extensions)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)) throw new InvalidDataException("素材は相対パスで指定してください。");
        var segments = relativePath.Replace('/', '\\').Split('\\');
        if (segments.Any(segment => segment is "" or "." or ".." || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new InvalidDataException("素材の相対パスが不正です。");
        var full = CanonicalPath(Path.Combine(directory, relativePath));
        if (!full.StartsWith(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("素材がアーカイブの外部を参照しています。");
        if (!File.Exists(full) || !extensions.Contains(Path.GetExtension(full))) throw new InvalidDataException("素材が存在しないか形式が未対応です。");
        using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        stream.ReadByte();
        return full;
    }

    private static string CanonicalPath(string path)
    {
        var full = Path.GetFullPath(path);
        var current = Path.GetPathRoot(full)!;
        foreach (var segment in full[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo entry = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (entry.Exists && (entry.Attributes & FileAttributes.ReparsePoint) != 0)
                current = entry.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? throw new InvalidDataException("素材のリンクを解決できません。");
        }
        return Path.TrimEndingDirectorySeparator(current);
    }
}
