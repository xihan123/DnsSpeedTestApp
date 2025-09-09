namespace DNSSpeedTester.Models;

public enum DnsProtocol
{
    UdpTcp = 0, // 传统 DNS（UDP/TCP）
    DoH, // DNS over HTTPS
    DoT, // DNS over TLS
    DoQ // DNS over QUIC
}