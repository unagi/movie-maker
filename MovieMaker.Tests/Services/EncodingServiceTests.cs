using System.IO;
using MovieMaker.Services;

namespace MovieMaker.Tests.Services;

public class EncodingServiceTests
{
    [Fact]
    public async Task GetAudioInfoAsync_WhenFileDoesNotExist_ReturnsNull()
    {
        var result = await EncodingService.GetAudioInfoAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav"));

        Assert.Null(result);
    }

    [Fact]
    public void GetLogDirectory_ReturnsExistingDirectory()
    {
        var directory = EncodingService.GetLogDirectory();

        Assert.False(string.IsNullOrWhiteSpace(directory));
        Assert.True(Directory.Exists(directory));
    }
}
