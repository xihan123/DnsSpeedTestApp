using System.Net;

namespace DNSSpeedTester.Models;

public class DnsServer
{
    public DnsServer(string name, string primaryIp, string? secondaryIp = null, bool isCustom = false)
    {
        Name = name;
        PrimaryIP = IPAddress.Parse(primaryIp);
        SecondaryIP = secondaryIp is not null ? IPAddress.Parse(secondaryIp) : null;
        IsCustom = isCustom;
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

    public string LatencyDisplay => Latency.HasValue ? $"{Latency.Value} 毫秒" : Status;
}