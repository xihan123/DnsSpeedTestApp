using System.Globalization;
using System.Windows.Data;

namespace DNSSpeedTester.Converters;

/// <summary>
///     布尔值取反转换器（如：IsBusy -> 控件可用性）。
/// </summary>
public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}
