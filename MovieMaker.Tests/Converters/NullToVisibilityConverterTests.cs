using System.Globalization;
using System.Windows;
using MovieMaker.Converters;

namespace MovieMaker.Tests.Converters;

public class NullToVisibilityConverterTests
{
    [Fact]
    public void Convert_DefaultMode_NullIsVisible()
    {
        var converter = new NullToVisibilityConverter();

        var visible = converter.Convert(null, typeof(Visibility), null, CultureInfo.InvariantCulture);
        var collapsed = converter.Convert("text", typeof(Visibility), null, CultureInfo.InvariantCulture);

        Assert.Equal(Visibility.Visible, visible);
        Assert.Equal(Visibility.Collapsed, collapsed);
    }

    [Fact]
    public void Convert_InverseMode_NullIsCollapsed()
    {
        var converter = new NullToVisibilityConverter
        {
            Inverse = true
        };

        var nullValue = converter.Convert(null, typeof(Visibility), null, CultureInfo.InvariantCulture);
        var nonNull = converter.Convert("text", typeof(Visibility), null, CultureInfo.InvariantCulture);

        Assert.Equal(Visibility.Collapsed, nullValue);
        Assert.Equal(Visibility.Visible, nonNull);
    }

    [Fact]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        var converter = new NullToVisibilityConverter();

        Assert.Throws<NotSupportedException>(() =>
            converter.ConvertBack(Visibility.Visible, typeof(object), null, CultureInfo.InvariantCulture));
    }
}
