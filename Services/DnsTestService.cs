using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using DnsClient;
using DNSSpeedTester.Models;

namespace DNSSpeedTester.Services;

public class DnsTestService
{
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


    // 生成随机域名 - 保持不变
    private static string GenerateRandomDomain()
    {
        return $"{Guid.NewGuid().ToString("N")[..8]}.example.com";
    }

    // 测试本机到DNS服务器的延迟 - 对外接口保持不变
    public async Task<DnsServer> TestDnsServerAsync(DnsServer server, string testDomain)
    {
        // 保存对当前server的引用
        var serverToTest = server;

        try
        {
            serverToTest.Status = "测试中...";
            serverToTest.Latency = null;

            // 使用增强的测试方法
            var finalLatency = await EnhancedDnsSpeedTest(serverToTest.PrimaryIP, testDomain);

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

    // 增强版DNS速度测试 - 完全重写的测试方法
    private async Task<int?> EnhancedDnsSpeedTest(IPAddress serverIP, string testDomain)
    {
        // 创建测试结果列表
        var latencies = new List<int>();

        // 1. TCP测试 - 较慢但更准确
        var tcpLatency = await MeasureTcpDnsLatency(serverIP, testDomain);
        if (tcpLatency.HasValue) latencies.Add(tcpLatency.Value);

        // 2. 强制刷新缓存的UDP测试
        var udpLatency = await MeasureUdpDnsLatency(serverIP, testDomain);
        if (udpLatency.HasValue) latencies.Add(udpLatency.Value);

        // 3. 随机子域名查询测试
        var randomLatency = await MeasureRandomDnsLatency(serverIP);
        if (randomLatency.HasValue) latencies.Add(randomLatency.Value);

        // 4. PING测试作为基线参考
        var pingLatency = await MeasurePingLatency(serverIP);
        if (pingLatency.HasValue) latencies.Add(pingLatency.Value);

        // 如果没有有效结果，返回null
        if (latencies.Count == 0) return null;

        // 结果处理策略
        // - 如果只有1个结果，直接返回
        // - 如果有2个结果，取较大值
        // - 如果有3个或更多结果，排序后取中值，避免异常值
        if (latencies.Count == 1) return latencies[0];

        if (latencies.Count == 2)
            // 取较大值能更好反映实际网络延迟
            return Math.Max(latencies[0], latencies[1]);

        // 排序并取中值
        latencies.Sort();
        return latencies[latencies.Count / 2];
    }

    // TCP DNS查询测试 - 强制建立连接，更准确反映网络路径
    private async Task<int?> MeasureTcpDnsLatency(IPAddress serverIP, string testDomain)
    {
        try
        {
            var lookupClient = new LookupClient(new LookupClientOptions
            {
                UseCache = false,
                Timeout = TimeSpan.FromSeconds(5),
                Retries = 0,
                // ServerIPs = new[] { serverIP },
                UseTcpOnly = true, // 强制使用TCP
                EnableAuditTrail = false
            });

            // 预热连接
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
                    /* 继续测试 */
                }

                // 间隔一段时间
                await Task.Delay(200);
            }

            if (validTests > 0) return totalLatency / validTests;
        }
        catch
        {
            /* 返回null */
        }

        return null;
    }

    // UDP DNS查询测试 - 更符合实际用户场景
    private async Task<int?> MeasureUdpDnsLatency(IPAddress serverIP, string testDomain)
    {
        try
        {
            var lookupClient = new LookupClient(new LookupClientOptions
            {
                UseCache = false,
                Timeout = TimeSpan.FromSeconds(3),
                Retries = 0,
                //ServerIPs = new[] { serverIP },
                UseTcpOnly = false, // 使用UDP
                EnableAuditTrail = false
            });

            // 不进行预热，直接测试真实情况

            // 使用不常见的记录类型，降低缓存命中率
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
                    /* 继续测试 */
                }

                // 间隔一段时间
                await Task.Delay(100);
            }

