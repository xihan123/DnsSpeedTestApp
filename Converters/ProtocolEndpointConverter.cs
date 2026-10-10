using System.Globalization;
using System.Windows.Data;
using DNSSpeedTester.Models;

namespace DNSSpeedTester.Converters;

/// <summary>
/// 输入依次为 DohDisplay、DotDisplay、DoqDisplay 和所选协议。
/// </summary>
public class ProtocolEndpointConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 4 || values[3] is not DnsProtocol protocol)
            return "—";

        return protocol switch
        {
            DnsProtocol.DoH => values[0] as string ?? "—",
            DnsProtocol.DoT => values[1] as string ?? "—",
            DnsProtocol.DoQ => values[2] as string ?? "—",
            _ => "—"
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class ProtocolColumnHeaderConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            DnsProtocol.DoH => "DoH 端点",
            DnsProtocol.DoT => "DoT 端点",
            DnsProtocol.DoQ => "DoQ 端点",
            _ => "端点地址"
        };

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
