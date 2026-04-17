using System.Net;
using DNSSpeedTester.Models;

namespace DNSSpeedTester.Services;

public class DnsServerDto
{
    public string Name { get; set; } = string.Empty;
    public string PrimaryIPString { get; set; } = string.Empty;
    public string? SecondaryIPString { get; set; }
    public bool IsCustom { get; set; }

    // 协议端点
    public string? DohUrl { get; set; }
    public string? DotHost { get; set; }
    public int DotPort { get; set; } = 853;
    public string? DoqHost { get; set; }
    public int DoqPort { get; set; } = 853;

    public static DnsServerDto FromDnsServer(DnsServer server)
    {
        return new DnsServerDto
        {
            Name = server.Name ?? string.Empty,
            PrimaryIPString = server.PrimaryIP?.ToString() ?? string.Empty,
            SecondaryIPString = server.SecondaryIP?.ToString(),
            IsCustom = server.IsCustom,
            DohUrl = server.DohUrl,
            DotHost = server.DotHost,
            DotPort = server.DotPort,
            DoqHost = server.DoqHost,
            DoqPort = server.DoqPort
        };
    }

    public DnsServer ToDnsServer()
    {
        var server = new DnsServer
        {
            Name = Name ?? string.Empty,
            IsCustom = IsCustom,
            DohUrl = DohUrl,
            DotHost = DotHost,
            DotPort = DotPort,
            DoqHost = DoqHost,
            DoqPort = DoqPort
        };

        if (!string.IsNullOrEmpty(PrimaryIPString))
            server.PrimaryIP = IPAddress.Parse(PrimaryIPString);

        if (!string.IsNullOrEmpty(SecondaryIPString))
            server.SecondaryIP = IPAddress.Parse(SecondaryIPString);

        return server;
    }
}
