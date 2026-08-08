using System.Globalization;
using System.IO;
using Wpf = System.Windows;
using Media = System.Windows.Media;
using Imaging = System.Windows.Media.Imaging;
using MovieMaker.Models;

namespace MovieMaker.Services;

public static class PlaceholderImageService
{
    public static string CreateDraftPlaceholder(
        string directory,
        string title,
        VideoOrientation orientation,
        DateTime timestamp)
    {
        Directory.CreateDirectory(directory);

        var outputPath = Path.Combine(directory, $"_draft_placeholder_{timestamp:yyyyMMdd_HHmmss}.png");
        var bitmap = CreateDraftPlaceholderBitmap(title, orientation);

        var encoder = new Imaging.PngBitmapEncoder();
        encoder.Frames.Add(Imaging.BitmapFrame.Create(bitmap));

        using var stream = File.Create(outputPath);
        encoder.Save(stream);
        return outputPath;
    }

    public static Imaging.BitmapSource CreateDraftPlaceholderBitmap(string title, VideoOrientation orientation)
    {
        var options = EncodingOptionsResolver.Resolve(orientation, EncodeProfile.DraftPreview);

        var visual = new Media.DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var background = new Media.SolidColorBrush((Media.Color)Media.ColorConverter.ConvertFromString("#111827"));
            var accent = new Media.SolidColorBrush((Media.Color)Media.ColorConverter.ConvertFromString("#7FB6E9"));
            var muted = new Media.SolidColorBrush((Media.Color)Media.ColorConverter.ConvertFromString("#9CA3AF"));

            dc.DrawRectangle(background, null, new Wpf.Rect(0, 0, options.Width, options.Height));
            dc.DrawRoundedRectangle(accent, null, new Wpf.Rect(32, 32, options.Width - 64, 12), 6, 6);

            var safeTitle = string.IsNullOrWhiteSpace(title) ? "Draft Preview" : title.Trim();
            var titleFontSize = orientation == VideoOrientation.Vertical ? 42 : 36;
            var bodyFontSize = orientation == VideoOrientation.Vertical ? 24 : 22;

            var titleText = CreateText(safeTitle, titleFontSize, Wpf.FontWeights.SemiBold, Media.Brushes.White, options.Width - 120);
            var subtitleText = CreateText("仮画像 / 低品質 / 非公開チェック用", bodyFontSize, Wpf.FontWeights.Normal, muted, options.Width - 120);
            var metaText = CreateText(
                orientation == VideoOrientation.Vertical ? "Vertical 540x960" : "Horizontal 960x540",
                bodyFontSize,
                Wpf.FontWeights.Normal,
                accent,
                options.Width - 120);

            var baseY = orientation == VideoOrientation.Vertical ? 180 : 120;
            dc.DrawText(titleText, new Wpf.Point(60, baseY));
            dc.DrawText(subtitleText, new Wpf.Point(60, baseY + titleText.Height + 24));
            dc.DrawText(metaText, new Wpf.Point(60, baseY + titleText.Height + subtitleText.Height + 44));
        }

        var bitmap = new Imaging.RenderTargetBitmap(options.Width, options.Height, 96, 96, Media.PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static Media.FormattedText CreateText(
        string text,
        double fontSize,
        Wpf.FontWeight fontWeight,
        Media.Brush brush,
        double maxWidth)
    {
        var formatted = new Media.FormattedText(
            text,
            CultureInfo.CurrentCulture,
            Wpf.FlowDirection.LeftToRight,
            new Media.Typeface(new Media.FontFamily("Segoe UI"), Wpf.FontStyles.Normal, fontWeight, Wpf.FontStretches.Normal),
            fontSize,
            brush,
            1.0);
        formatted.MaxTextWidth = maxWidth;
        return formatted;
    }
}
