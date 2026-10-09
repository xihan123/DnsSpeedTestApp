# DNSSpeedTester UI 重构设计文档

> 版本：v1.0 ｜ 日期：2026-10-09 ｜ 适用分支：`master`
> 范围取向（已与需求方确认）：**Material Design 3 精修** · **导航式重构** · **浅/深色 + 跟随系统** · **视觉 + 轻度架构**

---

## 目录

1. [背景与目标](#1-背景与目标)
2. [设计原则](#2-设计原则)
3. [信息架构与导航](#3-信息架构与导航)
4. [视觉系统（Material 3 精修）](#4-视觉系统material-3-精修)
5. [主题与深色模式方案](#5-主题与深色模式方案)
6. [各视图设计与线框图](#6-各视图设计与线框图)
7. [组件规范](#7-组件规范)
8. [配套架构调整（轻度）](#8-配套架构调整轻度)
9. [落地步骤（分阶段）](#9-落地步骤分阶段)
10. [验证方式](#10-验证方式)
11. [范围与非目标](#11-范围与非目标)

---

## 1. 背景与目标

### 1.1 为什么重构

当前 UI 为**单窗口单屏**布局（`MainWindow.xaml` 505 行），把网卡设置、测试配置、结果表格、两个添加表单（测试域名、自定义 DNS + 协议端点）全部堆叠在一屏，并由唯一的 `MainViewModel`（712 行）承载全部逻辑。随着 DoH/DoT/DoQ、Bootstrap DNS、协议端点编辑等功能叠加，单屏已显拥挤，且存在若干与 UI 直接相关的体验问题（详见随附代码审查）：

| 现状问题 | 关联审查项 | 本次重构是否覆盖 |
|---|---|---|
| 测速不可取消，最坏可假死约 60s，仅有不确定进度条 | A‑P1 / C‑P2 | ✅ 覆盖（停止按钮 + 取消令牌 + 进度回传） |
| 无深色模式；配色/状态色散落在 XAML 的大量 `DataTrigger` 中，含死样式（`部分成功`） | C‑P3 | ✅ 覆盖（主题 + 状态色集中化） |
| 自定义 DoH URL / 主机名无输入校验，错误到测速时才以通用"错误"暴露 | C‑P3 | ✅ 覆盖（输入即校验） |
| `Bootstrap` 文本框逐键解析并写文件 | C‑P3 | ✅ 覆盖（防抖/失焦提交） |
| 无边框窗口最大化遮挡任务栏 | C‑P3 | ✅ 覆盖（WindowChrome） |
| 单屏信息过载、层级不清 | — | ✅ 覆盖（导航式重构） |
| DataGrid 列级 `IsReadOnly` 标注不一致（非功能缺陷） | C‑P3 | ✅ 顺带清理 |

> 引擎级问题（DoQ 事务 ID、UDP/TCP 口径与总超时、Bootstrap 异步超时失效、持久化单条容错、延迟口径统一）属"视觉 + 深度修复"范畴，本次**不作为必做项**，但在 [§11](#11-范围与非目标) 列为后续跟进并给出指针。

### 1.2 目标

- **结构清晰**：按任务把界面拆成 4 个导航区，降低单屏密度。
- **观感统一**：沿用 MaterialDesignThemes（无需换框架），统一间距/圆角/层级/状态色。
- **主题完整**：浅色 / 深色 / 跟随系统，可切换并持久化。
- **体验闭环**：测速可停止、输入即校验、关键操作有明确反馈。
- **低风险落地**：复用现有服务与模型，分阶段迁移，每阶段可独立构建运行。

---

## 2. 设计原则

1. **复用优先**：保留 `DnsTestService` / `DnsSettingService` / `DataPersistenceService` 等服务层与 `DnsServer`/`TestDomain`/`NetworkAdapter` 模型，不改其公共契约（除为取消而新增的 `CancellationToken` 重载）。
2. **原生 Material**：优先用 MaterialDesignThemes 既有样式（如 `MaterialDesignNavigationRailTabControl`），少写自定义模板。
3. **语义化令牌**：颜色/状态不写死，统一走 `DynamicResource` 与转换器，保证深浅色自动适配。
4. **MVVM 纯净**：业务留在服务层，视图逻辑进 ViewModel；移除服务层/`code-behind` 里的 `MessageBox`（改由 VM 经对话框服务呈现）。
5. **渐进增强**：先搬运结构、再加能力，避免一次性大改。

---

## 3. 信息架构与导航

### 3.1 导航结构（左侧导航栏 Navigation Rail）

采用 MaterialDesignThemes 自带的 **`MaterialDesignNavigationRailTabControl`**（左侧竖向导航 + 右侧内容区），4 个一级入口：

```
┌───────────────────────────────────────────────────────────────────────┐
│  [≡] DNS 测速与设置工具                      [☀/🌙 主题]  — ▢ ✕          │  ← 自定义标题栏
├────────────┬──────────────────────────────────────────────────────────┤
│            │                                                            │
│  ⚡ 测速    │                                                            │
│            │                                                            │
│  🖧 系统DNS │                 （当前导航项对应的内容区）                   │
│            │                                                            │
│  ✎ 自定义  │                                                            │
│            │                                                            │
│  ⚙ 高级    │                                                            │
│            │                                                            │
├────────────┴──────────────────────────────────────────────────────────┤
│  状态栏：{StatusMessage}                              [⟳ 忙碌指示]        │
└───────────────────────────────────────────────────────────────────────┘
```

| 导航项 | 图标（PackIcon）| 职责 |
|---|---|---|
| **测速** | `Speedometer` | 选择域名/协议 → 开始/停止测速 → 结果列表与推荐 |
| **系统 DNS** | `Dns` / `Lan` | 选网卡、查看当前 DNS、设为系统 DNS、恢复 DHCP、管理员状态 |
| **自定义** | `PlaylistEdit` | 管理自定义 DNS 服务器（含协议端点）与自定义测试域名 |
| **高级** | `Cog` / `Tune` | Bootstrap DNS、QUIC 自检、网络诊断、主题设置、关于 |

### 3.2 为什么选导航栏而非标签页

- 桌面工具 4 个区，竖向 Rail 常驻、切换成本低，且 Material 3 原生样式即可，几乎零自定义模板。
- 结果区（测速）需要更大横向空间给 DataGrid，Rail 占用窄、不挤压表格。

### 3.3 跨区联动

- 在"测速"选中某行后，提供 **"设为系统 DNS"** 快捷动作：切到"系统 DNS"并带入所选服务器。
- "系统 DNS"顶部若检测到非管理员，显示告警横幅并引导（见 [§6.2](#62-系统-dns-视图)）。

---

## 4. 视觉系统（Material 3 精修）

### 4.1 主题基色

沿用现有 `BundledTheme`，主色/辅色保持 **DeepPurple / Lime**（降低风险），但统一改为可运行时切换（见 §5）。

```xml
<materialDesign:BundledTheme BaseTheme="Inherit"
                             PrimaryColor="DeepPurple"
                             SecondaryColor="Lime"
                             ColorAdjustment="{materialDesign:ColorAdjustment}" />
```

> `BaseTheme="Inherit"` 配合运行时 `ThemeService` 设定浅/深；`ColorAdjustment` 提升对比度可读性。

### 4.2 间距与圆角（Design Tokens）

统一为 4 的倍数，集中定义到 `Resources/Tokens.xaml`：

| Token | 值 | 用途 |
|---|---|---|
| `SpaceXS` | 4 | 图标与文字间距 |
| `SpaceS` | 8 | 控件内间距 |
| `SpaceM` | 16 | 卡片内边距、控件间距（主） |
| `SpaceL` | 24 | 区块间距 |
| `CornerCard` | 8 | 卡片圆角（沿用现状） |
| `ElevationCard` | Dp2 | 卡片层级 |

### 4.3 状态语义色（集中化，替代散落的 DataTrigger）

把 `MainWindow.xaml:283-318` 的一长串 `DataTrigger` 收敛为 **`StatusToBrushConverter`**（或资源字典查表），且深浅色各一套，经 `DynamicResource` 适配：

| 状态 | 语义 | 浅色 | 深色 |
|---|---|---|---|
| 成功 | Success | `#2E7D32` | `#66BB6A` |
| 证书警告 / 部分成功 | Warning | `#EF6C00` | `#FFB74D` |
| 超时 / 连接失败 / 证书错误 / 协议错误 / 错误 | Error | `#C62828` | `#EF5350` |
| 测试中… | Info | `Primary` | `Primary` |
| 未测试 / 不支持 | Muted | `#9E9E9E` | `#757575` |

> 顺带删除死样式 `Value="部分成功"`（引擎从不产生）或在引擎补充该状态，二选一。本次按"删除死样式"处理。

### 4.4 字体与排版

- 保持 `MaterialDesignFont`，字号层级：标题 20 / 小标题 16 / 正文 13 / 辅助 12。
- 延迟数值用等宽感知的 `TextBlock` + 徽标（见 §7），突出可比性。

---

## 5. 主题与深色模式方案

### 5.1 运行时切换（MaterialDesignThemes 5.x）

新增 `Services/ThemeService.cs`：

```csharp
public enum AppThemeMode { Light, Dark, System }

public sealed class ThemeService
{
    private readonly PaletteHelper _palette = new();

    public void Apply(AppThemeMode mode)
    {
        var baseTheme = mode switch
        {
            AppThemeMode.Light => BaseTheme.Light,
            AppThemeMode.Dark  => BaseTheme.Dark,
            _                  => ReadSystemBaseTheme() // 跟随系统
        };
        var theme = _palette.GetTheme();
        theme.SetBaseTheme(baseTheme == BaseTheme.Dark ? Theme.Dark : Theme.Light);
        _palette.SetTheme(theme);
    }

    private static BaseTheme ReadSystemBaseTheme()
    {
        // HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var light = (int?)key?.GetValue("AppsUseLightTheme") ?? 1;
        return light == 1 ? BaseTheme.Light : BaseTheme.Dark;
    }
}
```

### 5.2 跟随系统的动态响应

订阅 `Microsoft.Win32.SystemEvents.UserPreferenceChanged`（`Category == General`），当处于"跟随系统"模式时重新 `Apply`。注意在应用退出时取消订阅，避免静态事件泄漏。

### 5.3 持久化

新增 `app_settings.json`（`%LOCALAPPDATA%/DNSSpeedTester/`），通过扩展 `DataPersistenceService` 存读 `{ "ThemeMode": "System" }`。启动时读取并应用，默认 `System`。

### 5.4 深色模式适配清单

- 所有硬编码颜色（如标题栏 `Foreground="AliceBlue"`、关闭键 `#FFFF5252`）改为 `DynamicResource`/语义色。
- 自定义标题栏 `ColorZone` 的前景在深色下校对对比度。
- DataGrid 行选中/交替行、卡片背景全部走 `MaterialDesignPaper`/`MaterialDesignCardBackground`。

---

## 6. 各视图设计与线框图

> 每个视图是一个 `UserControl`（如 `Views/SpeedTestView.xaml`），由对应子 ViewModel 驱动（见 §8）。

### 6.1 测速视图

```
┌──────────────────────────────────────────────────────────────────────┐
│ 测试域名 [▼ 百度 [国内] www.baidu.com ]   协议 [▼ UDP/TCP ]            │
│ Bootstrap: 8.8.8.8 (已启用) ⓘ            [ ▶ 开始测速 ] / [ ■ 停止 ]   │
├──────────────────────────────────────────────────────────────────────┤
│ ▰▰▰▰▰▱▱▱▱  12/24   正在测试…                                           │
├──────────────────────────────────────────────────────────────────────┤
│ ┌ DNS 服务器 ─────────┬ 主/备 DNS ──────┬ 协议端点 ─┬ 延迟 ──┬ 操作 ┐  │
│ │ ★ Cloudflare DNS    │ 1.1.1.1 / 1.0…  │ DoH·DoT   │ 12 ms  │      │  │
│ │   Google DNS        │ 8.8.8.8 / 8.8…  │ DoH·DoT   │ 18 ms  │      │  │
│ │   阿里 DNS          │ 223.5.5.5       │ DoH·DoT·DoQ│ 23 ms │      │  │
│ │   我的DNS (自定义)   │ 192.168.1.1     │ —         │ 超时   │ 🗑   │  │
│ └─────────────────────┴─────────────────┴───────────┴────────┴──────┘  │
│                                   [ 设为系统 DNS → ]                     │
└──────────────────────────────────────────────────────────────────────┘
```

要点：
- **开始/停止互斥按钮**：`IsBusy` 时显示"停止"（触发取消），否则"开始测速"。替代当前"只能干等"。
- **推荐标记 ★**：最快成功项高亮（`Latency` 最小且状态=成功）。
- **协议端点列**：把 DoH/DoT/DoQ 多列合并为紧凑能力标签（点击可展开查看完整端点），为结果列腾出空间。原始的 8 列 DataGrid 过宽，精修后聚焦"名称 / 地址 / 能力 / 延迟 / 操作"。
- **延迟徽标**：带状态色（§4.3），`ToolTip` 显示 `StatusDetail`。
- **Bootstrap 指示**：只读 chip，提醒当前是否启用；配置入口在"高级"。
- **底部**：选中行后出现"设为系统 DNS →"引导到系统 DNS 视图。

### 6.2 系统 DNS 视图

```
┌──────────────────────────────────────────────────────────────────────┐
│ ⚠ 未以管理员身份运行，无法修改系统 DNS。 [ 以管理员重启 ]  (仅非管理员时) │
├──────────────────────────────────────────────────────────────────────┤
│ 网络适配器 [▼ Realtek Gaming 2.5GbE Family Controller ]  [⟳ 刷新]      │
│                                                                        │
│ 当前 DNS：   10.0.0.1, 10.0.0.2        获取方式：手动                   │
│ 待设置：     Cloudflare DNS (1.1.1.1 / 1.0.0.1)   ← 来自"测速"所选       │
│                                                                        │
│ [ ✓ 设为系统 DNS ]   [ ↺ 恢复自动获取 (DHCP) ]   [ 🩺 网络诊断 ]        │
└──────────────────────────────────────────────────────────────────────┘
```

要点：
- **管理员告警横幅**：复用 `NetworkDiagnostics.IsRunningAsAdmin()`，非管理员时置顶提示并禁用写操作按钮（避免现状里裸露的"错误代码: 5"）。
- **当前 DNS / 获取方式**：展示所选网卡的 `DnsServers` 与 `IsDhcpEnabled`。
- **待设置**：显示从"测速"带入的服务器（或在此直接下拉选择）。
- **操作反馈**：成功/失败经 VM 状态栏与 Snackbar 呈现，不再从服务层弹 `MessageBox`。

### 6.3 自定义视图

```
┌──────────────────────────────────────────────────────────────────────┐
│  自定义 DNS 服务器                                                       │
│  名称 [______]  主 DNS [______]  备用 DNS [______]                      │
│  DoH URL [________________]  DoT 主机 [________]  端口 [853]            │
│  DoQ 主机 [__________]  端口 [784]              [ ＋ 添加 ] / [ 💾 修改 ]│
│  ┌ 列表（仅自定义）─────────────────────────────────────────┐          │
│  │ 我的DNS  192.168.1.1  DoH:…  [✎ 选中进入编辑]  [🗑 删除]   │          │
│  └───────────────────────────────────────────────────────────┘          │
├──────────────────────────────────────────────────────────────────────┤
│  自定义测试域名                                                          │
│  名称 [______]  域名 [________________]        [ ＋ 添加 ] / [ 💾 修改 ]│
│  ┌ 列表（仅自定义）─────────────────────────────────────────┐          │
│  │ 我的站点  www.example.com  [✎]  [🗑]                       │          │
│  └───────────────────────────────────────────────────────────┘          │
└──────────────────────────────────────────────────────────────────────┘
```

要点：
- 保留现有"选中自定义条目 → 填充表单 → 按钮变修改"的编辑模式语义（`IsEditingDns`/`IsEditingTestDomain`）。
- **输入即校验**（§8.5）：
  - 主/备 DNS：合法 IP。
  - DoH URL：合法绝对 `https` URI（修复 `new Uri` 抛 `UriFormatException` 的隐患）。
  - 端口：1–65535。
  - 非法时输入框下方红字提示并禁用添加/修改按钮。
- 列表只放自定义条目（内置服务器在"测速"看），避免混淆。

### 6.4 高级视图

```
┌──────────────────────────────────────────────────────────────────────┐
│  Bootstrap DNS                                                          │
│  用于解析加密 DNS 上游域名的 UDP DNS 服务器                              │
│  [ 8.8.8.8____________ ]  [ 应用 ]   状态：✓ 有效                       │
├──────────────────────────────────────────────────────────────────────┤
│  诊断                                                                   │
│  [ 🛡 QUIC/DoQ 自检 ]   [ 🩺 网络适配器诊断 ]                           │
│  ┌ 输出 ─────────────────────────────────────────────────┐            │
│  │ (自检/诊断报告内联显示，可复制)                           │            │
│  └───────────────────────────────────────────────────────┘            │
├──────────────────────────────────────────────────────────────────────┤
│  外观                                                                   │
│  主题  ( ) 浅色   ( ) 深色   (•) 跟随系统                                │
├──────────────────────────────────────────────────────────────────────┤
│  关于                                                                   │
│  DNS 测速与设置工具  v{version}                                         │
└──────────────────────────────────────────────────────────────────────┘
```

要点：
- **Bootstrap** 改为"输入 + 应用（或失焦提交）"，不再逐键写文件（§8.6）。
- 诊断/自检报告**内联显示**（可复制），替代 `MessageBox` 弹窗，深色下也可读。
- 主题三选一，即时生效并持久化。

---

## 7. 组件规范

| 组件 | 规范 |
|---|---|
| **主按钮** | `MaterialDesignRaisedButton`；图标 + 文案；主操作（开始测速、设为系统 DNS） |
| **次按钮** | `MaterialDesignOutlinedButton`（恢复 DHCP、诊断、刷新） |
| **停止按钮** | `MaterialDesignRaisedButton` + Error 色；仅 `IsBusy` 时出现 |
| **图标按钮** | `MaterialDesignIconButton`（行内删除、窗口控制） |
| **延迟徽标** | 圆角 `Border` + 状态色背景/前景；`ToolTip={Binding StatusDetail}` |
| **能力标签** | DoH/DoT/DoQ 用小号 `Chip`/`Border`，无端点则显"—" |
| **导航** | `TabControl` + `MaterialDesignNavigationRailTabControl` |
| **主题切换** | 标题栏 `ToggleButton`（☀/🌙）+ 高级页三选一 `RadioButton` 组 |
| **反馈** | `Snackbar`（`MaterialDesign:Snackbar` + `SnackbarMessageQueue`）替代服务层 `MessageBox` |
| **确认框** | 删除确认用 `DialogHost`（已引入 `RootDialog`）而非 `MessageBox` |

---

## 8. 配套架构调整（轻度）

> 目标：**支撑新 UI 所必需**的最小结构改动。不触碰引擎算法与协议实现（那属深度修复）。

### 8.1 ViewModel 拆分（God VM → Shell + 4 子 VM）

当前 `MainViewModel`（712 行）承载全部逻辑。拆为：

```
ShellViewModel                      ← 导航、主题、全局 StatusMessage、共享服务
 ├─ SpeedTestViewModel              ← 域名/协议/测速/结果/取消
 ├─ SystemDnsViewModel              ← 网卡/设为系统 DNS/DHCP/管理员态
 ├─ CustomDataViewModel             ← 自定义 DNS + 测试域名的增删改与校验
 └─ AdvancedViewModel               ← Bootstrap/自检/诊断/主题/关于
```

- 共享的 `DnsServers` / `TestDomains` 集合与 `DnsTestService`/`DnsSettingService`/`DataPersistenceService` 由 `ShellViewModel` 持有并注入子 VM（构造注入即可，无需引入重型 DI；如需可用 `CommunityToolkit.Mvvm.DependencyInjection.Ioc`）。
- 视图 `UserControl` 的 `DataContext` 绑定到对应子 VM。
- **保留**现有源生成器用法（`[ObservableProperty]`/`[RelayCommand]`/`[NotifyCanExecuteChangedFor]`）。

> 若希望进一步降低风险，可先只做 **Shell + 视图拆分（XAML 搬运）**，VM 暂保持单体并以 `#region` 分区；拆 VM 作为后续小步。文档默认推荐拆分，但允许此降级路径。

### 8.2 取消与进度（修复 A‑P1 / C‑P2 的 UI 侧）

- `SpeedTestViewModel` 持有 `CancellationTokenSource`；`StartTestCommand` 创建、`StopCommand` 取消。
- 为 `DnsTestService.TestDnsServerAsync` 增加 `CancellationToken` 重载，并将其透传到各 `MeasureXxx`（`await` 处传 token）。这是"轻度架构"内的改动，不改测量算法本身。
- 通过 `IProgress<TestProgress>` 把"已完成/总数 + 单服务器结果"显式回传到 UI 线程，**移除对"碰巧回到 UI 线程"的隐式依赖**（审查 C‑P3）。

```csharp
public record TestProgress(DnsServer Server, int Done, int Total);
// VM: var progress = new Progress<TestProgress>(p => { /* 在 UI 线程更新 */ });
```

### 8.3 线程归属显式化

- 服务内网络测量统一 `ConfigureAwait(false)`（现在不敢加，因为依赖回到 UI 线程）；
- 所有对绑定集合/属性的更新经 `IProgress<T>` 或 `Dispatcher` 回到 UI 线程；
- 删除 `StartTestAsync` 结尾按 `Name+PrimaryIP` 的回查（审查 C‑P3），改为直接按引用更新。

### 8.4 转换器与资源整理

新增 `Resources/`：
- `Tokens.xaml`（间距/圆角/层级/语义色，浅深各一套）
- `Converters.xaml`：集中 `BooleanToVisibilityConverter`（内置）、`InverseBooleanConverter`、`NullToVisibilityConverter`、`StatusToBrushConverter`、`BoolToStartStopConverter`、`CapabilitiesConverter`（输出 DoH/DoT/DoQ 标签）。
- 在 `App.xaml` 合并这些字典。

### 8.5 输入校验（修复自定义端点校验缺失）

- 承载输入的 VM 改继承 `ObservableValidator`，字段加特性：`[Required]`、自定义 `[IpAddress]`、`[AbsoluteUri(Scheme="https")]`、`[Range(1,65535)]`。
- XAML 绑定开启 `ValidatesOnNotifyDataErrors=True`；MaterialDesign 的 `HintAssist` 原生显示校验错误。
- 命令 `CanExecute` 直接用 `!HasErrors`，替换现有 `try{IPAddress.Parse}catch` 式判断。

### 8.6 Bootstrap 防抖/提交

- 文本框绑定去掉 `UpdateSourceTrigger=PropertyChanged`，改 `LostFocus` 或显式"应用"按钮提交；或在 VM 内做 300ms 去抖，仅在合法且变化时 `SaveBootstrapDns`。

### 8.7 窗口最大化修复（WindowChrome）

保留自定义标题栏，但引入 `WindowChrome` 让系统管理最大化到工作区（不再遮挡任务栏）：

```xml
<WindowChrome.WindowChrome>
    <WindowChrome CaptionHeight="48" ResizeBorderThickness="6"
                  GlassFrameThickness="0" CornerRadius="0" UseAeroCaptionButtons="False"/>
</WindowChrome.WindowChrome>
```

- 标题栏自定义按钮加 `WindowChrome.IsHitTestVisibleInChrome="True"`。
- 可移除 `MainWindow.xaml.cs` 中手写的 `DragMove`/最大化图标切换（由 WindowChrome 接管），或保留并与之协调。

### 8.8 服务层去 UI 依赖

- `DnsSettingService` / `NetworkDiagnostics` 不再直接 `MessageBox.Show`，改为返回结果/抛出，由 VM 用 `Snackbar`/`DialogHost` 呈现（符合项目 MVVM 规范）。

---

## 9. 落地步骤（分阶段）

> 每个阶段结束都应能 `dotnet build` 通过并运行；建议每阶段一个提交。

| 阶段 | 内容 | 风险 | 关键文件 |
|---|---|---|---|
| **P0 基础** | 建 `Resources/Tokens.xaml`、`Converters.xaml`；WindowChrome 修复最大化；硬编码色改语义色 | 低 | `App.xaml`、`MainWindow.xaml(.cs)` |
| **P1 导航骨架** | 建 `ShellView`/`ShellViewModel` + `NavigationRailTabControl`；把现有面板**原样搬入** 4 个 `Views/*.xaml`（逻辑暂不动） | 低 | 新增 `Views/`、`ViewModels/` |
| **P2 VM 拆分** | 拆 `SpeedTest/SystemDns/CustomData/Advanced` 子 VM，接线 `DataContext`（可降级：暂留单 VM 分区） | 中 | `ViewModels/*` |
| **P3 取消/进度** | 加 `StopCommand` + `CancellationToken` 透传 + `IProgress` 回传；显式线程归属 | 中 | `SpeedTestViewModel`、`DnsTestService`（新增 token 重载） |
| **P4 校验/防抖** | `ObservableValidator` + 校验特性；Bootstrap 失焦/防抖提交 | 低 | `CustomDataViewModel`、`AdvancedViewModel` |
| **P5 主题** | `ThemeService` + 浅/深/系统切换 + `SystemEvents` 跟随 + 持久化 `app_settings.json` | 中 | 新增 `Services/ThemeService.cs`、扩展 `DataPersistenceService` |
| **P6 结果精修** | 推荐标记、延迟徽标、能力标签、`StatusToBrushConverter`、删死触发器；深色 QA | 低 | `SpeedTestView.xaml` |

---

## 10. 验证方式

**构建 / 运行**
```bash
dotnet build DNSSpeedTester.csproj          # 应无错误/警告回归
dotnet run                                  # 启动应用
```

**手动验收清单**
- [ ] 四个导航项切换正常，内容区正确渲染。
- [ ] 主题切换：浅 / 深 / 跟随系统即时生效；改系统主题时"跟随系统"自动响应；重启后保持上次选择。
- [ ] 最大化窗口**不遮挡任务栏**，还原正常；拖动标题栏、双击最大化正常。
- [ ] 开始测速后点"停止"能**迅速中止**（秒级），按钮状态与进度条正确复位。
- [ ] 自定义 DNS：输入非法 IP / 非 https 的 DoH URL / 越界端口时，**输入框即时报错且添加/修改按钮禁用**；合法后可添加；编辑模式填充/保存正常；重启后持久化保留。
- [ ] Bootstrap：逐键输入**不频繁写盘**；失焦/应用后合法值才保存并提示状态。
- [ ] 系统 DNS：非管理员显示告警且写操作禁用；管理员下设为系统 DNS、恢复 DHCP 均有 Snackbar 反馈。
- [ ] 深色模式下所有文字/图标/表格对比度可读，无硬编码亮色残留。

**回归重点**：自定义数据持久化（增/删/改 × 重启）、WMI 读写网卡、QUIC 自检与网络诊断报告显示。

---

## 11. 范围与非目标

### ✅ 本次覆盖（视觉 + 轻度架构）
导航式重构、Material 3 精修、浅/深/系统主题、测速取消/停止、输入校验、Bootstrap 防抖、最大化修复、服务层去 `MessageBox`、转换器/状态色集中化、VM 拆分（或分区降级）、结果视图精修。

### ⏭ 非目标（建议后续"深度修复"单独跟进，对应随附审查）
| 项 | 审查 ID | 说明 |
|---|---|---|
| DoQ 事务 ID 必须为 0（RFC 9250） | A‑P2 | 需改 `BuildDnsQuery`/DoQ 调用 |
| UDP/TCP 路径总超时与口径统一（Ping×1.2、"中位数"） | A‑P1/A‑P3 | 本次仅加"取消"，未重构测量算法 |
| Bootstrap 异步接收超时失效（`ReceiveTimeout` 对 `ReceiveAsync` 无效） | A‑P2 | 改用 `CancelAfter`/`CancellationToken` |
| Bootstrap 响应未校验事务 ID | A‑P3 | 解析前比对 ID |
| 加载自定义服务器单条坏记录清空整表 | B‑P2 | `LoadCustomDnsServers` 逐条 try/catch |
| 加密/传统延迟口径不可比 | A‑P3 | 需统一测量含义或分列展示 |

> 说明：取消能力需要为 `DnsTestService.TestDnsServerAsync` 增加 `CancellationToken` 透传，这属轻度架构、已纳入 P3；但**测量算法本身与协议正确性不在本次范围**。

---

*本文档为 UI 重构的设计蓝图，不含最终实现代码；经确认后可按 §9 分阶段落地。随附的《代码审查结论》提供了问题清单与审查 ID 交叉引用。*
