using System;
using System.Globalization;
using System.Windows.Data;

namespace MovieMaker.Converters;

public sealed class BoolToTextConverter : IValueConverter
{
    public string TrueText { get; set; } = "OK";
    public string FalseText { get; set; } = "未設定";

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool flag && flag)
        {
            return TrueText;
        }

        return FalseText;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
