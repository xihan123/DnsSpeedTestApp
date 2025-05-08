using System.Management;
using System.Security.Principal;
using System.Text;
using System.Windows;

namespace DNSSpeedTester.Helpers;

public static class NetworkDiagnostics
{
    public static void RunDiagnostics()
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 网络适配器诊断 ===");
            sb.AppendLine($"时间: {DateTime.Now}");
            sb.AppendLine($"操作系统: {Environment.OSVersion.VersionString}");
            sb.AppendLine($"64位系统: {Environment.Is64BitOperatingSystem}");
            sb.AppendLine($"进程权限: {(IsRunningAsAdmin() ? "管理员" : "普通用户")}");
            sb.AppendLine();

            // 检查 WMI 服务
            sb.AppendLine("检查 WMI 服务状态...");
            CheckWmiService(sb);
            sb.AppendLine();

            // 尝试获取网络适配器
            sb.AppendLine("尝试获取网络适配器信息...");
            GetNetworkAdaptersInfo(sb);

            // 显示诊断信息
            MessageBox.Show(sb.ToString(), "网络适配器诊断", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"运行诊断时出错: {ex.Message}", "诊断错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }


    private static bool IsRunningAsAdmin()
    {
        try
        {
            var principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static void CheckWmiService(StringBuilder sb)
    {
        try
        {
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Service WHERE Name='Winmgmt'"))
            {
                foreach (var service in searcher.Get())
                {
                    var state = service["State"]?.ToString() ?? "未知";
                    var startMode = service["StartMode"]?.ToString() ?? "未知";
                    sb.AppendLine($"WMI 服务状态: {state}");
                    sb.AppendLine($"WMI 服务启动模式: {startMode}");
                    return;
                }
            }

            sb.AppendLine("未找到 WMI 服务信息");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"检查 WMI 服务时出错: {ex.Message}");
        }
    }

    private static void GetNetworkAdaptersInfo(StringBuilder sb)
    {
        try
        {
            sb.AppendLine("方法 1: Win32_NetworkAdapter");
            using (var searcher =
                   new ManagementObjectSearcher(
                       "SELECT * FROM Win32_NetworkAdapter WHERE NetConnectionID IS NOT NULL"))
            {
                var adapters = searcher.Get();
                var count = 0;
                foreach (var adapter in adapters)
                {
                    count++;
                    sb.AppendLine($"- 适配器 {count}: {adapter["Description"]}");
                    sb.AppendLine($"  ID: {adapter["DeviceID"]}");
                    sb.AppendLine($"  名称: {adapter["NetConnectionID"]}");
                    sb.AppendLine($"  MAC: {adapter["MACAddress"]}");
                }

                sb.AppendLine($"总共找到 {count} 个适配器");
            }

            sb.AppendLine("\n方法 2: Win32_NetworkAdapterConfiguration");
            using (var searcher =
                   new ManagementObjectSearcher(
                       "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = True"))
            {
                var configs = searcher.Get();
                var count = 0;
                foreach (var config in configs)
                {
                    count++;
                    sb.AppendLine($"- 配置 {count}: {config["Description"]}");
                    sb.AppendLine($"  索引: {config["Index"]}");
                    sb.AppendLine($"  DHCP启用: {config["DHCPEnabled"]}");

                    if (config["IPAddress"] is string[] ipAddresses && ipAddresses.Length > 0)
                        sb.AppendLine($"  IP地址: {string.Join(", ", ipAddresses)}");

                    if (config["DNSServerSearchOrder"] is string[] dnsServers && dnsServers.Length > 0)
                        sb.AppendLine($"  DNS服务器: {string.Join(", ", dnsServers)}");
                }

                sb.AppendLine($"总共找到 {count} 个网络配置");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"获取网络适配器信息时出错: {ex.Message}");
            sb.AppendLine($"异常类型: {ex.GetType().FullName}");
            sb.AppendLine($"堆栈跟踪: {ex.StackTrace}");
        }
    }
}