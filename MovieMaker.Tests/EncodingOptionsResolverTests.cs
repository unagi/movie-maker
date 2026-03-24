using MovieMaker.Models;
using MovieMaker.Services;
using Xunit;

namespace MovieMaker.Tests;

public class EncodingOptionsResolverTests
{
    [Fact]
    public void DraftPreview_UsesLowResolutionAndFastSettings()
    {
        var options = EncodingOptionsResolver.Resolve(VideoOrientation.Horizontal, EncodeProfile.DraftPreview);

        Assert.Equal(960, options.Width);
        Assert.Equal(540, options.Height);
        Assert.Equal(24, options.FrameRate);
        Assert.Equal("128k", options.AudioBitrate);
        Assert.Equal("32000", options.AudioSampleRate);
        Assert.Equal("veryfast", options.LibX264Preset);
        Assert.Equal(32, options.LibX264Crf);
    }

    [Fact]
    public void StandardVertical_KeepsCurrentQualityDefaults()
    {
        var options = EncodingOptionsResolver.Resolve(VideoOrientation.Vertical, EncodeProfile.Standard);

        Assert.Equal(1080, options.Width);
        Assert.Equal(1920, options.Height);
        Assert.Equal(30, options.FrameRate);
        Assert.Equal("320k", options.AudioBitrate);
        Assert.Equal("48000", options.AudioSampleRate);
        Assert.Equal("medium", options.LibX264Preset);
        Assert.Equal(18, options.LibX264Crf);
    }

    [Fact]
    public void CopyrightCheckProduction_UsesProductionResolutionAndCheckAudioSettings()
    {
        var options = EncodingOptionsResolver.Resolve(VideoOrientation.Vertical, EncodeProfile.CopyrightCheckProduction);

        Assert.Equal(1080, options.Width);
        Assert.Equal(1920, options.Height);
        Assert.Equal(30, options.FrameRate);
        Assert.Equal("128k", options.AudioBitrate);
        Assert.Equal("32000", options.AudioSampleRate);
        Assert.Equal("medium", options.LibX264Preset);
        Assert.Equal(18, options.LibX264Crf);
    }
}
