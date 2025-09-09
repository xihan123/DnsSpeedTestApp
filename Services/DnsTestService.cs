using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using DnsClient;
using DNSSpeedTester.Models;

namespace DNSSpeedTester.Services;

public class DnsTestService
{
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    // 获取常用测试域名列表 - 保持不变
    public static List<TestDomain> GetCommonTestDomains()
    {
        return new List<TestDomain>
        {
            // 国内网站
            new("百度", "www.baidu.com", "国内"),
            new("淘宝", "www.taobao.com", "国内"),
            new("腾讯", "www.qq.com", "国内"),
            new("网易", "www.163.com", "国内"),
            new("哔哩哔哩", "www.bilibili.com", "国内"),
            new("知乎", "www.zhihu.com", "国内"),

            // 国际网站
            new("谷歌", "www.google.com", "国际"),
            new("YouTube", "www.youtube.com", "国际"),
            new("微软", "www.microsoft.com", "国际"),
            new("亚马逊", "www.amazon.com", "国际"),
            new("Facebook", "www.facebook.com", "国际"),
            new("Twitter", "twitter.com", "国际"),

            // CDN/云服务
            new("CloudFlare", "www.cloudflare.com", "CDN/云服务"),
            new("Akamai", "www.akamai.com", "CDN/云服务"),
            new("AWS", "aws.amazon.com", "CDN/云服务"),
            new("Azure", "azure.microsoft.com", "CDN/云服务"),

            // 随机域名 (避免缓存)
            new("随机域名", GenerateRandomDomain(), "特殊测试")
        };
    }

    private static string GenerateRandomDomain()
    {
        return $"{Guid.NewGuid().ToString("N")[..8]}.example.com";
    }

    // 对外：按协议测试
    public async Task<DnsServer> TestDnsServerAsync(DnsServer server, string testDomain, DnsProtocol protocol)
    {
        var serverToTest = server;

        try
        {
            serverToTest.Status = "测试中...";
            serverToTest.Latency = null;
            serverToTest.StatusDetail = string.Empty;

            var finalLatency = protocol switch
            {
                DnsProtocol.UdpTcp => await EnhancedDnsSpeedTest(serverToTest.PrimaryIP, testDomain),
                DnsProtocol.DoH => await MeasureDohLatency(serverToTest, testDomain),
                DnsProtocol.DoT => await MeasureDotLatency(serverToTest, testDomain),
                DnsProtocol.DoQ => await MeasureDoqLatency(serverToTest, testDomain),
                _ => null
            };

            if (finalLatency.HasValue)
            {
                serverToTest.Latency = finalLatency.Value;
                serverToTest.Status = "成功";
                serverToTest.StatusDetail = $"DNS响应时间: {finalLatency}ms";
            }
            else
            {
                serverToTest.Status = "超时";
                serverToTest.StatusDetail = "DNS查询失败或超时";
                serverToTest.Latency = null;
            }

            return serverToTest;
        }
        catch (Exception ex)
        {
            serverToTest.Status = "错误";
            serverToTest.StatusDetail = ex.Message;
            serverToTest.Latency = null;
            return serverToTest;
        }
    }

    // 增强版（传统 UDP/TCP）
    private async Task<int?> EnhancedDnsSpeedTest(IPAddress serverIP, string testDomain)
    {
        var latencies = new List<int>();

        var tcpLatency = await MeasureTcpDnsLatency(serverIP, testDomain);
        if (tcpLatency.HasValue) latencies.Add(tcpLatency.Value);

        var udpLatency = await MeasureUdpDnsLatency(serverIP, testDomain);
        if (udpLatency.HasValue) latencies.Add(udpLatency.Value);

        var randomLatency = await MeasureRandomDnsLatency(serverIP);
        if (randomLatency.HasValue) latencies.Add(randomLatency.Value);

        var pingLatency = await MeasurePingLatency(serverIP);
        if (pingLatency.HasValue) latencies.Add(pingLatency.Value);

        if (latencies.Count == 0) return null;

        if (latencies.Count == 1) return latencies[0];
        if (latencies.Count == 2) return Math.Max(latencies[0], latencies[1]);

        latencies.Sort();
        return latencies[latencies.Count / 2];
    }

