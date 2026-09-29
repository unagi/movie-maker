using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MediaColor = System.Windows.Media.Color;
using MediaFontFamily = System.Windows.Media.FontFamily;
using MediaPen = System.Windows.Media.Pen;
using MediaBrushes = System.Windows.Media.Brushes;

namespace MovieMaker.Services;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TextOverlayConfiguration
{
    [JsonRequired]
    public int SchemaVersion { get; init; }

    [JsonRequired]
    public required TextOverlayCanvas Canvas { get; init; }

    [JsonRequired]
    public required List<TextOverlayZone> Zones { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TextOverlayCanvas
{
    [JsonRequired]
    public int Width { get; init; }

    [JsonRequired]
    public int Height { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TextOverlayZone
{
    [JsonRequired]
    public required string Id { get; init; }

    [JsonRequired]
    public required int[] Bounds { get; init; }

    [JsonRequired]
    public required string WritingMode { get; init; }

    [JsonRequired]
    public required string HorizontalAlign { get; init; }

    [JsonRequired]
    public required string VerticalAlign { get; init; }

    [JsonRequired]
    public required TextOverlayFont Font { get; init; }

    [JsonRequired]
    public required TextOverlayPaint Paint { get; init; }

    [JsonRequired]
    public required TextOverlayFit Fit { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TextOverlayFont
{
    [JsonRequired]
    public required string Id { get; init; }

    [JsonRequired]
    public int Weight { get; init; }

    [JsonRequired]
    public double SizePx { get; init; }

    [JsonRequired]
    public double MinSizePx { get; init; }

    [JsonRequired]
    public double LetterSpacingPx { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TextOverlayPaint
{
    [JsonRequired]
    public required string Fill { get; init; }

    public TextOverlayStroke? Stroke { get; init; }

    public TextOverlayShadow? Shadow { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TextOverlayStroke
{
    [JsonRequired]
    public required string Color { get; init; }

    [JsonRequired]
    public double WidthPx { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TextOverlayShadow
{
    [JsonRequired]
    public required string Color { get; init; }

    [JsonRequired]
    public double OffsetX { get; init; }

    [JsonRequired]
    public double OffsetY { get; init; }

    [JsonRequired]
    public double BlurPx { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TextOverlayFit
{
    [JsonRequired]
    public int MaxLines { get; init; }

    [JsonRequired]
    public required string Overflow { get; init; }
}

public static class TextOverlayService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false
    };

    private static readonly Regex ColorPattern = new("^#[0-9A-Fa-f]{8}$", RegexOptions.Compiled);
    private const string SourceHanFontFileName = "SourceHanSansJP-Heavy.otf";

    public static Task<IReadOnlyList<string>> CreateTrackImagesAsync(string backgroundPath,
        string outputDirectory, IReadOnlyList<string> titles, TextOverlayConfiguration configuration)
    {
        var completion = new TaskCompletionSource<IReadOnlyList<string>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var typeface = LoadTypeface();
                var background = LoadBackground(backgroundPath);
                var paths = new string[titles.Count];
                for (var index = 0; index < titles.Count; index++)
                {
                    paths[index] = CreateTrackImage(background, typeface,
                        Path.Combine(outputDirectory, $"track-{index + 1:D4}.png"), titles[index], configuration);
                }
                completion.SetResult(paths);
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "MovieMaker text overlay renderer"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    public static bool TryParse(string json, out TextOverlayConfiguration? configuration, out string? error)
    {
        configuration = null;
        error = null;
        try
        {
            configuration = JsonSerializer.Deserialize<TextOverlayConfiguration>(json, JsonOptions);
            if (configuration == null)
            {
                error = "JSONのルートはオブジェクトで指定してください。";
                return false;
            }

            error = Validate(configuration);
            if (error != null)
            {
                configuration = null;
                return false;
            }

            return true;
        }
        catch (JsonException ex)
        {
            var location = ex.LineNumber.HasValue
                ? $"（{ex.LineNumber.Value + 1}行 {ex.BytePositionInLine.GetValueOrDefault() + 1}列）"
                : string.Empty;
            error = $"JSONを読み取れません{location}: {ex.Message}";
            return false;
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException)
        {
            error = $"JSONを読み取れません: {ex.Message}";
            return false;
        }
    }

    public static string? Validate(TextOverlayConfiguration configuration)
    {
        if (configuration.Canvas == null || configuration.Zones == null)
        {
            return "canvas と zones を指定してください。";
        }

        if (configuration.SchemaVersion != 1)
        {
            return "schemaVersion は 1 を指定してください。";
        }

        if (configuration.Canvas.Width < 1 || configuration.Canvas.Height < 1)
        {
            return "canvas.width と canvas.height は1以上の整数を指定してください。";
        }

        if (Math.Abs((double)configuration.Canvas.Width / configuration.Canvas.Height - 16.0 / 9.0) > 0.01)
        {
            return "canvas は16:9のサイズで指定してください。";
        }

        if (configuration.Zones.Count == 0)
        {
            return "zones を1件以上指定してください。";
        }

        foreach (var zone in configuration.Zones)
        {
            if (zone == null || zone.Bounds == null || zone.Font == null || zone.Paint == null || zone.Fit == null)
            {
                return "zones の各要素に bounds、font、paint、fit を指定してください。";
            }
            if (string.IsNullOrWhiteSpace(zone.Id))
            {
                return "zones[].id は空にできません。";
            }

            if (zone.Bounds.Length != 4 || zone.Bounds[0] < 0 || zone.Bounds[1] < 0 ||
                zone.Bounds[2] < 1 || zone.Bounds[3] < 1 ||
                (long)zone.Bounds[0] + zone.Bounds[2] > configuration.Canvas.Width ||
                (long)zone.Bounds[1] + zone.Bounds[3] > configuration.Canvas.Height)
            {
                return $"ゾーン「{zone.Id}」の bounds は canvas 内の [x, y, width, height] で指定してください。";
            }

            if (zone.WritingMode is not ("horizontal-tb" or "vertical-rl"))
            {
                return $"ゾーン「{zone.Id}」の writingMode は horizontal-tb または vertical-rl を指定してください。";
            }

            if (zone.HorizontalAlign is not ("left" or "center" or "right") ||
                zone.VerticalAlign is not ("top" or "center" or "bottom"))
            {
                return $"ゾーン「{zone.Id}」の horizontalAlign / verticalAlign を確認してください。";
            }

            if (!string.Equals(zone.Font.Id, "source-han-sans-jp-heavy", StringComparison.Ordinal))
            {
                return $"ゾーン「{zone.Id}」の font.id は source-han-sans-jp-heavy を指定してください。";
            }

            if (zone.Font.Weight is < 100 or > 900 || zone.Font.Weight % 100 != 0 ||
                !double.IsFinite(zone.Font.SizePx) || zone.Font.SizePx <= 0 ||
                !double.IsFinite(zone.Font.MinSizePx) || zone.Font.MinSizePx <= 0 ||
                zone.Font.MinSizePx > zone.Font.SizePx || !double.IsFinite(zone.Font.LetterSpacingPx))
            {
                return $"ゾーン「{zone.Id}」の font 設定を確認してください。";
            }

            if (!IsColor(zone.Paint.Fill) ||
                (zone.Paint.Stroke != null && (!IsColor(zone.Paint.Stroke.Color) ||
                                                !double.IsFinite(zone.Paint.Stroke.WidthPx) || zone.Paint.Stroke.WidthPx < 0)) ||
                (zone.Paint.Shadow != null && (!IsColor(zone.Paint.Shadow.Color) ||
                                                !double.IsFinite(zone.Paint.Shadow.OffsetX) ||
                                                !double.IsFinite(zone.Paint.Shadow.OffsetY) ||
                                                !double.IsFinite(zone.Paint.Shadow.BlurPx) || zone.Paint.Shadow.BlurPx < 0)))
            {
                return $"ゾーン「{zone.Id}」の paint 設定を確認してください。色は #AARRGGBB 形式です。";
            }

            if (zone.Fit.MaxLines < 1 || zone.Fit.Overflow != "reject")
            {
                return $"ゾーン「{zone.Id}」の fit は maxLines を1以上、overflow を reject にしてください。";
            }
        }

        return null;
    }

    private static BitmapSource LoadBackground(string backgroundPath)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(backgroundPath);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static Typeface LoadTypeface()
    {
        var fontDirectories = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts")
        };
        foreach (var directory in fontDirectories)
        {
            var fontPath = Path.Combine(directory, SourceHanFontFileName);
            if (!File.Exists(fontPath))
            {
                continue;
            }

            var glyphTypeface = new GlyphTypeface(new Uri(fontPath));
            var familyName = glyphTypeface.FamilyNames.TryGetValue(CultureInfo.GetCultureInfo("en-US"), out var englishName)
                ? englishName
                : glyphTypeface.FamilyNames.Values.First();
            var family = new MediaFontFamily(new Uri(directory + Path.DirectorySeparatorChar), "./#" + familyName);
            var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Black, FontStretches.Normal);
            if (typeface.TryGetGlyphTypeface(out var resolved) &&
                string.Equals(Path.GetFullPath(resolved.FontUri.LocalPath), Path.GetFullPath(fontPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                return typeface;
            }
        }

        throw new InvalidOperationException(
            $"フォント「Source Han Sans JP Heavy」が見つかりません。Windowsへ {SourceHanFontFileName} をインストールしてからアプリを再起動してください。");
    }

    public static string CreateTrackImage(string backgroundPath, string outputPath, string title,
        TextOverlayConfiguration configuration)
    {
        return CreateTrackImage(LoadBackground(backgroundPath), LoadTypeface(), outputPath, title, configuration);
    }

    private static string CreateTrackImage(BitmapSource background, Typeface typeface, string outputPath,
        string title, TextOverlayConfiguration configuration)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(MediaBrushes.Black, null,
                new Rect(0, 0, configuration.Canvas.Width, configuration.Canvas.Height));
            var scale = Math.Min((double)configuration.Canvas.Width / background.PixelWidth,
                (double)configuration.Canvas.Height / background.PixelHeight);
            var width = background.PixelWidth * scale;
            var height = background.PixelHeight * scale;
            drawing.DrawImage(background, new Rect((configuration.Canvas.Width - width) / 2,
                (configuration.Canvas.Height - height) / 2, width, height));
            foreach (var zone in configuration.Zones)
            {
                DrawZone(drawing, title, zone, typeface);
            }
        }

        var bitmap = new RenderTargetBitmap(configuration.Canvas.Width, configuration.Canvas.Height,
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var stream = File.Create(outputPath);
        encoder.Save(stream);
        return outputPath;
    }

    private static void DrawZone(DrawingContext drawing, string text, TextOverlayZone zone, Typeface typeface)
    {
        var bounds = new Rect(zone.Bounds[0], zone.Bounds[1], zone.Bounds[2], zone.Bounds[3]);
        var fontSize = zone.Font.SizePx;
        var vertical = zone.WritingMode == "vertical-rl";
        var longestUnbrokenColumn = vertical ? LongestUnbrokenRun(text) : 0;
        var canAvoidAutomaticColumnWrap = vertical &&
            ColumnExtent(longestUnbrokenColumn, zone.Font.MinSizePx, zone.Font.LetterSpacingPx) <= bounds.Height;
        while (true)
        {
            if (canAvoidAutomaticColumnWrap &&
                ColumnExtent(longestUnbrokenColumn, fontSize, zone.Font.LetterSpacingPx) > bounds.Height)
            {
                fontSize = Math.Max(zone.Font.MinSizePx, fontSize - 1);
                continue;
            }

            var glyphSize = fontSize;
            List<List<Glyph>> groups;
            bool cellFits;
            do
            {
                groups = vertical
                    ? BuildVerticalColumns(text, zone, typeface, glyphSize, fontSize)
                    : BuildHorizontalLines(text, zone, typeface, glyphSize);
                cellFits = !vertical || groups.SelectMany(group => group).All(glyph =>
                    glyph.Ink.IsEmpty ||
                    glyph.Ink.Width <= fontSize + 0.01 && glyph.Ink.Height <= fontSize + 0.01);
                if (cellFits || glyphSize <= zone.Font.MinSizePx)
                {
                    break;
                }
                glyphSize = Math.Max(zone.Font.MinSizePx, glyphSize - 1);
            } while (true);

            var lineStep = MeasureGlyph("国", typeface, glyphSize).LineHeight;
            var logicalPrimary = groups.Count * (vertical ? fontSize : lineStep);
            var logicalSecondary = groups.Max(group => GroupExtent(group, vertical, fontSize,
                zone.Font.LetterSpacingPx));
            var logicalFits = groups.Count <= zone.Fit.MaxLines && cellFits &&
                logicalPrimary <= (vertical ? bounds.Width : bounds.Height) + 0.01 &&
                logicalSecondary <= (vertical ? bounds.Height : bounds.Width) + 0.01;
            if (logicalFits)
            {
                var positioned = PositionGlyphs(groups, zone, bounds, fontSize, lineStep);
                var paintedBounds = BoundsOf(positioned, zone.Paint);
                if (paintedBounds.IsEmpty || Contains(bounds, paintedBounds))
                {
                    foreach (var item in positioned)
                    {
                        DrawGlyph(drawing, item, zone.Paint);
                    }
                    return;
                }
            }

            if (fontSize <= zone.Font.MinSizePx)
            {
                throw new InvalidOperationException($"タイトル「{text}」がゾーン「{zone.Id}」に収まりません。");
            }
            fontSize = Math.Max(zone.Font.MinSizePx, fontSize - 1);
        }
    }

    private static List<List<Glyph>> BuildHorizontalLines(string text, TextOverlayZone zone,
        Typeface typeface, double fontSize)
    {
        var lines = new List<List<Glyph>> { new() };
        foreach (var element in EnumerateTextElements(text))
        {
            if (element == "\n")
            {
                lines.Add([]);
                continue;
            }
            var glyph = MeasureGlyph(element, typeface, fontSize);
            var current = lines[^1];
            if (current.Count > 0 &&
                GroupExtent(current, false, fontSize, zone.Font.LetterSpacingPx) +
                zone.Font.LetterSpacingPx + glyph.Advance > zone.Bounds[2])
            {
                lines.Add([]);
                current = lines[^1];
            }
            current.Add(glyph);
        }
        return lines;
    }

    private static List<List<Glyph>> BuildVerticalColumns(string text, TextOverlayZone zone,
        Typeface typeface, double glyphSize, double cellSize)
    {
        var columns = new List<List<Glyph>> { new() };
        foreach (var element in EnumerateTextElements(text))
        {
            if (element == "\n")
            {
                columns.Add([]);
                continue;
            }
            var glyph = MeasureGlyph(element, typeface, glyphSize);
            var current = columns[^1];
            if (current.Count > 0 &&
                GroupExtent(current, true, cellSize, zone.Font.LetterSpacingPx) +
                zone.Font.LetterSpacingPx + cellSize > zone.Bounds[3])
            {
                columns.Add([]);
                current = columns[^1];
            }
            current.Add(glyph);
        }
        return columns;
    }

    private static double GroupExtent(IReadOnlyList<Glyph> glyphs, bool vertical,
        double fontSize, double spacing) =>
        glyphs.Sum(glyph => vertical ? fontSize : glyph.Advance) +
        Math.Max(0, glyphs.Count - 1) * spacing;

    private static double ColumnExtent(int glyphCount, double cellSize, double spacing) =>
        glyphCount * cellSize + Math.Max(0, glyphCount - 1) * spacing;

    private static int LongestUnbrokenRun(string text)
    {
        var longest = 0;
        var current = 0;
        foreach (var element in EnumerateTextElements(text))
        {
            if (element == "\n")
            {
                current = 0;
            }
            else
            {
                current++;
                longest = Math.Max(longest, current);
            }
        }
        return longest;
    }

    private static List<string> EnumerateTextElements(string text)
    {
        var elements = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            elements.Add((string)enumerator.Current!);
        }
        return elements;
    }

    private static Glyph MeasureGlyph(string text, Typeface typeface, double fontSize)
    {
        var formatted = new FormattedText(text, CultureInfo.GetCultureInfo("ja-JP"),
            System.Windows.FlowDirection.LeftToRight, typeface, fontSize, MediaBrushes.White, 1.0);
        var outline = formatted.BuildGeometry(new System.Windows.Point());
        return new Glyph(formatted.WidthIncludingTrailingWhitespace, formatted.Height,
            outline, outline.Bounds);
    }

    private static List<PositionedGlyph> PositionGlyphs(List<List<Glyph>> groups,
        TextOverlayZone zone, Rect bounds, double fontSize, double lineStep)
    {
        var vertical = zone.WritingMode == "vertical-rl";
        var positioned = new List<PositionedGlyph>();
        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var firstIndex = positioned.Count;
            var cursor = 0.0;
            foreach (var glyph in groups[groupIndex])
            {
                if (vertical)
                {
                    var cellX = -groupIndex * fontSize;
                    var x = glyph.Ink.IsEmpty ? cellX :
                        cellX + fontSize / 2 - (glyph.Ink.Left + glyph.Ink.Width / 2);
                    var y = glyph.Ink.IsEmpty ? cursor :
                        cursor + fontSize / 2 - (glyph.Ink.Top + glyph.Ink.Height / 2);
                    positioned.Add(new PositionedGlyph(glyph, x, y));
                    cursor += fontSize + zone.Font.LetterSpacingPx;
                }
                else
                {
                    positioned.Add(new PositionedGlyph(glyph, cursor, groupIndex * lineStep));
                    cursor += glyph.Advance + zone.Font.LetterSpacingPx;
                }
            }
            var groupItems = positioned.GetRange(firstIndex, positioned.Count - firstIndex);
            var groupBounds = BoundsOf(groupItems, zone.Paint);
            if (groupBounds.IsEmpty)
            {
                continue;
            }
            var secondaryOffset = vertical
                ? AlignOffset(zone.VerticalAlign, bounds.Y, bounds.Height, groupBounds.Height) - groupBounds.Top
                : AlignOffset(zone.HorizontalAlign, bounds.X, bounds.Width, groupBounds.Width) - groupBounds.Left;
            for (var index = firstIndex; index < positioned.Count; index++)
            {
                var item = positioned[index];
                positioned[index] = vertical
                    ? item with { Y = item.Y + secondaryOffset }
                    : item with { X = item.X + secondaryOffset };
            }
        }

        var blockBounds = BoundsOf(positioned, zone.Paint);
        if (!blockBounds.IsEmpty)
        {
            var primaryOffset = vertical
                ? AlignOffset(zone.HorizontalAlign, bounds.X, bounds.Width, blockBounds.Width) - blockBounds.Left
                : AlignOffset(zone.VerticalAlign, bounds.Y, bounds.Height, blockBounds.Height) - blockBounds.Top;
            for (var index = 0; index < positioned.Count; index++)
            {
                var item = positioned[index];
                positioned[index] = vertical
                    ? item with { X = item.X + primaryOffset }
                    : item with { Y = item.Y + primaryOffset };
            }
        }
        return positioned;
    }

    private static Rect BoundsOf(IEnumerable<PositionedGlyph> items, TextOverlayPaint paint)
    {
        var result = Rect.Empty;
        foreach (var item in items)
        {
            var ink = item.Glyph.Ink;
            if (ink.IsEmpty)
            {
                continue;
            }
            ink.Offset(item.X, item.Y);
            if (paint.Stroke is { WidthPx: > 0 } stroke)
            {
                ink.Inflate(stroke.WidthPx / 2, stroke.WidthPx / 2);
            }
            result.Union(ink);
            if (paint.Shadow is { } shadow)
            {
                var shadowInk = item.Glyph.Ink;
                shadowInk.Offset(item.X + shadow.OffsetX, item.Y + shadow.OffsetY);
                shadowInk.Inflate(shadow.BlurPx / 2, shadow.BlurPx / 2);
                result.Union(shadowInk);
            }
        }
        return result;
    }

    private static bool Contains(Rect zone, Rect painted) =>
        painted.Left >= zone.Left - 0.01 && painted.Top >= zone.Top - 0.01 &&
        painted.Right <= zone.Right + 0.01 && painted.Bottom <= zone.Bottom + 0.01;

    private static void DrawGlyph(DrawingContext drawing, PositionedGlyph item, TextOverlayPaint paint)
    {
        drawing.PushTransform(new TranslateTransform(item.X, item.Y));
        var geometry = item.Glyph.Outline;
        if (paint.Shadow is { } shadow)
        {
            var color = ParseColor(shadow.Color);
            if (shadow.BlurPx <= 0)
            {
                drawing.PushTransform(new TranslateTransform(shadow.OffsetX, shadow.OffsetY));
                drawing.DrawGeometry(new SolidColorBrush(color), null, geometry);
                drawing.Pop();
            }
            else
            {
                var samples = new[] { (-1d, -1d), (0d, -1d), (1d, -1d), (-1d, 0d),
                    (1d, 0d), (-1d, 1d), (0d, 1d), (1d, 1d) };
                var sampleColor = MediaColor.FromArgb((byte)Math.Max(1, color.A / samples.Length),
                    color.R, color.G, color.B);
                var brush = new SolidColorBrush(sampleColor);
                foreach (var (dx, dy) in samples)
                {
                    drawing.PushTransform(new TranslateTransform(shadow.OffsetX + dx * shadow.BlurPx / 2,
                        shadow.OffsetY + dy * shadow.BlurPx / 2));
                    drawing.DrawGeometry(brush, null, geometry);
                    drawing.Pop();
                }
            }
        }
        MediaPen? pen = null;
        if (paint.Stroke is { WidthPx: > 0 } stroke)
        {
            pen = new MediaPen(new SolidColorBrush(ParseColor(stroke.Color)), stroke.WidthPx);
        }
        drawing.DrawGeometry(new SolidColorBrush(ParseColor(paint.Fill)), pen, geometry);
        drawing.Pop();
    }

    private static double AlignOffset(string alignment, double start, double extent, double contentExtent) =>
        alignment switch
        {
            "center" => start + (extent - contentExtent) / 2,
            "right" or "bottom" => start + extent - contentExtent,
            _ => start
        };

    private static bool IsColor(string value) => !string.IsNullOrEmpty(value) && ColorPattern.IsMatch(value);

    private static MediaColor ParseColor(string value)
    {
        var argb = Convert.ToUInt32(value.Substring(1), 16);
        return MediaColor.FromArgb((byte)(argb >> 24), (byte)(argb >> 16),
            (byte)(argb >> 8), (byte)argb);
    }

    private sealed record Glyph(double Advance, double LineHeight, Geometry Outline, Rect Ink);
    private sealed record PositionedGlyph(Glyph Glyph, double X, double Y);
}
