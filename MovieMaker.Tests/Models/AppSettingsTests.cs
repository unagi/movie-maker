using MovieMaker.Models;

namespace MovieMaker.Tests.Models;

public class AppSettingsTests
{
    [Fact]
    public void NewInstance_HasEmptyDefaults()
    {
        var settings = new AppSettings();

        Assert.Equal(string.Empty, settings.OutputDirectory);
        Assert.Equal(string.Empty, settings.ArchiveDirectory);
    }
}
