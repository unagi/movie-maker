using System.Globalization;
using MovieMaker.Converters;

namespace MovieMaker.Tests.Converters;

public class BoolToTextConverterTests
{
    [Fact]
    public void Convert_WhenValueIsTrue_ReturnsTrueText()
    {
        var converter = new BoolToTextConverter
        {
            TrueText = "YES",
            FalseText = "NO"
        };

        var result = converter.Convert(true, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal("YES", result);
    }

    [Fact]
    public void Convert_WhenValueIsNotTrue_ReturnsFalseText()
    {
        var converter = new BoolToTextConverter
        {
            TrueText = "YES",
            FalseText = "NO"
        };

        Assert.Equal("NO", converter.Convert(false, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal("NO", converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        var converter = new BoolToTextConverter();

        Assert.Throws<NotSupportedException>(() =>
            converter.ConvertBack("YES", typeof(bool), null, CultureInfo.InvariantCulture));
    }
}
