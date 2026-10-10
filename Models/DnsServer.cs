using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DNSSpeedTester.Models;

public partial class DnsServer : ObservableObject
{
    public DnsServer(string name, string primaryIp, string? secondaryIp = null, bool isCustom = false,
        string? dohUrl = null, string? dotHost = null, int dotPort = 853, string? doqHost = null, int doqPort = 853)
    {
        _name = name;
        _primaryIP = IPAddress.Parse(primaryIp);
        _secondaryIP = secondaryIp is not null ? IPAddress.Parse(secondaryIp) : null;
        _isCustom = isCustom;
        _dohUrl = dohUrl;
        _dotHost = dotHost;
        _dotPort = dotPort;
        _doqHost = doqHost;
        _doqPort = doqPort;
    }

    public DnsServer() { }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private IPAddress _primaryIP = IPAddress.Any;

    [ObservableProperty]
    private IPAddress? _secondaryIP;

    [ObservableProperty]
    private bool _isCustom;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LatencyDisplay))]
    [NotifyPropertyChangedFor(nameof(HasLatency))]
    private int? _latency;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LatencyDisplay))]
    private string _status = "未测试";

    /// <summary>是否为最近一次测速中最快的服务器（用于结果高亮）。</summary>
    [ObservableProperty]
    private bool _isFastest;

    [ObservableProperty]
    private string _statusDetail = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DohDisplay))]
    private string? _dohUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DotDisplay))]
    private string? _dotHost;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DotDisplay))]
    private int _dotPort = 853;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DoqDisplay))]
    private string? _doqHost;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DoqDisplay))]
    private int _doqPort = 853;

    public string LatencyDisplay => Latency.HasValue ? $"{Latency.Value} 毫秒" : Status;

    public bool HasLatency => Latency.HasValue;

    public string DohDisplay => string.IsNullOrWhiteSpace(DohUrl) ? NotConfigured : DohUrl;

    public string DotDisplay => string.IsNullOrWhiteSpace(DotHost) ? NotConfigured : $"{DotHost}:{DotPort}";

    public string DoqDisplay => string.IsNullOrWhiteSpace(DoqHost) ? NotConfigured : $"{DoqHost}:{DoqPort}";

    private const string NotConfigured = "—";
}
