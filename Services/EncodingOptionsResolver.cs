using MovieMaker.Models;

namespace MovieMaker.Services;

public static class EncodingOptionsResolver
{
    public static EncodingOptions Resolve(VideoOrientation orientation, EncodeProfile profile)
    {
        var isVertical = orientation == VideoOrientation.Vertical;

        if (profile == EncodeProfile.DraftPreview)
        {
            return new EncodingOptions(
                isVertical ? 540 : 960,
                isVertical ? 960 : 540,
                24,
                "128k",
                "32000",
                "veryfast",
                32,
                "p1",
                32,
                "veryfast",
                32,
                "speed",
                32);
        }

        if (profile == EncodeProfile.CopyrightCheckProduction)
        {
            return new EncodingOptions(
                isVertical ? 1080 : 1920,
                isVertical ? 1920 : 1080,
                30,
                "128k",
                "32000",
                "medium",
                18,
                "p5",
                19,
                "medium",
                19,
                "quality",
                19);
        }

        return new EncodingOptions(
            isVertical ? 1080 : 1920,
            isVertical ? 1920 : 1080,
            30,
            "320k",
            "48000",
            "medium",
            18,
            "p5",
            19,
            "medium",
            19,
            "quality",
            19);
    }
}
