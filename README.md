# AITokenUsageWidget for Windows

Windows 11 小组件 + 主配置应用：实时显示 **DeepSeek**、**Kimi Code**、**GLM Coding** 三家 AI 供应商的账户余额与 API 额度用量。与 macOS 原版（[AITokenUsageWidget](https://github.com/chenxing890/AITokenUsageWidget)）功能逐项对等。

需求与设计文档：[docs/REQUIREMENTS.md](docs/REQUIREMENTS.md)

## 功能 / Features

- 三家供应商用量监控：DeepSeek 余额；Kimi 5 小时 / 7 天 / 月度总额；GLM 5 小时 / 7 天 / 月度工具 / 30 天累计（中文大数格式化）
- Windows 小组件（中 / 大尺寸）：3 个供应商自动紧凑三列；进度条 + 重置倒计时；手动刷新按钮；失败回退缓存并标注时间
- 主配置应用（WinUI 3）：NavigationView 布局、API Key 自动清洗与 DPAPI 加密落盘、测试连接、打开控制台、内嵌实时预览（模拟 / 实际两种形态）
- 用量告警 Toast：阈值可配（50–95%，默认 80%）、同「供应商-窗口」防重复、回落 10% 重置、点击跳转对应供应商页
- 三档主题（跟随系统 / 浅色 / 深色）：主窗口即时生效，小组件下次刷新应用显式配色
- 配置可靠性三层防护：逐字段容错解码 → corrupt 文件保留 → 写前 `.bak` 备份 + 临时文件原子替换；命名 Mutex 串行化

## 构建与运行 / Build

前置：Windows 11 22H2+、Visual Studio 2022（含 WinUI / .NET 8 工作负载）或 Windows 上的 .NET 8 SDK。

```powershell
# 共享层单元测试（可在任意平台运行，49 个用例）
dotnet test tests/Shared.Tests/AITokenUsageWidget.Shared.Tests.csproj

# 打包主应用（生成 MSIX）
dotnet build src/App/AITokenUsageWidget.App.csproj -c Release -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true
# 产物：src/App/AppPackages/ 下含 .msix；右键安装（开发期需开启开发者模式或信任测试证书）
```

Visual Studio：打开 `AITokenUsageWidget.sln`，将 `AITokenUsageWidget.App` 设为启动项目，F5 运行。

固定小组件：`Win + W` 打开小组件面板 → 添加小组件 → 选择「AI 模型用量」（Windows 不允许应用自动固定）。

## 结构 / Layout

```
src/Shared   共享层（net8.0，跨平台可测）：模型、容错存储、三家接口解析、告警引擎、卡片构建
src/App      WinUI 3 主应用 + Widget Provider（单 exe 双模式：UI / COM 无头 + STA 消息泵）
tests/       xUnit：golden 样本解析、配置可靠性、告警引擎、卡片 JSON
```

关键设计：

- **单 exe 双模式**：正常启动为 UI；Widgets Board 以 `-RegisterProcessAsComServer` 拉起时进入无头 Provider 模式（`Program.cs` → `Widgets/WidgetHost.cs`），对齐微软官方 Widget Provider 模式。
- **共享文件**：`%LOCALAPPDATA%\AITokenUsageWidget\shared.json`（配置 + 缓存 + 主题 + 告警记录），主 App 与 Provider 经命名 Mutex 串行读写；API Key 以 DPAPI 密文落盘（`dpapi:` 前缀，旧明文自动兼容）。
- **刷新调度**：Provider 15 分钟定时 + 激活即刷 + 主 App 保存后命名事件通知立即刷新 + 卡片按钮手动刷新。
- **主题兜底**：Adaptive Cards 不支持任意十六进制前景色，采用「文字色枚举 + 包内纯色背景图」实现浅色/深色强制主题；跟随系统时交给 Widgets Board 按系统主题渲染，规避「白字落白底」。

## 平台差异与已知限制

- 后台刷新为 Provider 进程内 15 分钟定时（Widgets Board 存活期间有效），未注册系统 BackgroundTask；面板不可见时不推屏但数据/告警流程不变。
- Kimi 月度总额依赖网页 `kimi-auth` Cookie（非官方接口），失败静默降级。
- `Assets/*.png` 为脚本生成的占位图标，发布前可替换为正式品牌图标。
- Windows 10 降级形态（§5.5）暂未实现，当前版本面向 Windows 11。

## 验证 / Verification

| 项 | 方式 |
|---|---|
| 三家解析（含边界） | `UsageServiceTests` golden 样本（字符串数字、used 缺失回退、GLM 业务失败、缺 unit 排序、月度/累计静默失败） |
| 配置可靠性 | `ConfigStoreTests`（损坏保留、部分字段容错、.bak、默认补齐、密文落盘、旧明文兼容） |
| 告警引擎 | `AlertEngineTests`（等于阈值、防重复、回落边界 69.9/70/72、阈值重评估、余额型不参与） |
| 卡片 JSON | `WidgetCardTests`（合法 Adaptive Card、dense 三列、缓存标注、明暗主题、进度条） |

隐私：无遥测、无统计；仅出站 HTTPS 直连三家官方域名；日志与代码均不打印 Key。
