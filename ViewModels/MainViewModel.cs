using System.Collections.ObjectModel;
using System.Net;
using System.Windows;
using System.Windows.Input;
using DNSSpeedTester.Helpers;
using DNSSpeedTester.Models;
using DNSSpeedTester.Services;

namespace DNSSpeedTester.ViewModels;

public class MainViewModel : ViewModelBase
{
    // 服务
    private readonly DataPersistenceService _dataPersistenceService = new();
    private readonly DnsSettingService _dnsSettingService = new();
    private readonly DnsTestService _dnsTestService = new();

    private bool _isBusy;

    // 新 DNS 条目输入
    private string _newDnsName;

    private string _newPrimaryDns;

    private string _newSecondaryDns;

    // 新测试域名输入
    private string _newTestDomainName;

    private string _newTestDomainValue;

    // 选中项
    private DnsServer? _selectedDnsServer;

    private NetworkAdapter? _selectedNetworkAdapter;

    private TestDomain? _selectedTestDomain;

    // 状态信息
    private string _statusMessage;

    // 测试结果
    private int _testedCount, _totalCount;


    // 构造函数
    public MainViewModel()
    {
        // 初始化集合
        DnsServers = new ObservableCollection<DnsServer>();
        NetworkAdapters = new ObservableCollection<NetworkAdapter>();
        TestDomains = new ObservableCollection<TestDomain>();

        // 初始化命令
        StartTestCommand = new RelayCommand(async obj => await StartDnsTest(), _ => !IsBusy);
        SetDnsCommand = new RelayCommand(async obj => await SetDns(),
            _ => !IsBusy && SelectedDnsServer != null && SelectedNetworkAdapter != null);
        ResetToDhcpCommand = new RelayCommand(async obj => await ResetToDhcp(),
            _ => !IsBusy && SelectedNetworkAdapter != null);
        AddCustomDnsCommand = new RelayCommand(obj => AddCustomDns(), _ => !IsBusy && CanAddCustomDns());
        RemoveCustomDnsCommand = new RelayCommand(obj => RemoveCustomDns(obj as DnsServer), _ => true);
        RunNetworkDiagnosticsCommand = new RelayCommand(_ => NetworkDiagnostics.RunDiagnostics(), _ => true);

        // 测试域名命令
        AddTestDomainCommand = new RelayCommand(obj => AddCustomTestDomain(), _ => !IsBusy && CanAddTestDomain());
        RemoveTestDomainCommand = new RelayCommand(obj => RemoveTestDomain(obj as TestDomain), _ => true);
        RefreshRandomDomainCommand = new RelayCommand(obj => RefreshRandomTestDomain(), _ => !IsBusy);

        // 初始化数据
        LoadData();
    }

    // 添加诊断命令
    public ICommand RunNetworkDiagnosticsCommand { get; }

    // 集合
    public ObservableCollection<DnsServer> DnsServers { get; }
    public ObservableCollection<NetworkAdapter> NetworkAdapters { get; }
    public ObservableCollection<TestDomain> TestDomains { get; }

    public DnsServer? SelectedDnsServer
    {
        get => _selectedDnsServer;
        set => SetProperty(ref _selectedDnsServer, value);
    }

    public NetworkAdapter? SelectedNetworkAdapter
    {
        get => _selectedNetworkAdapter;
        set => SetProperty(ref _selectedNetworkAdapter, value);
    }

    public TestDomain? SelectedTestDomain
    {
        get => _selectedTestDomain;
        set => SetProperty(ref _selectedTestDomain, value);
    }

