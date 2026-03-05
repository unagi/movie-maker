using System.Globalization;
using System.Windows;
using MovieMaker.Converters;

namespace MovieMaker.Tests.Converters;

public class StringNullOrEmptyToVisibilityConverterTests
{
    [Fact]
    public void Convert_DefaultMode_EmptyOrNullIsVisible()
    {
        var converter = new StringNullOrEmptyToVisibilityConverter();

        Assert.Equal(Visibility.Visible, converter.Convert(null, typeof(Visibility), null, CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Visible, converter.Convert(" ", typeof(Visibility), null, CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Collapsed, converter.Convert("title", typeof(Visibility), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Convert_InverseMode_EmptyOrNullIsCollapsed()
    {
        var converter = new StringNullOrEmptyToVisibilityConverter
        {
            Inverse = true
        };

        Assert.Equal(Visibility.Collapsed, converter.Convert(null, typeof(Visibility), null, CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Collapsed, converter.Convert("", typeof(Visibility), null, CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Visible, converter.Convert("title", typeof(Visibility), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        var converter = new StringNullOrEmptyToVisibilityConverter();

        Assert.Throws<NotSupportedException>(() =>
            converter.ConvertBack(Visibility.Visible, typeof(string), null, CultureInfo.InvariantCulture));
    }
}
