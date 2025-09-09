using System.Diagnostics;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Text;

namespace DNSSpeedTester.Services;

/// <summary>
///     一个纯粹的 QUIC 握手延迟测试工具。
///     它只建立连接以测量握手时间，不发送任何应用数据。
/// </summary>
public static class QuicSelfCheck
{
    /// <summary>
    ///     运行 QUIC 握手延迟测试并生成报告。
    /// </summary>
    public static async Task<string> RunAsync()
    {
        var sb = new StringBuilder();
        sb.AppendLine("QUIC 握手延迟测试报告");
        sb.AppendLine(new string('-', 36));
        sb.AppendLine($"运行时: {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"OS: {Environment.OSVersion}");
        sb.AppendLine($"QuicConnection.IsSupported: {QuicConnection.IsSupported}");
        sb.AppendLine();

        if (!QuicConnection.IsSupported)
        {
            sb.AppendLine("当前环境不支持 QUIC。");
            return sb.ToString();
        }

        // 定义要测试的目标服务器列表
        var targets = new List<DnsTarget>
        {
            // new("Local", IPAddress.Parse("192.168.43.113"), 853, "local.dns.server"),
            new("AliDNS", IPAddress.Parse("223.5.5.5"), 853, "dns.alidns.com"),
            new("神绫", IPAddress.Parse("8.130.208.37"), 853, "dns.yuguan.xyz")
        };

        // 遍历每个目标进行测试
        foreach (var target in targets)
        {
            // 为每次连接尝试设置5秒超时
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var options = new QuicClientConnectionOptions
            {
                RemoteEndPoint = new IPEndPoint(target.Address, target.Port),
                DefaultCloseErrorCode = 0,
                DefaultStreamErrorCode = 0,
                IdleTimeout = TimeSpan.FromSeconds(10), // 连接空闲超时
                ClientAuthenticationOptions = new SslClientAuthenticationOptions
                {
                    TargetHost = target.TlsHost,
                    EnabledSslProtocols = SslProtocols.Tls13,
                    // 应用层协议协商(ALPN)是TLS握手的一部分，所以仍然需要指定它。
                    ApplicationProtocols = [new SslApplicationProtocol("doq")],
                    // 注意：如果本地服务器使用自签名证书，可能需要下面这个回调来忽略证书错误。
                    RemoteCertificateValidationCallback = (sender, cert, chain, errors) => true
                }
            };

            var sw = Stopwatch.StartNew();
            try
            {
                // 核心操作：仅建立QUIC连接。
                // 'await using' 会在代码块结束时自动、异步地关闭连接。
                await using var connection = await QuicConnection.ConnectAsync(options, cts.Token);
                sw.Stop();

                // 报告成功和耗时
                sb.AppendLine($"[成功] {target.Name,-18} ({target.Address}:{target.Port})");
                sb.AppendLine(
                    $"       耗时: {sw.ElapsedMilliseconds} ms, 协商协议: {connection.NegotiatedApplicationProtocol}");
            }
            catch (Exception ex)
            {
                sw.Stop();
                // 报告失败、耗时和原因
                var reason = ex switch
                {
                    OperationCanceledException => "操作超时",
                    QuicException qex => $"连接被中止 (错误: {qex.QuicError})",
                    AuthenticationException => "证书验证失败",
                    _ => ex.GetType().Name
                };
                sb.AppendLine($"[失败] {target.Name,-18} ({target.Address}:{target.Port})");
                sb.AppendLine($"       耗时: {sw.ElapsedMilliseconds} ms, 原因: {reason}");
            }

            sb.AppendLine(); // 增加空行以提高可读性
        }

        return sb.ToString();
    }

    // 定义一个测试目标
    private readonly record struct DnsTarget(string Name, IPAddress Address, int Port, string TlsHost);
}