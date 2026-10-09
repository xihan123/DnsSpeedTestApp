using System.Globalization;
using System.Windows.Data;
using DNSSpeedTester.Models;

namespace DNSSpeedTester.Converters;

/// <summary>
///     将 DnsServer 支持的加密协议汇总为紧凑标签（如 "DoH · DoT · DoQ"），
///     以便结果表格用单列展示协议能力，避免表格过宽。
/// </summary>
public class CapabilitiesConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DnsServer server) return "—";

        var capabilities = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(server.DohUrl)) capabilities.Add("DoH");
        if (!string.IsNullOrWhiteSpace(server.DotHost)) capabilities.Add("DoT");
        if (!string.IsNullOrWhiteSpace(server.DoqHost)) capabilities.Add("DoQ");

        return capabilities.Count > 0 ? string.Join(" · ", capabilities) : "—";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}