    private LookupClient CreateTcpClient(IPAddress serverIP)
    {
        var options = new LookupClientOptions(new IPEndPoint(serverIP, 53))
        {
            UseCache = false,
            Timeout = TimeSpan.FromSeconds(5),
            Retries = 0,
            UseTcpOnly = true,
            EnableAuditTrail = false
        };
        return new LookupClient(options);
    }

    private LookupClient CreateUdpClient(IPAddress serverIP)
    {
        var options = new LookupClientOptions(new IPEndPoint(serverIP, 53))
        {
            UseCache = false,
            Timeout = TimeSpan.FromSeconds(5),
            Retries = 0,
            UseTcpOnly = false,
            EnableAuditTrail = false
        };
        return new LookupClient(options);
    }

    private async Task<int?> MeasureTcpDnsLatency(IPAddress serverIP, string testDomain)
    {
        try
        {
            var lookupClient = CreateTcpClient(serverIP);

            try
            {
                await lookupClient.QueryAsync("www.example.com", QueryType.A);
            }
            catch
            {
                /* 忽略预热错误 */
            }

            // 短暂延迟确保预热完成
            await Task.Delay(50);

            // 执行多次测试并取均值
            var validTests = 0;
            var totalLatency = 0;

            for (var i = 0; i < 3; i++)
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    var result = await lookupClient.QueryAsync(testDomain, QueryType.A);
                    stopwatch.Stop();

                    if (!result.HasError)
                    {
                        validTests++;
                        totalLatency += (int)stopwatch.ElapsedMilliseconds;
                    }
                }
                catch
                {
                }

                await Task.Delay(200);
            }

