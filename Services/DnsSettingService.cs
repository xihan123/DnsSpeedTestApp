using System.Diagnostics;
using System.Management;
using System.Net;
using System.Windows;
using DNSSpeedTester.Models;

namespace DNSSpeedTester.Services;

public class DnsSettingService
{
    // 获取网络适配器列表
    public List<NetworkAdapter> GetNetworkAdapters()
    {
        var adapters = new List<NetworkAdapter>();

        try
        {
            // 使用更可靠的WMI查询获取网络适配器信息
            // 查询所有网络适配器，不限制为已连接状态
            using (var searcher =
                   new ManagementObjectSearcher(
                       "SELECT * FROM Win32_NetworkAdapter WHERE NetConnectionID IS NOT NULL"))
            {
                foreach (var obj in searcher.Get())
                    try
                    {
                        var deviceId = obj["DeviceID"]?.ToString();
                        var name = obj["NetConnectionID"]?.ToString();
                        var description = obj["Description"]?.ToString();

                        // 只有当所有必要数据都存在时才添加适配器
                        if (!string.IsNullOrEmpty(deviceId) && !string.IsNullOrEmpty(name))
                        {
                            var isConnected = false;
                            try
                            {
                                // 尝试检查连接状态 (NetConnectionStatus=2表示已连接)
                                var status = obj["NetConnectionStatus"];
                                isConnected = status != null && Convert.ToInt32(status) == 2;
                            }
                            catch
                            {
                                // 如果无法确定连接状态，则默认为已连接
                                isConnected = true;
                            }

                            var adapter = new NetworkAdapter(deviceId, name, description)
                            {
                                IsConnected = isConnected
                            };
                            adapters.Add(adapter);

                            // 获取配置信息
                            LoadNetworkAdapterConfig(adapter);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"处理单个网络适配器时出错: {ex.Message}");
                    }
            }

            // 如果没有找到网络适配器，尝试备用方法
            if (adapters.Count == 0)
                using (var searcher =
                       new ManagementObjectSearcher(
                           "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = True"))
                {
                    foreach (var obj in searcher.Get())
                        try
                        {
                            var index = obj["Index"]?.ToString() ?? "";
                            var description = obj["Description"]?.ToString() ?? "";

                            if (!string.IsNullOrEmpty(index) && !string.IsNullOrEmpty(description))
                            {
                                var adapter = new NetworkAdapter(index, description, description);
                                adapter.IsConnected = true;

                                // 获取 DNS 服务器
                                if (obj["DNSServerSearchOrder"] is string[] dnsServers && dnsServers.Length > 0)
                                    foreach (var dns in dnsServers)
                                        try
                                        {
                                            adapter.DnsServers.Add(IPAddress.Parse(dns));
                                        }
                                        catch
                                        {
                                        }

                                adapters.Add(adapter);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"处理备用网络配置时出错: {ex.Message}");
                        }
                }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"获取网络适配器时出错: {ex.Message}\n\n这可能是由于权限不足或WMI服务问题导致的。\n请确保以管理员身份运行程序。",
                "错误", MessageBoxButton.OK, MessageBoxImage.Error);

            Debug.WriteLine($"获取网络适配器时出错: {ex.Message}");
            Debug.WriteLine($"异常堆栈: {ex.StackTrace}");
        }

