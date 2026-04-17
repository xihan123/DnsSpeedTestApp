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
    private static readonly HttpClient NoProxyHttpClient = new(new HttpClientHandler
    {
        UseProxy = false
    })
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    private sealed record TestResult(int? LatencyMs, string? ErrorMessage, string? ErrorCategory);

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

            TestResult result;

            switch (protocol)
            {
                case DnsProtocol.UdpTcp:
                {
                    var latency = await EnhancedDnsSpeedTest(serverToTest.PrimaryIP, testDomain);
                    result = latency.HasValue
                        ? new TestResult(latency, null, null)
                        : new TestResult(null, "DNS查询失败或超时", "Timeout");
                    break;
                }
                case DnsProtocol.DoH:
                    result = await WithRetryAsync(ct => MeasureDohLatency(serverToTest, testDomain, ct));
                    break;
                case DnsProtocol.DoT:
                    result = await WithRetryAsync(ct => MeasureDotLatency(serverToTest, testDomain, ct));
                    break;
                case DnsProtocol.DoQ:
                    result = await MeasureDoqLatency(serverToTest, testDomain);
                    break;
                default:
                    result = new TestResult(null, "不支持的协议", "Protocol");
                    break;
            }

            if (result.LatencyMs.HasValue)
            {
                serverToTest.Latency = result.LatencyMs.Value;
                serverToTest.Status = "成功";
                serverToTest.StatusDetail = result.ErrorMessage ?? $"DNS响应时间: {result.LatencyMs}ms";
            }
            else
            {
                serverToTest.Status = result.ErrorCategory switch
                {
                    "Timeout" => "超时",
                    "Tls" => "证书错误",
                    "TlsWarning" => "证书警告",
                    "Connection" => "连接失败",
                    "NotSupported" => "不支持",
                    "Protocol" => "协议错误",
                    _ => "错误"
                };
                serverToTest.StatusDetail = result.ErrorMessage ?? "未知错误";
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
    private async Task<int?> EnhancedDnsSpeedTest(IPAddress serverIp, string testDomain)
    {
        var latencies = new List<int>();

        var tcpLatency = await MeasureTcpDnsLatency(serverIp, testDomain);
        if (tcpLatency.HasValue) latencies.Add(tcpLatency.Value);

        var udpLatency = await MeasureUdpDnsLatency(serverIp, testDomain);
        if (udpLatency.HasValue) latencies.Add(udpLatency.Value);

        var randomLatency = await MeasureRandomDnsLatency(serverIp);
        if (randomLatency.HasValue) latencies.Add(randomLatency.Value);

        var pingLatency = await MeasurePingLatency(serverIp);
        if (pingLatency.HasValue) latencies.Add(pingLatency.Value);

        if (latencies.Count == 0) return null;

        if (latencies.Count == 1) return latencies[0];
        if (latencies.Count == 2) return Math.Max(latencies[0], latencies[1]);

        latencies.Sort();
        return latencies[latencies.Count / 2];
    }

    private LookupClient CreateTcpClient(IPAddress serverIp)
    {
        var options = new LookupClientOptions(new IPEndPoint(serverIp, 53))
        {
            UseCache = false,
            Timeout = TimeSpan.FromSeconds(5),
            Retries = 0,
            UseTcpOnly = true,
            EnableAuditTrail = false
        };
        return new LookupClient(options);
    }

    private LookupClient CreateUdpClient(IPAddress serverIp)
    {
        var options = new LookupClientOptions(new IPEndPoint(serverIp, 53))
        {
            UseCache = false,
            Timeout = TimeSpan.FromSeconds(5),
            Retries = 0,
            UseTcpOnly = false,
            EnableAuditTrail = false
        };
        return new LookupClient(options);
    }

    private async Task<int?> MeasureTcpDnsLatency(IPAddress serverIp, string testDomain)
    {
        try
        {
            var lookupClient = CreateTcpClient(serverIp);

            try
            {
                await lookupClient.QueryAsync("www.example.com", QueryType.A);
            }
            catch (Exception)
            {
                // 预热错误可以忽略
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
                    var queryResult = await lookupClient.QueryAsync(testDomain, QueryType.A);
                    stopwatch.Stop();

                    if (!queryResult.HasError)
                    {
                        validTests++;
                        totalLatency += (int)stopwatch.ElapsedMilliseconds;
                    }
                }
                catch (Exception)
                {
                    // 单次查询失败可以忽略
                }

                await Task.Delay(200);
            }

            if (validTests > 0) return totalLatency / validTests;
        }
        catch (Exception)
        {
            // 整体方法失败
        }

        return null;
    }

    private async Task<int?> MeasureUdpDnsLatency(IPAddress serverIp, string testDomain)
    {
        try
        {
            var lookupClient = CreateUdpClient(serverIp);
            var queryTypes = new[] { QueryType.AAAA, QueryType.MX, QueryType.TXT };

            var validTests = 0;
            var totalLatency = 0;

            foreach (var queryType in queryTypes)
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    var queryResult = await lookupClient.QueryAsync(testDomain, queryType);
                    stopwatch.Stop();

                    if (!queryResult.HasError)
                    {
                        validTests++;
                        totalLatency += (int)stopwatch.ElapsedMilliseconds;
                    }
                }
                catch (Exception)
                {
                    // 单次查询失败可以忽略
                }

                await Task.Delay(100);
            }

            if (validTests > 0) return totalLatency / validTests;
        }
        catch (Exception)
        {
            // 整体方法失败
        }

        return null;
    }

    private async Task<int?> MeasureRandomDnsLatency(IPAddress serverIp)
    {
        try
        {
            var lookupClient = CreateUdpClient(serverIp);
            var validTests = 0;
            var totalLatency = 0;

            for (var i = 0; i < 3; i++)
            {
                var randomDomain = GenerateRandomDomain();
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    var queryResult = await lookupClient.QueryAsync(randomDomain, QueryType.A);
                    stopwatch.Stop();

                    if (!queryResult.HasError)
                    {
                        validTests++;
                        totalLatency += (int)stopwatch.ElapsedMilliseconds;
                    }
                }
                catch (Exception)
                {
                    // 单次查询失败可以忽略
                }

                await Task.Delay(100);
            }

            if (validTests > 0) return totalLatency / validTests;
        }
        catch (Exception)
        {
            // 整体方法失败
        }

        return null;
    }

    private async Task<int?> MeasurePingLatency(IPAddress serverIp)
    {
        try
        {
            using var ping = new Ping();
            var validPings = 0;
            var totalPingLatency = 0;

            for (var i = 0; i < 4; i++)
                try
                {
                    var reply = await ping.SendPingAsync(serverIp, 3000, new byte[32]);
                    if (reply.Status == IPStatus.Success)
                    {
                        validPings++;
                        totalPingLatency += (int)reply.RoundtripTime;
                    }

                    await Task.Delay(100);
                }
                catch (Exception)
                {
                    // 单次 ping 失败可以忽略
                }

            if (validPings > 0)
                return (int)Math.Round(totalPingLatency * 1.2 / validPings);
        }
        catch (Exception)
        {
            // 整体方法失败
        }

        return null;
    }

    // ========= DoH / DoT / DoQ 实现 =========

    private static string FormatSocketError(SocketError code) => code switch
    {
        SocketError.HostNotFound => "域名解析失败",
        SocketError.HostUnreachable => "主机不可达",
        SocketError.NetworkUnreachable => "网络不可达",
        SocketError.ConnectionRefused => "连接被拒绝",
        SocketError.TimedOut => "连接超时",
        SocketError.NoData => "域名无对应记录",
        _ => code.ToString()
    };

    private static string DiagnoseDnsResponse(byte[] query, byte[]? response)
    {
        if (response == null || response.Length < 12) return "响应过短";
        if (query[0] != response[0] || query[1] != response[1]) return "事务ID不匹配";
        var flags = IPAddress.NetworkToHostOrder(BitConverter.ToInt16(response, 2));
        var rcode = flags & 0x0F;
        return rcode switch
        {
            0 => "未知原因",
            1 => "Format Error",
            2 => "Server Failure",
            3 => "域名不存在 (NXDomain)",
            4 => "未实现 (NotImplemented)",
            5 => "拒绝查询 (Refused)",
            _ => $"RCODE={rcode}"
        };
    }

    private static async Task<IPAddress?> ResolveHostAsync(string hostname, AddressFamily preferredFamily,
        CancellationToken ct)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(hostname, ct);
            return addresses.FirstOrDefault(ip => ip.AddressFamily == preferredFamily)
                   ?? addresses.FirstOrDefault();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<TestResult> WithRetryAsync(
        Func<CancellationToken, Task<TestResult>> action, int maxRetries = 1)
    {
        TestResult? lastResult = null;
        for (var i = 0; i <= maxRetries; i++)
        {
            if (i > 0) await Task.Delay(500);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            try
            {
                lastResult = await action(cts.Token);
                if (lastResult.LatencyMs.HasValue) return lastResult;
                // 不支持的协议不需要重试
                if (lastResult.ErrorCategory == "NotSupported") return lastResult;
            }
            catch (OperationCanceledException)
            {
                lastResult = new TestResult(null, $"第{i + 1}次尝试超时", "Timeout");
            }
        }

        return lastResult ?? new TestResult(null, "所有重试均失败", "Timeout");
    }

    private async Task<TestResult> MeasureDohLatency(DnsServer server, string testDomain, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(server.DohUrl))
            return new TestResult(null, "该服务器不支持DoH协议", "NotSupported");

        try
        {
            var dnsQuery = BuildDnsQuery(testDomain, 1);
            using var req = new HttpRequestMessage(HttpMethod.Post, server.DohUrl);
            req.Headers.TryAddWithoutValidation("Accept", "application/dns-message");
            req.Content = new ByteArrayContent(dnsQuery);
            req.Content.Headers.ContentType =
                new MediaTypeHeaderValue("application/dns-message");

            var sw = Stopwatch.StartNew();
            using var resp = await NoProxyHttpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            sw.Stop();
            var handshakeLatency = (int)sw.ElapsedMilliseconds;

            var respBytes = await resp.Content.ReadAsByteArrayAsync(ct);

            if (!resp.IsSuccessStatusCode)
                return new TestResult(null, $"HTTP {(int)resp.StatusCode}", "Connection");

            if (IsValidDnsResponse(dnsQuery, respBytes))
                return new TestResult(handshakeLatency, null, null);

            return new TestResult(null, $"DNS响应无效: {DiagnoseDnsResponse(dnsQuery, respBytes)}", "Protocol");
        }
        catch (OperationCanceledException)
        {
            return new TestResult(null, "连接超时", "Timeout");
        }
        catch (HttpRequestException ex) when (ex.InnerException is SocketException se)
        {
            return new TestResult(null, $"网络连接失败: {FormatSocketError(se.SocketErrorCode)}", "Connection");
        }
        catch (HttpRequestException ex)
        {
            return new TestResult(null, $"HTTP请求失败: {ex.Message}", "Connection");
        }
    }

    private async Task<TestResult> MeasureDotLatency(DnsServer server, string testDomain, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(server.DotHost))
            return new TestResult(null, "该服务器不支持DoT协议", "NotSupported");

        try
        {
            var targetIp = await ResolveHostAsync(server.DotHost!, server.PrimaryIP.AddressFamily, ct)
                           ?? server.PrimaryIP;

            using var tcp = new TcpClient();
            var sw = Stopwatch.StartNew();
            await tcp.ConnectAsync(targetIp, server.DotPort, ct);

            using var ssl = new SslStream(tcp.GetStream(), false);
            var sslOptions = new SslClientAuthenticationOptions
            {
                TargetHost = server.DotHost!,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            };
            await ssl.AuthenticateAsClientAsync(sslOptions, ct);
            sw.Stop();
            var handshakeLatency = (int)sw.ElapsedMilliseconds;

            var query = BuildDnsQuery(testDomain, 1);
            var framed = AddTcpLengthPrefix(query);

            await ssl.WriteAsync(framed, ct);
            await ssl.FlushAsync(ct);

            var resp = await ReadTcpPrefixedMessageAsync(ssl, ct);

            if (IsValidDnsResponse(query, resp))
                return new TestResult(handshakeLatency, null, null);

            return new TestResult(null, $"DNS响应无效: {DiagnoseDnsResponse(query, resp)}", "Protocol");
        }
        catch (OperationCanceledException)
        {
            return new TestResult(null, "连接超时", "Timeout");
        }
        catch (AuthenticationException ex)
        {
            return new TestResult(null, $"TLS证书验证失败: {ex.Message}", "Tls");
        }
        catch (SocketException ex)
        {
            return new TestResult(null, $"TCP连接失败: {FormatSocketError(ex.SocketErrorCode)}", "Connection");
        }
        catch (IOException ex)
        {
            return new TestResult(null, $"IO错误: {ex.Message}", "Connection");
        }
    }

    private async Task<TestResult> MeasureDoqLatency(DnsServer server, string testDomain,
        bool checkOnlyConnection = false)
    {
        if (!QuicConnection.IsSupported)
            return new TestResult(null, "系统不支持QUIC协议", "NotSupported");
        if (string.IsNullOrWhiteSpace(server.DoqHost))
            return new TestResult(null, "该服务器不支持DoQ协议", "NotSupported");

        using var outerCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        // 提取 Token 避免在局部函数中捕获 using 变量
        var outerToken = outerCts.Token;

        async Task<TestResult> AttemptAsync(int port, bool bypassCert = false)
        {
            if (outerToken.IsCancellationRequested)
                return new TestResult(null, "操作已取消", "Timeout");

            var targetIp = await ResolveHostAsync(server.DoqHost!, server.PrimaryIP.AddressFamily, outerToken)
                           ?? server.PrimaryIP;

            try
            {
                var endPoint = new IPEndPoint(targetIp, port);
                var options = new QuicClientConnectionOptions
                {
                    RemoteEndPoint = endPoint,
                    DefaultCloseErrorCode = 0,
                    DefaultStreamErrorCode = 0,
                    IdleTimeout = TimeSpan.FromSeconds(10),
                    ClientAuthenticationOptions = new SslClientAuthenticationOptions
                    {
                        TargetHost = server.DoqHost,
                        ApplicationProtocols = [new SslApplicationProtocol("doq")],
                        RemoteCertificateValidationCallback = bypassCert
                            ? new RemoteCertificateValidationCallback((_, _, _, _) => true)
                            : null
                    }
                };

                var sw = Stopwatch.StartNew();
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
                connectCts.CancelAfter(TimeSpan.FromSeconds(5));

                await using var conn = await QuicConnection.ConnectAsync(options, connectCts.Token);
                sw.Stop();
                var handshakeLatency = (int)sw.ElapsedMilliseconds;

                if (checkOnlyConnection)
                {
                    Debug.WriteLine(
                        $"[DoQ 诊断模式] 连接到 {targetIp}:{port} (Host: {server.DoqHost}) 成功! 耗时: {handshakeLatency} ms");
                    return new TestResult(handshakeLatency, null, null);
                }

                await using var stream =
                    await conn.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, outerToken);
                var query = BuildDnsQuery(testDomain, 1);
                var framed = AddTcpLengthPrefix(query);
                await stream.WriteAsync(framed, outerToken);
                stream.CompleteWrites();

                var resp = await ReadTcpPrefixedMessageAsync(stream, outerToken);
                if (IsValidDnsResponse(query, resp))
                    return new TestResult(handshakeLatency,
                        bypassCert ? "证书验证已跳过" : null,
                        bypassCert ? "TlsWarning" : null);

                return new TestResult(null, $"DNS响应无效: {DiagnoseDnsResponse(query, resp)}", "Protocol");
            }
            catch (OperationCanceledException)
            {
                return new TestResult(null, $"连接 {targetIp}:{port} 超时", "Timeout");
            }
            catch (AuthenticationException ex)
            {
                return new TestResult(null, $"TLS失败({targetIp}:{port}): {ex.Message}", "Tls");
            }
            catch (Exception ex)
            {
                return new TestResult(null, $"QUIC连接失败({targetIp}:{port}): {ex.Message}", "Connection");
            }
        }

        // 端口回退：先严格验证证书，TLS 失败则回退到跳过验证
        int[] ports = server.DoqPort == 853 ? [853, 784] : server.DoqPort == 784 ? [784, 853] : [server.DoqPort];

        TestResult? lastError = null;
        foreach (var port in ports)
        {
            if (outerToken.IsCancellationRequested) break;

            var attemptResult = await AttemptAsync(port, bypassCert: false);
            if (attemptResult.LatencyMs.HasValue) return attemptResult;

            // TLS 证书错误时，用跳过证书验证重试一次
            if (attemptResult.ErrorCategory == "Tls")
            {
                var bypassResult = await AttemptAsync(port, bypassCert: true);
                if (bypassResult.LatencyMs.HasValue) return bypassResult;
            }

            lastError = attemptResult;
        }

        return lastError ?? new TestResult(null, "所有尝试均失败", "Timeout");
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
        var transactionId = (ushort)Random.Shared.Next(1, 65535);
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
    public static bool IsValidDnsResponse(byte[] query, byte[]? response)
    {
        // 响应至少需要有 12 字节的头部
        if (response is not { Length: >= 12 }) return false;

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

    public static DnsServer[] GetCommonDnsServers()
    {
        return
        [
            new DnsServer("Google DNS", "8.8.8.8", "8.8.4.4",
                dohUrl: "https://dns.google/dns-query",
                dotHost: "dns.google"),

            new DnsServer("Cloudflare DNS", "1.1.1.1", "1.0.0.1",
                dohUrl: "https://dns.cloudflare.com/dns-query",
                dotHost: "one.one.one.one"),

            new DnsServer("Quad9", "9.9.9.9", "149.112.112.112",
                dohUrl: "https://dns.quad9.net/dns-query",
                dotHost: "dns.quad9.net"),

            new DnsServer("OpenDNS", "208.67.222.222", "208.67.220.220",
                dohUrl: "https://doh.opendns.com/dns-query",
                dotHost: "dns.opendns.com"),

            new DnsServer("AdGuard DNS", "94.140.14.14", "94.140.15.15",
                dohUrl: "https://dns.adguard-dns.com/dns-query",
                dotHost: "dns.adguard-dns.com",
                doqHost: "dns.adguard-dns.com"),

            new DnsServer("ControlD", "76.76.2.0", "76.76.10.0",
                dohUrl: "https://freedns.controld.com/p0",
                dotHost: "p0.freedns.controld.com"),

            new DnsServer("CleanBrowsing", "185.228.168.168", "185.228.169.168",
                dohUrl: "https://doh.cleanbrowsing.org/doh/family-filter/",
                dotHost: "family-filter-dns.cleanbrowsing.org"),

            new DnsServer("Mullvad DNS", "194.242.2.2",
                dohUrl: "https://dns.mullvad.net/dns-query",
                dotHost: "dns.mullvad.net"),

            new DnsServer("Surfshark DNS", "194.169.169.169",
                dohUrl: "https://dns.surfsharkdns.com/dns-query",
                dotHost: "dns.surfsharkdns.com",
                doqHost: "dns.surfsharkdns.com"),

            new DnsServer("Verisign DNS", "64.6.64.6", "64.6.65.6"),

            new DnsServer("Hurricane Electric", "74.82.42.42",
                dohUrl: "https://ordns.he.net/dns-query",
                dotHost: "ordns.he.net"),

            new DnsServer("DNS.SB", "185.222.222.222", "45.11.45.11",
                dohUrl: "https://doh.dns.sb/dns-query",
                dotHost: "dot.sb"),

            new DnsServer("Yandex DNS", "77.88.8.8", "77.88.8.1",
                dohUrl: "https://common.dot.dns.yandex.net/dns-query",
                dotHost: "common.dot.dns.yandex.net"),

            new DnsServer("Quad101", "101.101.101.101", "101.102.103.104",
                dohUrl: "https://dns.twnic.tw/dns-query",
                dotHost: "101.101.101.101"),

            new DnsServer("Level3 DNS", "4.2.2.1", "4.2.2.2"),

            new DnsServer("阿里 DNS", "223.5.5.5", "223.6.6.6",
                dohUrl: "https://dns.alidns.com/dns-query",
                dotHost: "dns.alidns.com",
                doqHost: "dns.alidns.com"),

            new DnsServer("DNSPod", "119.29.29.29", "182.254.116.116",
                dohUrl: "https://doh.pub/dns-query",
                dotHost: "dot.pub"),

            new DnsServer("114 DNS", "114.114.114.114", "114.114.115.115"),

            new DnsServer("百度 DNS", "180.76.76.76"),

            new DnsServer("360 DNS", "101.226.4.6", "218.30.118.6",
                dohUrl: "https://doh.360.cn/dns-query",
                dotHost: "dot.360.cn"),

            new DnsServer("CNNIC SDNS", "1.2.4.8", "210.2.4.8"),

            new DnsServer("火山引擎 DNS", "180.184.1.1", "180.184.2.2"),

            new DnsServer("OneDNS", "117.50.10.10", "52.80.52.52",
                dohUrl: "https://doh-pure.onedns.net/dns-query",
                dotHost: "dot-pure.onedns.net")
        ];
    }
}