            if (validTests > 0) return totalLatency / validTests;
        }
        catch
        {
        }

        return null;
    }

    private async Task<int?> MeasureUdpDnsLatency(IPAddress serverIP, string testDomain)
    {
        try
        {
            var lookupClient = CreateUdpClient(serverIP);
            var queryTypes = new[] { QueryType.AAAA, QueryType.MX, QueryType.TXT };

            var validTests = 0;
            var totalLatency = 0;

            foreach (var queryType in queryTypes)
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    var result = await lookupClient.QueryAsync(testDomain, queryType);
                    stopwatch.Stop();

                    validTests++;
                    totalLatency += (int)stopwatch.ElapsedMilliseconds;
                }
                catch
                {
                }

                await Task.Delay(100);
            }

            if (validTests > 0) return totalLatency / validTests;
        }
        catch
        {
        }

        return null;
    }

    private async Task<int?> MeasureRandomDnsLatency(IPAddress serverIP)
    {
        try
        {
            var lookupClient = CreateUdpClient(serverIP);
            var validTests = 0;
            var totalLatency = 0;

            for (var i = 0; i < 3; i++)
            {
                var randomDomain = GenerateRandomDomain();
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    var result = await lookupClient.QueryAsync(randomDomain, QueryType.A);
                    stopwatch.Stop();

                    validTests++;
                    totalLatency += (int)stopwatch.ElapsedMilliseconds;
                }
                catch
                {
                }

                await Task.Delay(100);
            }

            if (validTests > 0) return totalLatency / validTests;
        }
        catch
        {
        }

        return null;
    }

    private async Task<int?> MeasurePingLatency(IPAddress serverIP)
    {
        try
        {
            using var ping = new Ping();
            var validPings = 0;
            var totalPingLatency = 0;

            for (var i = 0; i < 4; i++)
                try
                {
                    var reply = await ping.SendPingAsync(serverIP, 3000, new byte[32]);
                    if (reply.Status == IPStatus.Success)
                    {
                        validPings++;
                        totalPingLatency += (int)reply.RoundtripTime;
                    }

                    await Task.Delay(100);
                }
                catch
                {
                }

            if (validPings > 0)
                return (int)(totalPingLatency / validPings * 1.2);
        }
        catch
        {
        }

        return null;
    }

    // ========= DoH / DoT / DoQ 实现 =========

    private async Task<int?> MeasureDohLatency(DnsServer server, string testDomain)
    {
        if (string.IsNullOrWhiteSpace(server.DohUrl)) return null;

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            var dnsQuery = BuildDnsQuery(testDomain, 1); // A
            using var req = new HttpRequestMessage(HttpMethod.Post, server.DohUrl);
            req.Headers.TryAddWithoutValidation("Accept", "application/dns-message");
            req.Content = new ByteArrayContent(dnsQuery);
            req.Content.Headers.ContentType =
                new MediaTypeHeaderValue("application/dns-message");

            var sw = Stopwatch.StartNew();
            // SendAsync 会完成 TCP 连接、TLS 握手和发送 HTTP 请求
            using var resp = await SharedHttpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            // 在收到响应头的瞬间，握手必然已经完成，此时停止计时
            sw.Stop();
            var handshakeLatency = (int)sw.ElapsedMilliseconds;

            // 继续完成正常的请求流程，以确保测量的有效性
            var respBytes = await resp.Content.ReadAsByteArrayAsync(cts.Token);

            if (!resp.IsSuccessStatusCode) return null;

            // 只有当整个 DNS 查询都有效时，才认为这次握手延迟测量是成功的
            if (IsValidDnsResponse(dnsQuery, respBytes)) return handshakeLatency;

            return null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<int?> MeasureDotLatency(DnsServer server, string testDomain)
    {
        if (string.IsNullOrWhiteSpace(server.DotHost)) return null;

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        try
        {
            using var tcp = new TcpClient();

            var sw = Stopwatch.StartNew();
            // 1. TCP 握手
            await tcp.ConnectAsync(server.PrimaryIP, server.DotPort, cts.Token);

            using var ssl = new SslStream(tcp.GetStream(), false,
                null);
            var sslOptions = new SslClientAuthenticationOptions
            {
                TargetHost = server.DotHost!,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            };
            // 2. TLS 握手
            await ssl.AuthenticateAsClientAsync(sslOptions, cts.Token);
            // TLS 握手完成，安全通道已建立，立即停止计时
            sw.Stop();
            var handshakeLatency = (int)sw.ElapsedMilliseconds;

            // 继续完成正常的请求流程
            var query = BuildDnsQuery(testDomain, 1);
            var framed = AddTcpLengthPrefix(query);

            await ssl.WriteAsync(framed, cts.Token);
            await ssl.FlushAsync(cts.Token);

            var resp = await ReadTcpPrefixedMessageAsync(ssl, cts.Token);

            // 只有当整个 DNS 查询都有效时，才认为这次握手延迟测量是成功的
            if (IsValidDnsResponse(query, resp)) return handshakeLatency;

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    ///     测量 DoQ 延迟。该函数现在包含一个诊断模式。
    /// </summary>
    /// <param name="server">要测试的 DNS 服务器。</param>
    /// <param name="testDomain">用于测试的域名。</param>
    /// <param name="checkOnlyConnection">【诊断模式】如果为 true，则仅测试连接握手，不执行DNS查询，行为与 QuicSelfCheck 完全一致。</param>
    /// <returns>握手延迟（毫秒），或在失败时返回 null。</returns>
    private async Task<int?> MeasureDoqLatency(DnsServer server, string testDomain, bool checkOnlyConnection = false)
    {
        if (!QuicConnection.IsSupported || string.IsNullOrWhiteSpace(server.DoqHost)) return null;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        async Task<IPAddress?> ResolveDoqIpAddressAsync()
        {
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(server.DoqHost, cts.Token);
                var targetIp = addresses.FirstOrDefault(ip => ip.AddressFamily == server.PrimaryIP.AddressFamily)
                               ?? addresses.FirstOrDefault();

                if (targetIp != null) return targetIp;
            }
            catch
            {
                /* 忽略解析失败 */
            }

            // 解析失败则回退到 PrimaryIP
            return server.PrimaryIP;
        }

        async Task<int?> AttemptAsync(int port)
        {
            if (cts.IsCancellationRequested) return null;

            var targetIp = await ResolveDoqIpAddressAsync();
            if (targetIp == null) return null;

            try
            {
                var endPoint = new IPEndPoint(targetIp, port);
                var options = new QuicClientConnectionOptions
                {
                    RemoteEndPoint = endPoint,
                    DefaultCloseErrorCode = 0,
                    DefaultStreamErrorCode = 0,
                    IdleTimeout = TimeSpan.FromSeconds(10), // 连接空闲超时
                    ClientAuthenticationOptions = new SslClientAuthenticationOptions
                    {
                        TargetHost = server.DoqHost,
                        ApplicationProtocols = new List<SslApplicationProtocol> { new("doq") },
                        RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true
                    }
                };

                var sw = Stopwatch.StartNew();
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                connectCts.CancelAfter(TimeSpan.FromSeconds(5));

                await using var conn = await QuicConnection.ConnectAsync(options, connectCts.Token);
                sw.Stop();
                var handshakeLatency = (int)sw.ElapsedMilliseconds;

                // --- 诊断模式开关 ---
                if (checkOnlyConnection)
                {
                    Debug.WriteLine(
                        $"[DoQ 诊断模式] 连接到 {targetIp}:{port} (Host: {server.DoqHost}) 成功! 耗时: {handshakeLatency} ms");
                    return handshakeLatency;
                }
                // --- 诊断模式结束 ---

                // 正常模式：继续完成一次完整的查询
                await using var stream = await conn.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cts.Token);
                var query = BuildDnsQuery(testDomain, 1);
                var framed = AddTcpLengthPrefix(query);
                await stream.WriteAsync(framed, cts.Token);
                stream.CompleteWrites();

                var resp = await ReadTcpPrefixedMessageAsync(stream, cts.Token);
                if (IsValidDnsResponse(query, resp)) return handshakeLatency;

                Debug.WriteLine($"[DoQ] 连接成功，但DNS响应无效。Host: {server.DoqHost}");
                return null;
            }
            catch (Exception ex)
            {
                var reason = ex is OperationCanceledException ? "超时" : $"QuicException ({ex.Message})";
                Debug.WriteLine($"[DoQ] 连接到 {targetIp}:{port} (Host: {server.DoqHost}) 失败. 原因: {reason}");
                return null;
            }
        }

        // --- 端口回退逻辑 ---
        var result = await AttemptAsync(server.DoqPort);
        if (result.HasValue) return result;

        if (server.DoqPort == 853) return await AttemptAsync(784);
        if (server.DoqPort == 784) return await AttemptAsync(853);

        return null;
    }

    /// <summary>
    ///     构建一个简单的 DNS 查询报文。
    /// </summary>
    /// <param name="domain">要查询的域名。</param>
    /// <param name="qtype">查询类型 (e.g., 1 for A, 28 for AAAA)。</param>
    /// <returns>包含 DNS 查询的字节数组。</returns>
    public static byte[] BuildDnsQuery(string domain, ushort qtype)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // 1. 事务 ID (随机生成)
        var transactionId = (ushort)new Random().Next(1, 65535);
        writer.Write(IPAddress.HostToNetworkOrder((short)transactionId));

        // 2. 标志位 (标准递归查询)
        // 0... .... .... .... = QR: 0 (Query)
        // .000 0... .... .... = Opcode: 0 (Standard Query)
        // .... ..0. .... .... = TC: 0 (Not Truncated)
        // .... ...1 .... .... = RD: 1 (Recursion Desired)
        // 其他为0
        writer.Write((ushort)IPAddress.HostToNetworkOrder(0x0100));

        // 3. 问题、回答、权威、附加记录的数量
        writer.Write(IPAddress.HostToNetworkOrder((short)1)); // QDCOUNT (1 question)
        writer.Write(IPAddress.HostToNetworkOrder((short)0)); // ANCOUNT
        writer.Write(IPAddress.HostToNetworkOrder((short)0)); // NSCOUNT
        writer.Write(IPAddress.HostToNetworkOrder((short)0)); // ARCOUNT

        // 4. 问题部分 (Question Section)
        // 4.1 查询名称 (QNAME)
        var labels = domain.Split('.');
        foreach (var label in labels)
        {
            var labelBytes = Encoding.ASCII.GetBytes(label);
            writer.Write((byte)labelBytes.Length);
            writer.Write(labelBytes);
        }

        writer.Write((byte)0x00); // 域名结束符

        // 4.2 查询类型 (QTYPE)
        writer.Write(IPAddress.HostToNetworkOrder((short)qtype));

        // 4.3 查询类别 (QCLASS) - 1 for IN (Internet)
        writer.Write(IPAddress.HostToNetworkOrder((short)1));

        return ms.ToArray();
    }

    /// <summary>
    ///     为 DNS 报文添加 2 字节的长度前缀 (用于 TCP 传输)。
    /// </summary>
    public static byte[] AddTcpLengthPrefix(byte[] query)
    {
        if (query.Length > ushort.MaxValue) throw new ArgumentException("Query is too large for TCP prefixing.");

        var prefixedQuery = new byte[query.Length + 2];
        var lengthBytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((short)query.Length));

        // 复制长度 (2字节)
        Buffer.BlockCopy(lengthBytes, 0, prefixedQuery, 0, 2);
        // 复制原始查询
        Buffer.BlockCopy(query, 0, prefixedQuery, 2, query.Length);

        return prefixedQuery;
    }

    /// <summary>
    ///     从流中异步读取带有 2 字节长度前缀的 DNS 报文。
    /// </summary>
    public static async Task<byte[]> ReadTcpPrefixedMessageAsync(Stream stream, CancellationToken ct)
    {
        // 1. 读取 2 字节的长度前缀
        var lengthBuffer = new byte[2];
        await stream.ReadExactlyAsync(lengthBuffer, 0, 2, ct);
        var length = (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(lengthBuffer, 0));

        // 2. 根据长度读取完整的 DNS 报文
        var messageBuffer = new byte[length];
        await stream.ReadExactlyAsync(messageBuffer, 0, length, ct);

        return messageBuffer;
    }

    /// <summary>
    ///     验证 DNS 响应是否有效。
    /// </summary>
    /// <param name="query">原始查询报文。</param>
    /// <param name="response">收到的响应报文。</param>
    /// <returns>如果响应有效则为 true，否则为 false。</returns>
    public static bool IsValidDnsResponse(byte[] query, byte[] response)
    {
        // 响应至少需要有 12 字节的头部
        if (response == null || response.Length < 12) return false;

        // 1. 检查事务 ID 是否匹配 (报文的前 2 个字节)
        if (query[0] != response[0] || query[1] != response[1]) return false;

        // 2. 检查响应标志位中的 RCODE (响应码)
        // RCODE 位于第 4 个字节的低 4 位
        // 0... .... .... .... = QR: 1 (Response)
        // .... .... .... 1111 = RCODE
        var flags = IPAddress.NetworkToHostOrder(BitConverter.ToInt16(response, 2));
        var rcode = flags & 0x0F; // 提取 RCODE

        // RCODE 为 0 表示 NoError (无错误)
        return rcode == 0;
    }

    // 获取常见公共DNS服务器列表 - 扩展端点
    public static DnsServer[] GetCommonDnsServers()
    {
        return new[]
        {
            new DnsServer("Google DNS", "8.8.8.8", "8.8.4.4",
                dohUrl: "https://dns.google/dns-query",
                dotHost: "dns.google",
                doqHost: null),

            new DnsServer("Cloudflare DNS", "1.1.1.1", "1.0.0.1",
                dohUrl: "https://cloudflare-dns.com/dns-query",
                dotHost: "dns.cloudflare.com",
                doqHost: null),

            new DnsServer("Quad9", "9.9.9.9", "149.112.112.112",
                dohUrl: "https://dns.quad9.net/dns-query",
                dotHost: "dns.quad9.net",
                doqHost: null),

            new DnsServer("OpenDNS", "208.67.222.222", "208.67.220.220",
                dohUrl: "https://doh.opendns.com/dns-query",
                dotHost: "dns.opendns.com",
                doqHost: null),

            new DnsServer("AdGuard DNS", "94.140.14.14", "94.140.15.15",
                dohUrl: "https://dns.adguard.com/dns-query",
                dotHost: "dns.adguard.com",
                doqHost: "dns.adguard.com"),

            new DnsServer("阿里 DNS", "223.5.5.5", "223.6.6.6",
                dohUrl: "https://dns.alidns.com/dns-query",
                dotHost: "dns.alidns.com",
                doqHost: "dns.alidns.com"),

            new DnsServer("DNSPod", "119.29.29.29", "182.254.116.116",
                dohUrl: "https://doh.pub/dns-query",
                dotHost: "dot.pub",
                doqHost: null),

            new DnsServer("114 DNS", "114.114.114.114", "114.114.115.115",
                dohUrl: null, dotHost: null, doqHost: null),

            new DnsServer("百度 DNS", "180.76.76.76",
                dohUrl: null, dotHost: null, doqHost: null),

            new DnsServer("360 DNS", "101.226.4.6", "218.30.118.6",
                dohUrl: "https://doh.360.cn/dns-query", dotHost: "doh.360.cn", doqHost: null),

            new DnsServer("CNNIC SDNS", "1.2.4.8", "210.2.4.8",
                dohUrl: null, dotHost: null, doqHost: null),

            new DnsServer("火山引擎 DNS", "180.184.1.1", "180.184.2.2",
                dohUrl: null, dotHost: null, doqHost: null)
        };
    }
}