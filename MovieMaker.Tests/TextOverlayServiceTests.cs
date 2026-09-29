using MovieMaker.Services;
using System.Buffers.Binary;
using System.Drawing;
using Xunit;

namespace MovieMaker.Tests;

public sealed class TextOverlayServiceTests
{
    private const string ValidConfiguration = """
        {
          "schemaVersion": 1,
          "canvas": { "width": 1920, "height": 1080 },
          "zones": [
            {
              "id": "title-left",
              "bounds": [72, 120, 210, 830],
              "writingMode": "vertical-rl",
              "horizontalAlign": "center",
              "verticalAlign": "center",
              "font": {
                "id": "source-han-sans-jp-heavy",
                "weight": 900,
                "sizePx": 104,
                "minSizePx": 68,
                "letterSpacingPx": 2
              },
              "paint": { "fill": "#FFFFFFFF" },
              "fit": { "maxLines": 3, "overflow": "reject" }
            }
          ]
        }
        """;

    [Fact]
    public void TryParse_AcceptsAValidConfiguration()
    {
        var parsed = TextOverlayService.TryParse(ValidConfiguration, out var configuration, out var error);

        Assert.True(parsed, error);
        Assert.NotNull(configuration);
        Assert.Equal(1920, configuration!.Canvas.Width);
        Assert.Equal("title-left", configuration.Zones[0].Id);
    }