    public string NewTestDomainName
    {
        get => _newTestDomainName;
        set
        {
            SetProperty(ref _newTestDomainName, value);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string NewTestDomainValue
    {
        get => _newTestDomainValue;
        set
        {
            SetProperty(ref _newTestDomainValue, value);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string NewDnsName
    {
        get => _newDnsName;
        set
        {
            SetProperty(ref _newDnsName, value);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string NewPrimaryDns
    {
        get => _newPrimaryDns;
        set
        {
            SetProperty(ref _newPrimaryDns, value);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string NewSecondaryDns
    {
        get => _newSecondaryDns;
        set
        {
            SetProperty(ref _newSecondaryDns, value);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            SetProperty(ref _isBusy, value);
            ((RelayCommand)StartTestCommand).RaiseCanExecuteChanged();
            ((RelayCommand)SetDnsCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ResetToDhcpCommand).RaiseCanExecuteChanged();
            ((RelayCommand)AddCustomDnsCommand).RaiseCanExecuteChanged();
            ((RelayCommand)AddTestDomainCommand).RaiseCanExecuteChanged();
            ((RelayCommand)RefreshRandomDomainCommand).RaiseCanExecuteChanged();
        }
    }

    public int TestedCount
    {
        get => _testedCount;
        set => SetProperty(ref _testedCount, value);
    }

    public int TotalCount
    {
        get => _totalCount;
        set => SetProperty(ref _totalCount, value);
    }

    // 命令
    public ICommand StartTestCommand { get; }
    public ICommand SetDnsCommand { get; }
    public ICommand ResetToDhcpCommand { get; }
    public ICommand AddCustomDnsCommand { get; }
    public ICommand RemoveCustomDnsCommand { get; }
    public ICommand AddTestDomainCommand { get; }
    public ICommand RemoveTestDomainCommand { get; }
    public ICommand RefreshRandomDomainCommand { get; }

    // 加载数据
    private void LoadData()
    {
        try
        {
            IsBusy = true;
            StatusMessage = "正在加载数据...";

            // 加载 DNS 服务器
            DnsServers.Clear();
            var commonServers = DnsTestService.GetCommonDnsServers();
            var customServers = _dataPersistenceService.LoadCustomDnsServers();

            foreach (var server in commonServers.Concat(customServers)) DnsServers.Add(server);

            // 加载网络适配器
            NetworkAdapters.Clear();
            var adapters = _dnsSettingService.GetNetworkAdapters()
                .Where(a => a.IsConnected)
                .ToList();

            foreach (var adapter in adapters) NetworkAdapters.Add(adapter);

            if (NetworkAdapters.Count > 0) SelectedNetworkAdapter = NetworkAdapters[0];

            // 加载测试域名
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

    // 加载测试域名
    private void LoadTestDomains()
    {
        TestDomains.Clear();

        // 加载内置的测试域名
        var commonDomains = DnsTestService.GetCommonTestDomains();

        // 加载自定义测试域名
        var customDomains = _dataPersistenceService.LoadCustomTestDomains();

        foreach (var domain in commonDomains.Concat(customDomains)) TestDomains.Add(domain);

        // 默认选中第一个域名
        if (TestDomains.Count > 0)
            // 默认选择百度
            SelectedTestDomain = TestDomains.FirstOrDefault(d => d.Domain == "www.baidu.com") ?? TestDomains[0];
    }

    // 开始 DNS 测试
    private async Task StartDnsTest()
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
            StatusMessage = $"开始测试 DNS 服务器 (测试域名: {SelectedTestDomain.Domain})...";

            // 重要：克隆当前的DNS服务器列表，以保留原始顺序和引用
            var dnsServersList = DnsServers.ToList();

            // 为每个DNS服务器准备测试
            foreach (var server in dnsServersList)
            {
                server.Status = "测试中...";
                server.Latency = null;
            }

            // 为每个服务器创建测试任务
            var tasks = new Dictionary<DnsServer, Task<DnsServer>>();
            foreach (var server in dnsServersList)
            {
                // 为每个服务器创建一个独立的测试任务，使用选定的测试域名
                var task = _dnsTestService.TestDnsServerAsync(server, SelectedTestDomain.Domain);
                tasks.Add(server, task);
            }

            // 等待所有任务完成
            while (tasks.Count > 0)
            {
                // 等待任何一个任务完成
                var completedTask = await Task.WhenAny(tasks.Values);

                // 找出完成的任务对应的DNS服务器
                var serverEntry = tasks.FirstOrDefault(x => x.Value == completedTask);
                var server = serverEntry.Key;
                var task = serverEntry.Value;

                // 从待处理任务中删除
                tasks.Remove(server);

                try
                {
                    // 获取测试结果
                    var testedServer = await task;

                    // 手动更新UI集合中的对象属性
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
                    // 处理单个测试的异常
                    if (server != null)
                    {
                        server.Status = "错误";
                        server.StatusDetail = ex.Message;
                        server.Latency = null;
                    }
                }

                // 更新进度
                TestedCount++;
                StatusMessage = $"测试进度: {TestedCount}/{TotalCount}";
            }

            // 按延迟排序
            var sortedServers = new List<DnsServer>(
                DnsServers.OrderBy(s => s.Latency.HasValue ? s.Latency.Value : int.MaxValue)
            );

            // 更新UI列表
            Application.Current.Dispatcher.Invoke(() =>
            {
                DnsServers.Clear();
                foreach (var server in sortedServers) DnsServers.Add(server);

                // 选择延迟最低的服务器
                SelectedDnsServer = DnsServers.FirstOrDefault(s => s.Latency.HasValue);
            });

            StatusMessage = $"DNS 测速完成，测量结果为本机到各 DNS 服务器解析 {SelectedTestDomain.Domain} 的往返延迟";
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

    // 设置 DNS
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

    // 恢复为 DHCP
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

    // 添加自定义 DNS
    private void AddCustomDns()
    {
        try
        {
            if (!CanAddCustomDns())
            {
                StatusMessage = "请输入有效的 DNS 名称和主 DNS 服务器地址";
                return;
            }

            var newDns = new DnsServer(
                NewDnsName.Trim(),
                NewPrimaryDns.Trim(),
                !string.IsNullOrWhiteSpace(NewSecondaryDns) ? NewSecondaryDns.Trim() : null,
                true);

            DnsServers.Add(newDns);

            // 清空输入字段
            NewDnsName = string.Empty;
            NewPrimaryDns = string.Empty;
            NewSecondaryDns = string.Empty;

            // 保存自定义 DNS 列表
            SaveCustomDnsServers();

            StatusMessage = $"已添加自定义 DNS 服务器: {newDns.Name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"添加自定义 DNS 时出错: {ex.Message}";
        }
    }

    // 删除自定义 DNS
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

    // 添加自定义测试域名
    private void AddCustomTestDomain()
    {
        try
        {
            if (!CanAddTestDomain())
            {
                StatusMessage = "请输入有效的域名名称和值";
                return;
            }

            var newDomain = new TestDomain(
                NewTestDomainName.Trim(),
                NewTestDomainValue.Trim(),
                "自定义",
                true);

            TestDomains.Add(newDomain);
            SelectedTestDomain = newDomain;

            // 清空输入字段
            NewTestDomainName = string.Empty;
            NewTestDomainValue = string.Empty;

            // 保存自定义测试域名
            SaveCustomTestDomains();

            StatusMessage = $"已添加自定义测试域名: {newDomain.Name} [{newDomain.Domain}]";
        }
        catch (Exception ex)
        {
            StatusMessage = $"添加自定义测试域名时出错: {ex.Message}";
        }
    }

    // 删除测试域名
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

            // 如果删除的是当前选中的域名，则选择默认域名
            if (SelectedTestDomain == domain)
                SelectedTestDomain = TestDomains.FirstOrDefault(d => d.Domain == "www.baidu.com") ??
                                     TestDomains.FirstOrDefault();
        }
    }

    // 刷新随机测试域名
    private void RefreshRandomTestDomain()
    {
        try
        {
            var randomDomain = TestDomains.FirstOrDefault(d => d.Category == "特殊测试");
            if (randomDomain != null)
            {
                var randomPart = Guid.NewGuid().ToString().Replace("-", "").Substring(0, 8);
                var newDomainValue = $"{randomPart}.example.com";

                // 更新随机域名
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

    // 保存自定义 DNS 列表
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

    // 保存自定义测试域名
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

    // 验证是否可以添加自定义 DNS
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

    // 验证是否可以添加自定义测试域名
    private bool CanAddTestDomain()
    {
        if (string.IsNullOrWhiteSpace(NewTestDomainName) || string.IsNullOrWhiteSpace(NewTestDomainValue)) return false;

        // 验证域名格式是否有效
        try
        {
            // 简单验证，接受任何非空值
            var domain = NewTestDomainValue.Trim();
            return domain.Length > 0 && domain.Contains(".");
        }
        catch
        {
            return false;
        }
    }
}