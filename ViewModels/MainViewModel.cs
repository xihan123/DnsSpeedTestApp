using System.Collections.ObjectModel;
using System.Net;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DNSSpeedTester.Models;
using DNSSpeedTester.Services;

namespace DNSSpeedTester.ViewModels;

public partial class MainViewModel : ObservableObject
{
    // 服务
    private readonly DataPersistenceService _dataPersistenceService = new();
    private readonly DnsSettingService _dnsSettingService = new();
    private readonly DnsTestService _dnsTestService = new();

    // 集合
    public ObservableCollection<DnsServer> DnsServers { get; }
    public ObservableCollection<NetworkAdapter> NetworkAdapters { get; }
    public ObservableCollection<TestDomain> TestDomains { get; }
    public ObservableCollection<KeyValuePair<string, DnsProtocol>> ProtocolOptions { get; }

    // 选中项
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SetDnsCommand))]
    private DnsServer? _selectedDnsServer;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SetDnsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetToDhcpCommand))]
    private NetworkAdapter? _selectedNetworkAdapter;

    [ObservableProperty]
    private TestDomain? _selectedTestDomain;

    // 编辑状态
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddDnsButtonText))]
    private bool _isEditingDns;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddTestDomainButtonText))]
    private bool _isEditingTestDomain;

    public string AddDnsButtonText => IsEditingDns ? "修改" : "添加 DNS";
    public string AddTestDomainButtonText => IsEditingTestDomain ? "修改" : "添加测试域名";

    [ObservableProperty]
    private DnsProtocol _selectedProtocol = DnsProtocol.UdpTcp;

    // 状态信息
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // 测试结果
    [ObservableProperty]
    private int _testedCount;

    [ObservableProperty]
    private int _totalCount;

    // 忙碌状态
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartTestCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetDnsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetToDhcpCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddCustomDnsCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddTestDomainCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshRandomDomainCommand))]
    private bool _isBusy;

    // 新 DNS 条目输入
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCustomDnsCommand))]
    private string _newDnsName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCustomDnsCommand))]
    private string _newPrimaryDns = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCustomDnsCommand))]
    private string _newSecondaryDns = string.Empty;

    [ObservableProperty]
    private string _newDohUrl = string.Empty;

    [ObservableProperty]
    private string _newDotHost = string.Empty;

    [ObservableProperty]
    private string _newDotPort = "853";

    [ObservableProperty]
    private string _newDoqHost = string.Empty;

    [ObservableProperty]
    private string _newDoqPort = "853";

    // 新测试域名输入
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTestDomainCommand))]
    private string _newTestDomainName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTestDomainCommand))]
    private string _newTestDomainValue = string.Empty;

    // 构造函数
    public MainViewModel()
    {
        DnsServers = [];
        NetworkAdapters = [];
        TestDomains = [];

        ProtocolOptions =
        [
            new("UDP/TCP", DnsProtocol.UdpTcp),
            new("DoH (HTTPS)", DnsProtocol.DoH),
            new("DoT (TLS 853)", DnsProtocol.DoT),
            new("DoQ (QUIC 853)", DnsProtocol.DoQ)
        ];

        LoadData();
    }

    partial void OnSelectedDnsServerChanged(DnsServer? value)
    {
        if (value is { IsCustom: true })
        {
            NewDnsName = value.Name;
            NewPrimaryDns = value.PrimaryIP.ToString();
            NewSecondaryDns = value.SecondaryIP?.ToString() ?? string.Empty;
            NewDohUrl = value.DohUrl ?? string.Empty;
            NewDotHost = value.DotHost ?? string.Empty;
            NewDotPort = value.DotPort.ToString();
            NewDoqHost = value.DoqHost ?? string.Empty;
            NewDoqPort = value.DoqPort.ToString();
            IsEditingDns = true;
        }
        else
        {
            ClearDnsInputFields();
            IsEditingDns = false;
        }
    }

    partial void OnSelectedTestDomainChanged(TestDomain? value)
    {
        if (value is { IsCustom: true })
        {
            NewTestDomainName = value.Name;
            NewTestDomainValue = value.Domain;
            IsEditingTestDomain = true;
        }
        else
        {
            NewTestDomainName = string.Empty;
            NewTestDomainValue = string.Empty;
            IsEditingTestDomain = false;
        }
    }

    private void ClearDnsInputFields()
    {
        NewDnsName = string.Empty;
        NewPrimaryDns = string.Empty;
        NewSecondaryDns = string.Empty;
        NewDohUrl = string.Empty;
        NewDotHost = string.Empty;
        NewDotPort = "853";
        NewDoqHost = string.Empty;
        NewDoqPort = "853";
    }

    // 命令

    [RelayCommand(CanExecute = nameof(CanStartTest))]
    private async Task StartTestAsync()
    {
        if (SelectedTestDomain == null)
        {
            StatusMessage = "请先选择测试域名";
            return;
        }

        try
        {
            IsBusy = true;
            TotalCount = DnsServers.Count;
            TestedCount = 0;
            StatusMessage = $"开始测试 DNS 服务器 (协议: {SelectedProtocol}, 域名: {SelectedTestDomain.Domain})...";

            var dnsServersList = DnsServers.ToList();

            foreach (var server in dnsServersList)
            {
                server.Status = "测试中...";
                server.Latency = null;
            }

            var tasks = new Dictionary<DnsServer, Task<DnsServer>>();
            foreach (var server in dnsServersList)
            {
                var task = _dnsTestService.TestDnsServerAsync(server, SelectedTestDomain.Domain, SelectedProtocol);
                tasks.Add(server, task);
            }

            while (tasks.Count > 0)
            {
                var completedTask = await Task.WhenAny(tasks.Values);

                var serverEntry = tasks.FirstOrDefault(x => x.Value == completedTask);
                var server = serverEntry.Key;
                var task = serverEntry.Value;

                tasks.Remove(server);

                try
                {
                    var testedServer = await task;

                    var serverInCollection = DnsServers.FirstOrDefault(s =>
                        s.Name == testedServer.Name &&
                        s.PrimaryIP.ToString() == testedServer.PrimaryIP.ToString());

                    if (serverInCollection != null)
                    {
                        serverInCollection.Latency = testedServer.Latency;
                        serverInCollection.Status = testedServer.Status;
                        serverInCollection.StatusDetail = testedServer.StatusDetail;
                    }
                }
                catch (Exception ex)
                {
                    if (server != null)
                    {
                        server.Status = "错误";
                        server.StatusDetail = ex.Message;
                        server.Latency = null;
                    }
                }

                TestedCount++;
                StatusMessage = $"测试进度: {TestedCount}/{TotalCount}";
            }

            var sortedServers = new List<DnsServer>(
                DnsServers.OrderBy(s => s.Latency.HasValue ? s.Latency.Value : int.MaxValue)
            );

            Application.Current.Dispatcher.Invoke(() =>
            {
                DnsServers.Clear();
                foreach (var server in sortedServers) DnsServers.Add(server);

                SelectedDnsServer = DnsServers.FirstOrDefault(s => s.Latency.HasValue);
            });

            StatusMessage = $"DNS 测速完成（协议: {SelectedProtocol}），结果为本机到各 DNS 服务器解析 {SelectedTestDomain.Domain} 的往返延迟";
        }
        catch (Exception ex)
        {
            StatusMessage = $"测试中发生错误: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanStartTest => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSetDns))]
    private async Task SetDns()
    {
        if (SelectedDnsServer == null || SelectedNetworkAdapter == null)
        {
            StatusMessage = "请先选择 DNS 服务器和网络适配器";
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = $"正在设置 DNS 服务器: {SelectedDnsServer.Name}...";

            var result = await _dnsSettingService.SetDnsServersAsync(SelectedNetworkAdapter, SelectedDnsServer);
            StatusMessage = result.Message;
        }
        catch (Exception ex)
        {
            StatusMessage = $"设置 DNS 时出错: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSetDns => !IsBusy && SelectedDnsServer != null && SelectedNetworkAdapter != null;

    [RelayCommand(CanExecute = nameof(CanResetToDhcp))]
    private async Task ResetToDhcp()
    {
        if (SelectedNetworkAdapter == null)
        {
            StatusMessage = "请先选择网络适配器";
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "正在恢复为自动获取 DNS...";

            var result = await _dnsSettingService.ResetToDhcpAsync(SelectedNetworkAdapter);
            StatusMessage = result.Message;
        }
        catch (Exception ex)
        {
            StatusMessage = $"恢复 DHCP 时出错: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanResetToDhcp => !IsBusy && SelectedNetworkAdapter != null;

    [RelayCommand(CanExecute = nameof(CanAddCustomDns))]
    private void AddCustomDns()
    {
        try
        {
            if (!CanAddCustomDns())
            {
                StatusMessage = "请输入有效的 DNS 名称和主 DNS 服务器地址";
                return;
            }

            // 解析端口（可选，失败时采用默认值）
            var dotPort = 853;
            if (!string.IsNullOrWhiteSpace(NewDotPort) && int.TryParse(NewDotPort, out var tmpDotPort) &&
                tmpDotPort > 0 && tmpDotPort <= 65535)
                dotPort = tmpDotPort;

            var doqPort = 784;
            if (!string.IsNullOrWhiteSpace(NewDoqPort) && int.TryParse(NewDoqPort, out var tmpDoqPort) &&
                tmpDoqPort > 0 && tmpDoqPort <= 65535)
                doqPort = tmpDoqPort;

            if (IsEditingDns && SelectedDnsServer is { IsCustom: true } server)
            {
                server.Name = NewDnsName.Trim();
                server.PrimaryIP = IPAddress.Parse(NewPrimaryDns.Trim());
                server.SecondaryIP = !string.IsNullOrWhiteSpace(NewSecondaryDns)
                    ? IPAddress.Parse(NewSecondaryDns.Trim())
                    : null;
                server.DohUrl = string.IsNullOrWhiteSpace(NewDohUrl) ? null : NewDohUrl.Trim();
                server.DotHost = string.IsNullOrWhiteSpace(NewDotHost) ? null : NewDotHost.Trim();
                server.DotPort = dotPort;
                server.DoqHost = string.IsNullOrWhiteSpace(NewDoqHost) ? null : NewDoqHost.Trim();
                server.DoqPort = doqPort;

                IsEditingDns = false;
                ClearDnsInputFields();
                SaveCustomDnsServers();
                StatusMessage = $"已修改 DNS 服务器: {server.Name}";
                return;
            }

            var newDns = new DnsServer(
                NewDnsName.Trim(),
                NewPrimaryDns.Trim(),
                !string.IsNullOrWhiteSpace(NewSecondaryDns) ? NewSecondaryDns.Trim() : null,
                true,
                string.IsNullOrWhiteSpace(NewDohUrl) ? null : NewDohUrl.Trim(),
                string.IsNullOrWhiteSpace(NewDotHost) ? null : NewDotHost.Trim(),
                dotPort,
                string.IsNullOrWhiteSpace(NewDoqHost) ? null : NewDoqHost.Trim(),
                doqPort);

            DnsServers.Add(newDns);

            ClearDnsInputFields();
            SaveCustomDnsServers();

            StatusMessage = $"已添加自定义 DNS 服务器: {newDns.Name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"添加自定义 DNS 时出错: {ex.Message}";
        }
    }

    private bool CanAddCustomDns()
    {
        if (string.IsNullOrWhiteSpace(NewDnsName) || string.IsNullOrWhiteSpace(NewPrimaryDns)) return false;

        try
        {
            IPAddress.Parse(NewPrimaryDns);

            if (!string.IsNullOrWhiteSpace(NewSecondaryDns)) IPAddress.Parse(NewSecondaryDns);

            return true;
        }
        catch
        {
            return false;
        }
    }

    [RelayCommand]
    private void RemoveCustomDns(DnsServer server)
    {
        if (server == null || !server.IsCustom) return;

        var result = MessageBox.Show(
            $"确定要删除自定义 DNS 服务器 '{server.Name}' 吗？",
            "删除确认",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            DnsServers.Remove(server);
            SaveCustomDnsServers();
            StatusMessage = $"已删除自定义 DNS 服务器: {server.Name}";
        }
    }

    [RelayCommand]
    private void RunNetworkDiagnostics()
    {
        Helpers.NetworkDiagnostics.RunDiagnostics();
    }

    [RelayCommand]
    private async Task SelfCheckAsync()
    {
        try
        {
            var report = await QuicSelfCheck.RunAsync();
            MessageBox.Show(report, "自检报告", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"自检失败: {ex.Message}", "自检报告", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddTestDomain))]
    private void AddTestDomain()
    {
        try
        {
            if (!CanAddTestDomain())
            {
                StatusMessage = "请输入有效的域名名称和值";
                return;
            }

            if (IsEditingTestDomain && SelectedTestDomain is { IsCustom: true } domain)
            {
                domain.Name = NewTestDomainName.Trim();
                domain.Domain = NewTestDomainValue.Trim();

                IsEditingTestDomain = false;
                NewTestDomainName = string.Empty;
                NewTestDomainValue = string.Empty;
                SaveCustomTestDomains();
                StatusMessage = $"已修改测试域名: {domain.Name} [{domain.Domain}]";
                return;
            }

            var newDomain = new TestDomain(
                NewTestDomainName.Trim(),
                NewTestDomainValue.Trim(),
                "自定义",
                true);

            TestDomains.Add(newDomain);
            SelectedTestDomain = newDomain;

            NewTestDomainName = string.Empty;
            NewTestDomainValue = string.Empty;

            SaveCustomTestDomains();

            StatusMessage = $"已添加自定义测试域名: {newDomain.Name} [{newDomain.Domain}]";
        }
        catch (Exception ex)
        {
            StatusMessage = $"添加自定义测试域名时出错: {ex.Message}";
        }
    }

    private bool CanAddTestDomain()
    {
        if (string.IsNullOrWhiteSpace(NewTestDomainName) || string.IsNullOrWhiteSpace(NewTestDomainValue)) return false;

        try
        {
            var domain = NewTestDomainValue.Trim();
            return domain.Length > 0 && domain.Contains(".");
        }
        catch
        {
            return false;
        }
    }

    [RelayCommand]
    private void RemoveTestDomain(TestDomain domain)
    {
        if (domain == null || !domain.IsCustom) return;

        var result = MessageBox.Show(
            $"确定要删除自定义测试域名 '{domain.Name} [{domain.Domain}]' 吗？",
            "删除确认", MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            TestDomains.Remove(domain);
            SaveCustomTestDomains();
            StatusMessage = $"已删除自定义测试域名: {domain.Name}";

            if (SelectedTestDomain == domain)
                SelectedTestDomain = TestDomains.FirstOrDefault(d => d.Domain == "www.baidu.com") ??
                                     TestDomains.FirstOrDefault();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefreshRandomDomain))]
    private void RefreshRandomDomain()
    {
        try
        {
            var randomDomain = TestDomains.FirstOrDefault(d => d.Category == "特殊测试");
            if (randomDomain != null)
            {
                var randomPart = Guid.NewGuid().ToString().Replace("-", "").Substring(0, 8);
                var newDomainValue = $"{randomPart}.example.com";

                var index = TestDomains.IndexOf(randomDomain);
                TestDomains.Remove(randomDomain);

                var newRandomDomain = new TestDomain("随机域名", newDomainValue, "特殊测试");
                TestDomains.Insert(index, newRandomDomain);

                if (SelectedTestDomain == randomDomain) SelectedTestDomain = newRandomDomain;

                StatusMessage = $"已刷新随机测试域名: {newDomainValue}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"刷新随机域名时出错: {ex.Message}";
        }
    }

    private bool CanRefreshRandomDomain => !IsBusy;

    // 加载数据
    private void LoadData()
    {
        try
        {
            IsBusy = true;
            StatusMessage = "正在加载数据...";

            DnsServers.Clear();
            var commonServers = DnsTestService.GetCommonDnsServers();
            var customServers = _dataPersistenceService.LoadCustomDnsServers();

            foreach (var server in commonServers.Concat(customServers)) DnsServers.Add(server);

            NetworkAdapters.Clear();
            var adapters = _dnsSettingService.GetNetworkAdapters()
                .Where(a => a.IsConnected)
                .ToList();

            foreach (var adapter in adapters) NetworkAdapters.Add(adapter);

            if (NetworkAdapters.Count > 0) SelectedNetworkAdapter = NetworkAdapters[0];

            LoadTestDomains();

            StatusMessage = $"已加载 {DnsServers.Count} 个 DNS 服务器和 {NetworkAdapters.Count} 个网络适配器";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载数据时出错: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LoadTestDomains()
    {
        TestDomains.Clear();

        var commonDomains = DnsTestService.GetCommonTestDomains();
        var customDomains = _dataPersistenceService.LoadCustomTestDomains();

        foreach (var domain in commonDomains.Concat(customDomains)) TestDomains.Add(domain);

        if (TestDomains.Count > 0)
            SelectedTestDomain = TestDomains.FirstOrDefault(d => d.Domain == "www.baidu.com") ?? TestDomains[0];
    }

    private void SaveCustomDnsServers()
    {
        try
        {
            var customServers = DnsServers.Where(s => s.IsCustom).ToList();
            _dataPersistenceService.SaveCustomDnsServers(customServers);
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存自定义 DNS 列表时出错: {ex.Message}";
        }
    }

    private void SaveCustomTestDomains()
    {
        try
        {
            var customDomains = TestDomains.Where(d => d.IsCustom).ToList();
            _dataPersistenceService.SaveCustomTestDomains(customDomains);
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存自定义测试域名列表时出错: {ex.Message}";
        }
    }
}
