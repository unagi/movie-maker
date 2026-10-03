using MovieMaker.Models;
using MovieMaker.Services;
using Xunit;

namespace MovieMaker.Tests;

public class OutputNamingServiceTests
{
    [Fact]
    public void DraftPreview_AddsCheckPrefixToOutputFileName()
    {
        var fileName = OutputNamingService.BuildOutputFileName("sample", "20260321_120000", EncodeProfile.DraftPreview);

        Assert.Equal("draft-preview_sample_20260321_120000.mp4", fileName);
    }

    [Fact]
    public void Standard_KeepsOriginalOutputFileName()
    {
        var fileName = OutputNamingService.BuildOutputFileName("sample", "20260321_120000", EncodeProfile.Standard);

        Assert.Equal("sample_20260321_120000.mp4", fileName);
    }

    [Fact]
    public void Shorts_KeepsOriginalOutputFileName()
    {
        var fileName = OutputNamingService.BuildOutputFileName("sample", "20260321_120000", EncodeProfile.CopyrightCheckProduction);

        Assert.Equal("sample_20260321_120000.mp4", fileName);
    }
}
