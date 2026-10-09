using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace DNSSpeedTester.Converters;

/// <summary>
///     将 DnsServer.Status 文本映射为语义状态画刷（集中化，替代 XAML 中散落的 DataTrigger）。
///     画刷键在 Resources/SharedResources.xaml 中定义，便于主题统一管理。
/// </summary>
public class StatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = value as string ?? string.Empty;
        var key = status switch
        {
            "成功" => "StatusSuccessBrush",
            "证书警告" or "部分成功" => "StatusWarningBrush",
            "超时" or "错误" or "连接失败" or "证书错误" or "协议错误" => "StatusErrorBrush",
            "测试中..." => "StatusInfoBrush",
            _ => "StatusMutedBrush" // 未测试 / 不支持 / 已取消 等
        };

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
