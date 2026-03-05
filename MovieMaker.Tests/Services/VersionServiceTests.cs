using MovieMaker.Services;

namespace MovieMaker.Tests.Services;

public class VersionServiceTests
{
    [Fact]
    public void DisplayVersion_IsEitherEmptyOrVersionPrefixed()
    {
        var versionText = VersionService.DisplayVersion;

        Assert.NotNull(versionText);
        if (!string.IsNullOrEmpty(versionText))
        {
            Assert.StartsWith("Version ", versionText, StringComparison.Ordinal);
        }
    }
}
