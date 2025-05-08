using System.Net;

namespace DNSSpeedTester.Models;

public class NetworkAdapter
{
    public NetworkAdapter(string id, string name, string description)
    {
        Id = id;
        Name = name;
        Description = description;
        DnsServers = new List<IPAddress>();
    }

    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public bool IsConnected { get; set; }
    public bool IsDhcpEnabled { get; set; }
    public List<IPAddress> DnsServers { get; set; } = new();

    public override string ToString()
    {
        return Description;
    }
}