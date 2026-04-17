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
    private int? _latency;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LatencyDisplay))]
    private string _status = "未测试";

    [ObservableProperty]
    private string _statusDetail = string.Empty;

    [ObservableProperty]
    private string? _dohUrl;

    [ObservableProperty]
    private string? _dotHost;

    [ObservableProperty]
    private int _dotPort = 853;

    [ObservableProperty]
    private string? _doqHost;

    [ObservableProperty]
    private int _doqPort = 853;

    public string LatencyDisplay => Latency.HasValue ? $"{Latency.Value} 毫秒" : Status;
}