    [Theory]
    [InlineData("\"schemaVersion\": 1", "\"schemaVersion\": 2")]
    [InlineData("\"bounds\": [72, 120, 210, 830]", "\"bounds\": [72, 120, 2100, 830]")]
    [InlineData("\"overflow\": \"reject\"", "\"overflow\": \"clip\"")]
    public void TryParse_RejectsInvalidConfigurationValues(string oldValue, string newValue)
    {
        var json = ValidConfiguration.Replace(oldValue, newValue, StringComparison.Ordinal);

        var parsed = TextOverlayService.TryParse(json, out _, out var error);

        Assert.False(parsed);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryParse_RejectsUnknownProperties()
    {
        var json = ValidConfiguration.Replace("\"schemaVersion\": 1,", "\"schemaVersion\": 1,\n  \"unexpected\": true,", StringComparison.Ordinal);

        var parsed = TextOverlayService.TryParse(json, out _, out var error);

        Assert.False(parsed);
        Assert.Contains("JSON", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateTrackImagesAsync_RendersTrackTitleAtTheConfiguredCanvasSize()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MovieMaker-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var background = Path.Combine(directory, "background.png");
            File.WriteAllBytes(background, Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p7sAAAAASUVORK5CYII="));
            Assert.True(TextOverlayService.TryParse(ValidConfiguration, out var configuration, out var error), error);

            var paths = await TextOverlayService.CreateTrackImagesAsync(background, directory,
                ["揺れる想い"], configuration!);

            Assert.Single(paths);
            var png = File.ReadAllBytes(paths[0]);
            Assert.True(png.Length > 1000);
            Assert.Equal(1920, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));
            Assert.Equal(1080, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CreateTrackImagesAsync_VerticalICharactersHaveSeparateRows()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MovieMaker-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var backgroundPath = Path.Combine(directory, "background.png");
            using (var background = new Bitmap(16, 9))
            {
                using var graphics = Graphics.FromImage(background);
                graphics.Clear(Color.Black);
                background.Save(backgroundPath);
            }

            Assert.True(TextOverlayService.TryParse(ValidConfiguration, out var configuration, out var error), error);
            var paths = await TextOverlayService.CreateTrackImagesAsync(backgroundPath, directory,
                ["IIII"], configuration!);
            using var rendered = new Bitmap(paths[0]);

            var occupiedRows = new List<int>();
            for (var y = 120; y < 950; y++)
            {
                for (var x = 72; x < 282; x++)
                {
                    var pixel = rendered.GetPixel(x, y);
                    if (pixel.R <= 128 && pixel.G <= 128 && pixel.B <= 128)
                    {
                        continue;
                    }

                    occupiedRows.Add(y);
                    break;
                }
            }

            Assert.NotEmpty(occupiedRows);
            Assert.Equal(3, occupiedRows.Zip(occupiedRows.Skip(1))
                .Count(pair => pair.Second - pair.First >= 10));

            var rowStarts = occupiedRows.Where((row, index) =>
                index == 0 || row - occupiedRows[index - 1] >= 10).ToArray();
            Assert.Equal(4, rowStarts.Length);
            foreach (var (first, second) in rowStarts.Zip(rowStarts.Skip(1)))
            {
                Assert.InRange(second - first, 104, 108);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CreateTrackImagesAsync_VerticalOverhangingGlyphShrinksWithinFixedCells()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MovieMaker-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var backgroundPath = Path.Combine(directory, "background.png");
            using (var background = new Bitmap(16, 9))
            {
                using var graphics = Graphics.FromImage(background);
                graphics.Clear(Color.Black);
                background.Save(backgroundPath);
            }

            Assert.True(TextOverlayService.TryParse(ValidConfiguration, out var configuration, out var error), error);
            var paths = await TextOverlayService.CreateTrackImagesAsync(backgroundPath, directory,
                ["III", "IjI"], configuration!);
            using var baseline = new Bitmap(paths[0]);
            using var rendered = new Bitmap(paths[1]);

            var occupiedRows = new List<int>();
            for (var y = 120; y < 950; y++)
            {
                for (var x = 72; x < 282; x++)
                {
                    var pixel = rendered.GetPixel(x, y);
                    if (pixel.R <= 128 && pixel.G <= 128 && pixel.B <= 128)
                    {
                        continue;
                    }
                    occupiedRows.Add(y);
                    break;
                }
            }

            var bands = new List<List<int>> { new() };
            foreach (var row in occupiedRows)
            {
                if (bands[^1].Count > 0 && row - bands[^1][^1] >= 10)
                {
                    bands.Add([]);
                }
                bands[^1].Add(row);
            }
            Assert.Equal(3, bands.Count);
            var centers = bands.Select(band => (band[0] + band[^1]) / 2.0).ToArray();
            foreach (var (first, second) in centers.Zip(centers.Skip(1)))
            {
                Assert.InRange(second - first, 104, 108);
            }

            var baselineRows = new List<int>();
            for (var y = 120; y < 950; y++)
            {
                for (var x = 72; x < 282; x++)
                {
                    var pixel = baseline.GetPixel(x, y);
                    if (pixel.R <= 128 && pixel.G <= 128 && pixel.B <= 128)
                    {
                        continue;
                    }
                    baselineRows.Add(y);
                    break;
                }
            }
            var baselineFirstBandHeight = baselineRows.TakeWhile((row, index) =>
                index == 0 || row - baselineRows[index - 1] < 10).Count();
            Assert.True(bands[0].Count < baselineFirstBandHeight);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("IIIIIIII", 0)]
    [InlineData("IjIIIIII", 0)]
    [InlineData("IIIIIIIIIIIII", 1)]
    public async Task CreateTrackImagesAsync_VerticalTitleShrinksBeforeAutomaticColumnWrap(
        string title, int expectedColumnGaps)
    {
        var directory = Path.Combine(Path.GetTempPath(), "MovieMaker-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var backgroundPath = Path.Combine(directory, "background.png");
            using (var background = new Bitmap(16, 9))
            {
                using var graphics = Graphics.FromImage(background);
                graphics.Clear(Color.Black);
                background.Save(backgroundPath);
            }

            Assert.True(TextOverlayService.TryParse(ValidConfiguration, out var configuration, out var error), error);
            var paths = await TextOverlayService.CreateTrackImagesAsync(backgroundPath, directory,
                [title], configuration!);
            using var rendered = new Bitmap(paths[0]);

            var occupiedColumns = new List<int>();
            for (var x = 72; x < 282; x++)
            {
                for (var y = 120; y < 950; y++)
                {
                    var pixel = rendered.GetPixel(x, y);
                    if (pixel.R <= 128 && pixel.G <= 128 && pixel.B <= 128)
                    {
                        continue;
                    }
                    occupiedColumns.Add(x);
                    break;
                }
            }

            Assert.NotEmpty(occupiedColumns);
            Assert.Equal(expectedColumnGaps, occupiedColumns.Zip(occupiedColumns.Skip(1))
                .Count(pair => pair.Second - pair.First >= 10));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CreateTrackImagesAsync_HorizontalAdvanceWrapsBeforeZoneWidth()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MovieMaker-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var backgroundPath = Path.Combine(directory, "background.png");
            using (var background = new Bitmap(16, 9))
            {
                using var graphics = Graphics.FromImage(background);
                graphics.Clear(Color.Black);
                background.Save(backgroundPath);
            }

            var json = ValidConfiguration.Replace("\"vertical-rl\"", "\"horizontal-tb\"", StringComparison.Ordinal);
            Assert.True(TextOverlayService.TryParse(json, out var configuration, out var error), error);
            var paths = await TextOverlayService.CreateTrackImagesAsync(backgroundPath, directory,
                ["WWW"], configuration!);
            using var rendered = new Bitmap(paths[0]);

            var occupiedRows = new List<int>();
            for (var y = 120; y < 950; y++)
            {
                for (var x = 72; x < 282; x++)
                {
                    var pixel = rendered.GetPixel(x, y);
                    if (pixel.R <= 128 && pixel.G <= 128 && pixel.B <= 128)
                    {
                        continue;
                    }
                    occupiedRows.Add(y);
                    break;
                }
            }

            Assert.NotEmpty(occupiedRows);
            Assert.Equal(1, occupiedRows.Zip(occupiedRows.Skip(1))
                .Count(pair => pair.Second - pair.First >= 10));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CreateTrackImagesAsync_HorizontalLetterSpacingAddsToAdvance()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MovieMaker-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var backgroundPath = Path.Combine(directory, "background.png");
            using (var background = new Bitmap(16, 9))
            {
                using var graphics = Graphics.FromImage(background);
                graphics.Clear(Color.Black);
                background.Save(backgroundPath);
            }

            var json = ValidConfiguration.Replace("\"vertical-rl\"", "\"horizontal-tb\"", StringComparison.Ordinal);
            Assert.True(TextOverlayService.TryParse(json, out var configuration, out var error), error);
            var paths = await TextOverlayService.CreateTrackImagesAsync(backgroundPath, directory,
                ["II"], configuration!);
            using var rendered = new Bitmap(paths[0]);

            var occupiedColumns = new List<int>();
            for (var x = 72; x < 282; x++)
            {
                for (var y = 120; y < 950; y++)
                {
                    var pixel = rendered.GetPixel(x, y);
                    if (pixel.R <= 128 && pixel.G <= 128 && pixel.B <= 128)
                    {
                        continue;
                    }
                    occupiedColumns.Add(x);
                    break;
                }
            }

            var bands = new List<List<int>> { new() };
            foreach (var column in occupiedColumns)
            {
                if (bands[^1].Count > 0 && column - bands[^1][^1] >= 10)
                {
                    bands.Add([]);
                }
                bands[^1].Add(column);
            }
            Assert.Equal(2, bands.Count);
            var centers = bands.Select(band => (band[0] + band[^1]) / 2.0).ToArray();
            Assert.InRange(centers[1] - centers[0], 37, 40);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
