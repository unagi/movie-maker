using MovieMaker.Models;

namespace MovieMaker.Services;

public static class OutputNamingService
{
    private const string DraftPreviewPrefix = "draft-preview_";

    public static string BuildOutputFileName(string title, string timestamp, EncodeProfile profile)
    {
        var prefix = profile switch
        {
            EncodeProfile.DraftPreview => DraftPreviewPrefix,
            _ => string.Empty
        };
        return $"{prefix}{title}_{timestamp}.mp4";
    }
}