        return adapters;
    }

    // 加载网络适配器配置
    private void LoadNetworkAdapterConfig(NetworkAdapter adapter)
    {
        try
        {
            using (var searcher = new ManagementObjectSearcher(
                       $"SELECT * FROM Win32_NetworkAdapterConfiguration WHERE Index = {adapter.Id}"))
            {
                foreach (var obj in searcher.Get())
                {
                    try
                    {
                        adapter.IsDhcpEnabled = Convert.ToBoolean(obj["DHCPEnabled"] ?? false);

                        // 获取 DNS 服务器
                        if (obj["DNSServerSearchOrder"] is string[] dnsServers && dnsServers.Length > 0)
                            foreach (var dns in dnsServers)
                                try
                                {
                                    adapter.DnsServers.Add(IPAddress.Parse(dns));
                                }
                                catch
                                {
                                }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"读取网络适配器配置属性时出错: {ex.Message}");
                    }

                    break; // 只处理第一个匹配的配置
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"加载网络适配器配置时出错: {ex.Message}");
        }
    }

    // 其他方法保持不变...
    // 设置 DNS 服务器
    public async Task<OperationResult> SetDnsServersAsync(NetworkAdapter adapter, DnsServer dnsServer)
    {
        return await Task.Run(() => SetDnsServers(adapter, dnsServer));
    }

    private OperationResult SetDnsServers(NetworkAdapter adapter, DnsServer dnsServer)
    {
        try
        {
            using (var searcher = new ManagementObjectSearcher(
                       $"SELECT * FROM Win32_NetworkAdapterConfiguration WHERE Index = {adapter.Id}"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    // 准备 DNS 服务器地址
                    var dnsServerAddresses = new List<string>();
                    dnsServerAddresses.Add(dnsServer.PrimaryIP.ToString());

                    if (dnsServer.SecondaryIP != null) dnsServerAddresses.Add(dnsServer.SecondaryIP.ToString());

                    // 设置 DNS 服务器
                    var inParams = obj.GetMethodParameters("SetDNSServerSearchOrder");
                    inParams["DNSServerSearchOrder"] = dnsServerAddresses.ToArray();
                    var outParams = obj.InvokeMethod("SetDNSServerSearchOrder", inParams, null);

                    var returnValue = (uint)outParams["ReturnValue"];
                    if (returnValue == 0)
                    {
                        // 更新适配器对象的DNS服务器列表
                        adapter.DnsServers.Clear();
                        adapter.DnsServers.AddRange(dnsServerAddresses.Select(IPAddress.Parse));
                        adapter.IsDhcpEnabled = false;

                        return new OperationResult
                        {
                            Success = true,
                            Message = $"已成功将 {adapter.Description} 的 DNS 服务器设置为 {dnsServer.Name}"
                        };
                    }

                    return new OperationResult
                    {
                        Success = false,
                        Message = $"设置 DNS 服务器失败，错误代码: {returnValue}"
                    };
                }
            }

            return new OperationResult
            {
                Success = false,
                Message = $"未找到网络适配器 {adapter.Description}"
            };
        }
        catch (Exception ex)
        {
            return new OperationResult
            {
                Success = false,
                Message = $"设置 DNS 服务器时出错: {ex.Message}"
            };
        }
    }

    // 重置为 DHCP
    public async Task<OperationResult> ResetToDhcpAsync(NetworkAdapter adapter)
    {
        return await Task.Run(() => ResetToDhcp(adapter));
    }

    private OperationResult ResetToDhcp(NetworkAdapter adapter)
    {
        try
        {
            using (var searcher = new ManagementObjectSearcher(
                       $"SELECT * FROM Win32_NetworkAdapterConfiguration WHERE Index = {adapter.Id}"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    // 设置 DNS 为自动获取
                    var outParams = obj.InvokeMethod("SetDNSServerSearchOrder", new object[] { null });
                    var returnValue = (uint)outParams;

                    if (returnValue == 0)
                    {
                        // 更新适配器对象
                        adapter.DnsServers.Clear();
                        adapter.IsDhcpEnabled = true;

                        return new OperationResult
                        {
                            Success = true,
                            Message = $"已成功将 {adapter.Description} 的 DNS 设置为自动获取 (DHCP)"
                        };
                    }

                    return new OperationResult
                    {
                        Success = false,
                        Message = $"重置为 DHCP 失败，错误代码: {returnValue}"
                    };
                }
            }

            return new OperationResult
            {
                Success = false,
                Message = $"未找到网络适配器 {adapter.Description}"
            };
        }
        catch (Exception ex)
        {
            return new OperationResult
            {
                Success = false,
                Message = $"重置为 DHCP 时出错: {ex.Message}"
            };
        }
    }
}

public class OperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; }
}