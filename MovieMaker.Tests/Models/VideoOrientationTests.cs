using MovieMaker.Models;

namespace MovieMaker.Tests.Models;

public class VideoOrientationTests
{
    [Fact]
    public void Enum_HasExpectedValues()
    {
        var values = Enum.GetValues<VideoOrientation>();

        Assert.Contains(VideoOrientation.Vertical, values);
        Assert.Contains(VideoOrientation.Horizontal, values);
    }
}