            if (validTests > 0) return totalLatency / validTests;
        }
        catch
        {
            /* 返回null */
        }

        return null;
    }

    // 随机子域名查询测试 - 完全避开任何缓存
    private async Task<int?> MeasureRandomDnsLatency(IPAddress serverIP)
    {
        try
        {
            var lookupClient = new LookupClient(new LookupClientOptions
            {
                UseCache = false,
                Timeout = TimeSpan.FromSeconds(3),
                Retries = 0,
                //ServerIPs = new[] { serverIP },
                UseTcpOnly = false,
                EnableAuditTrail = false
            });

            var validTests = 0;
            var totalLatency = 0;

            // 测试3次不同的随机域名
            for (var i = 0; i < 3; i++)
            {
                // 每次生成一个新的随机域名
                var randomDomain = GenerateRandomDomain();

                var stopwatch = Stopwatch.StartNew();
                try
                {
                    var result = await lookupClient.QueryAsync(randomDomain, QueryType.A);
                    stopwatch.Stop();

                    // 注意：即使查询不到结果也算有效测试，因为这是期望的
                    validTests++;
                    totalLatency += (int)stopwatch.ElapsedMilliseconds;
                }
                catch
                {
                    /* 继续测试 */
                }

                // 间隔一段时间
                await Task.Delay(100);
            }

            if (validTests > 0) return totalLatency / validTests;
        }
        catch
        {
            /* 返回null */
        }

        return null;
    }

    // PING测试 - 提供基线网络延迟参考
    private async Task<int?> MeasurePingLatency(IPAddress serverIP)
    {
        try
        {
            using (var ping = new Ping())
            {
                var validPings = 0;
                var totalPingLatency = 0;
                var pingsToRun = 4;

                for (var i = 0; i < pingsToRun; i++)
                    try
                    {
                        var reply = await ping.SendPingAsync(serverIP, 3000, new byte[32]);

                        if (reply.Status == IPStatus.Success)
                        {
                            validPings++;
                            totalPingLatency += (int)reply.RoundtripTime;
                        }

                        // 短暂休眠，避免过快发送ping请求
                        await Task.Delay(100);
                    }
                    catch
                    {
                        /* 忽略单次ping失败 */
                    }

                if (validPings > 0)
                    // Ping延迟通常比DNS查询低，所以乘以1.2作为修正
                    return (int)(totalPingLatency / validPings * 1.2);
            }
        }
        catch
        {
            /* 返回null */
        }

        return null;
    }

    // 获取常见公共DNS服务器列表 - 保持不变
    public static DnsServer[] GetCommonDnsServers()
    {
        return new[]
        {
            new DnsServer("Google DNS", "8.8.8.8", "8.8.4.4"),
            new DnsServer("Cloudflare DNS", "1.1.1.1", "1.0.0.1"),
            new DnsServer("Quad9", "9.9.9.9", "149.112.112.112"),
            new DnsServer("OpenDNS", "208.67.222.222", "208.67.220.220"),
            new DnsServer("AdGuard DNS", "94.140.14.14", "94.140.15.15"),
            new DnsServer("阿里 DNS", "223.5.5.5", "223.6.6.6"),
            new DnsServer("DNSPod", "119.29.29.29", "182.254.116.116"),
            new DnsServer("114 DNS", "114.114.114.114", "114.114.115.115"),
            new DnsServer("腾讯 DNS", "119.28.28.28", "182.254.118.118"),
            new DnsServer("百度 DNS", "180.76.76.76"),
            new DnsServer("360 DNS", "101.226.4.6", "218.30.118.6"),
            new DnsServer("CNNIC SDNS", "1.2.4.8", "210.2.4.8"),
            new DnsServer("DNS PAI", "101.226.4.6", "218.30.118.6"),
            new DnsServer("火山引擎 DNS", "180.184.1.1", "180.184.2.2")
        };
    }
}