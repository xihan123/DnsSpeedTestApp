using System.Net;
using DNSSpeedTester.Models;

namespace DNSSpeedTester.Services;

public class DnsServerDto
{
    // Properties matching your DnsServer class
    public string Name { get; set; } = string.Empty;
    public string PrimaryIPString { get; set; } = string.Empty;
    public string? SecondaryIPString { get; set; }
    public bool IsCustom { get; set; }
    public int? Latency { get; set; }
    public string Status { get; set; } = "未测试";
    public string StatusDetail { get; set; } = string.Empty;

    // Convert from DnsServer to DTO
    public static DnsServerDto FromDnsServer(DnsServer server)
    {
        return new DnsServerDto
        {
            Name = server.Name ?? string.Empty,
            PrimaryIPString = server.PrimaryIP?.ToString() ?? string.Empty,
            SecondaryIPString = server.SecondaryIP?.ToString(),
            IsCustom = server.IsCustom,
            Latency = server.Latency,
            Status = server.Status ?? "未测试",
            StatusDetail = server.StatusDetail ?? string.Empty
        };
    }

    // Convert from DTO back to DnsServer
    public DnsServer ToDnsServer()
    {
        var server = new DnsServer
        {
            Name = Name ?? string.Empty,
            IsCustom = IsCustom,
            Latency = Latency,
            Status = Status ?? "未测试",
            StatusDetail = StatusDetail ?? string.Empty
        };

        // Parse IP addresses from their string representations
        if (!string.IsNullOrEmpty(PrimaryIPString)) server.PrimaryIP = IPAddress.Parse(PrimaryIPString);

        if (!string.IsNullOrEmpty(SecondaryIPString)) server.SecondaryIP = IPAddress.Parse(SecondaryIPString);

        return server;
    }
}