using System.Net;

namespace DNSSpeedTester.Models;

public class DnsServer
{
    public DnsServer(string name, string primaryIp, string? secondaryIp = null, bool isCustom = false,
        string? dohUrl = null, string? dotHost = null, int dotPort = 853, string? doqHost = null, int doqPort = 853)
    {
        Name = name;
        PrimaryIP = IPAddress.Parse(primaryIp);
        SecondaryIP = secondaryIp is not null ? IPAddress.Parse(secondaryIp) : null;
        IsCustom = isCustom;

        DohUrl = dohUrl;
        DotHost = dotHost;
        DotPort = dotPort;
        DoqHost = doqHost;
        DoqPort = doqPort;
    }

    public DnsServer()
    {
    }

    public string Name { get; set; }
    public IPAddress PrimaryIP { get; set; }
    public IPAddress? SecondaryIP { get; set; }
    public bool IsCustom { get; set; }
    public int? Latency { get; set; }
    public string Status { get; set; } = "未测试";
    public string StatusDetail { get; set; } = string.Empty;

    // 新增：各协议端点信息
    public string? DohUrl { get; set; }
    public string? DotHost { get; set; }
    public int DotPort { get; set; } = 853;
    public string? DoqHost { get; set; }
    public int DoqPort { get; set; } = 853;

    public string LatencyDisplay => Latency.HasValue ? $"{Latency.Value} 毫秒" : Status;
}