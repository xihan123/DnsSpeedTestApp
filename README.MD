![DnsSpeedTestApp](https://socialify.git.ci/xihan123/DnsSpeedTestApp/image?description=1&forks=1&issues=1&language=1&name=1&owner=1&pulls=1&stargazers=1&theme=Auto)

![Windows](https://img.shields.io/badge/Platform-Windows-0078D6.svg)
![.NET](https://img.shields.io/badge/.NET-9.0-512BD4.svg)
[![Latest Release](https://img.shields.io/github/v/release/xihan123/DnsSpeedTestApp)](https://github.com/xihan123/DnsSpeedTestApp/releases)
![downloads](https://img.shields.io/github/downloads/xihan123/DnsSpeedTestApp/total)

# DNS 测速与设置工具

一个 Windows 下的 DNS 测速工具，用 C# / WPF 编写。测一下各家 DNS 在当前网络下的解析延迟，再选一个设置到网卡上。

## 功能

- 内置国内外公共 DNS，测速后按延迟排序，标记本次最快的服务器
- 支持 UDP/TCP、DoH、DoT、DoQ 四种协议测速
- 自定义 DNS 服务器和测试域名，支持新增、修改和删除
- 为指定网卡设置 DNS，也可以恢复自动获取
- 提供网卡诊断、QUIC/DoQ 自检和 Bootstrap DNS 设置
- 浅色、深色、跟随系统三种主题

## 下载与运行

到 [Releases](https://github.com/xihan123/DnsSpeedTestApp/releases) 下载发布版本。

框架依赖版本需要安装 [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/zh-cn/download/dotnet/9.0)，选择与程序架构对应的版本。

修改系统 DNS 需要管理员权限，右键程序选择「以管理员身份运行」。

## 使用说明

### 测速

1. 在「测速」页选择测试域名和协议
2. 点击「开始测速」，需要中断时点击「停止」
3. 测试完成后查看延迟和状态，最快的一项会自动选中

这里测的是 DNS 查询的往返延迟。换一个域名、换一个网络，排名都可能变化；解析快也不代表网页加载或下载一定更快。

「随机域名」会在每轮测速时更新，也可以手动刷新，用来减少缓存对结果的影响。

### 设置系统 DNS

1. 在「测速」列表中选中要使用的 DNS
2. 切换到「系统 DNS」，选择正在使用的网卡
3. 点击「设置所选 DNS」，查看状态栏是否设置成功

恢复自动获取时，点击「恢复 DHCP」。

设置到网卡的是所选服务器的主、备用 IP 地址。测速时选择 DoH、DoT 或 DoQ，不会同时给 Windows 配置加密 DNS。

### 自定义 DNS 和域名

在「自定义」页填写名称和地址后添加。

- DNS：主 DNS 地址必填，备用地址可留空；加密协议另填 DoH URL、DoT 或 DoQ 主机与端口
- 域名：填写名称和域名，例如 `www.example.com`，不需要加 `https://`
- 修改：在测速列表或域名下拉框中选中自定义条目，再回到「自定义」页修改并保存
- 删除 DNS：点击测速列表中自定义条目旁的删除按钮

配置保存在 `%LOCALAPPDATA%\DNSSpeedTester`，下次启动会自动读取。

### 高级设置

「高级」页可以设置 Bootstrap DNS 和主题。

Bootstrap DNS 用于解析 DoH、DoT、DoQ 服务器的域名，填写 UDP DNS 服务器 IP，留空则使用系统 DNS。

## 从源码运行

需要 Windows 和 .NET 9 SDK。仓库的 `global.json` 指定 SDK 9.0.304，并允许使用更新的 9.0 次版本。

```powershell
git clone https://github.com/xihan123/DnsSpeedTestApp.git
cd DnsSpeedTestApp
dotnet run --project DNSSpeedTester.csproj
```

发布 Windows x64 框架依赖版本：

```powershell
dotnet publish DNSSpeedTester.csproj -c Release -r win-x64 --self-contained false
```

## 常见问题

- **找不到网卡**：在「系统 DNS」页点击网卡诊断按钮，检查 WMI 服务是否正常运行。
- **设置 DNS 提示权限不足**：关闭程序，以管理员身份重新运行。
- **超时或失败**：先确认当前网络能连接该服务器；加密协议还需要服务器提供对应端点。
- **DoQ 无法使用**：在「系统 DNS」页运行 QUIC/DoQ 自检，查看系统支持情况和连接结果。
- **设置后仍使用旧的解析结果**：运行 `ipconfig /flushdns` 清除系统 DNS 缓存，通过 `ipconfig /all` 确认网卡 DNS
  地址。浏览器开启了自己的安全 DNS 时，也需要检查浏览器设置。

遇到其他问题可以提 [Issue](https://github.com/xihan123/DnsSpeedTestApp/issues)，附上使用的协议、测试域名和报错信息。

## 赞助

本项目 CDN 加速及安全防护由 Tencent EdgeOne 赞助。

[![EdgeOne](https://edgeone.ai/media/34fe3a45-492d-4ea4-ae5d-ea1087ca7b4b.png)](https://edgeone.ai/zh?from=github)
