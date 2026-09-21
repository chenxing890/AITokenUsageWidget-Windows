# AITokenUsageWidget for Windows — 需求与设计文档

> 版本：v1.0（设计稿）
> 对应 macOS 原版：AITokenUsageWidget v1.0（github.com/chenxing890/AITokenUsageWidget）
> 目标：在 Windows 平台实现与 macOS 版**完全相同的功能**，并符合 Windows 界面规范与用户交互习惯。
> 状态：本文档仅包含需求与设计，不含代码实现。

---

## 1. 项目概述

### 1.1 背景

macOS 版 AITokenUsageWidget 是一款通知中心小组件，实时显示 DeepSeek、Kimi Code（Moonshot）、GLM Coding（智谱）三家 AI 供应商的账户余额与 API 额度用量。本项目将其完整移植到 Windows 平台，以 **Windows 小组件（Windows Widgets）+ 主配置 App** 的形态交付。

### 1.2 产品定位

- 名称：**AITokenUsageWidget for Windows**（小组件显示名「AI 模型用量」）
- 平台：Windows 11（必需）；Windows 10 22H2+ 提供降级形态（见 §5.5）
- 形态：主配置 App（WinUI 3 桌面应用）+ Windows 小组件面板中的 Adaptive Card 小组件
- 语言：首发简体中文，资源结构预留多语言（zh-CN / en-US）

### 1.3 功能对等原则

Windows 版必须与 macOS v1.0 功能一一对应。对照总表：

| 功能域 | macOS 实现 | Windows 实现 | 对等要求 |
|---|---|---|---|
| 用量监控 | WidgetKit 小组件，15 分钟刷新 | Widget Provider + 后台定时刷新 | 数据内容、刷新策略一致 |
| 主配置界面 | SwiftUI 设置窗口 | WinUI 3 设置窗口（NavigationView） | 配置项、预览、自动保存一致 |
| 主题 | 跟随系统 / 浅色 / 深色，App 与 Widget 联动 | 同左 | 三档主题，两端联动 |
| 用量告警 | UNUserNotificationCenter | Windows Toast 通知（AppNotification） | 阈值、防重复、回落重置逻辑一致 |
| 数据共享 | App Group / 共享 JSON | 共享 JSON 文件（%LOCALAPPDATA%） | 容错解码、备份、corrupt 保留机制一致 |
| 供应商接口 | DeepSeek / Kimi / GLM 三家官方接口 | 同一批 HTTPS 接口 | 请求、解析、错误处理一致 |

---

## 2. 功能性需求（FR）

### FR-1 供应商用量监控

支持三家供应商，数据与 macOS 版完全一致：

| 供应商 | 显示内容 | 接口 | 鉴权 |
|---|---|---|---|
| **DeepSeek** | 账户余额：总余额 / 赠送 / 充值 + 可用状态指示 | GET https://api.deepseek.com/user/balance | Authorization: Bearer {apiKey} |
| **Kimi Code** | 5 小时窗口 / 7 天窗口（百分比 + 重置倒计时）；月度总额（可选，需 kimi-auth Cookie） | GET https://api.kimi.com/coding/v1/usages；月度：网页 billing 接口 POST https://www.kimi.com/apiv2/kimi.gateway.billing.v1.BillingService/GetUsages（body 为 {}） | Bearer Key；月度用 Cookie |
| **GLM Coding（智谱）** | 5 小时 / 7 天（百分比，unit 3/6 判定；缺 unit 按重置时间排序）；月度工具（TIME_LIMIT，次数绝对值）；30 天累计（token 数 + 调用次数，中文大数格式化） | GET https://open.bigmodel.cn/api/monitor/usage/quota/limit；GET .../api/monitor/usage/model-usage?startTime&endTime（近 30 天） | Authorization: {apiKey}（无 Bearer 前缀） |

解析规则（与 macOS 版一致，实现时逐条对照）：

- **Kimi**：limits[] 中 window.duration == 300（分钟）→ 5 小时窗口；顶层 usage → 7 天窗口；窗口结构 {limit, used, remaining, resetTime}，数字以字符串返回；used 缺失时用 limit - remaining 计算；月度接口失败静默忽略，不影响前两个窗口。
- **GLM**：HTTP 200 也可能业务失败，必须检查 success == false / error / code != 200 并透出 msg；CREDIT_LIMIT（新套餐）返回绝对值 currentValue/usage，TOKENS_LIMIT（老套餐）仅百分比；30 天累计失败静默忽略；大数格式化 55237346 → "5524万"，≥1 亿显示「x.x亿」。
- **DeepSeek**：balance_infos[0] 取 total_balance / granted_balance / topped_up_balance / currency，is_available 取顶层；空响应视为错误。
- **通用**：查询接口不消耗模型 token；Key 未填返回 missingKey 状态；网络/解析失败返回中文友好错误提示（401→"API Key 无效或已过期"、超时→"请求超时，请检查网络"等）。

### FR-2 小组件

- **尺寸**：支持中（Medium）、大（Large）两种；Windows Widgets 无 macOS 的 small，不提供。
- **内容**：仅显示已启用的供应商卡片。
  - 中尺寸：1–2 个供应商为宽松双列；3 个供应商自动切换**紧凑三列**（dense 模式：单行短标题「5h」「7d」「月度」、小字号、紧凑间距）。
  - 大尺寸：纵向列表，每个供应商一块，含进度条与重置倒计时。
- **卡片内容**：
  - 余额型（DeepSeek）：货币符号（CNY→¥，USD→$）+ 总余额大字 + 赠送/充值明细 + 可用状态点。
  - 窗口型（Kimi/GLM）：每窗口显示标题、已用百分比、进度条（品牌色）、重置倒计时（如「2 小时后重置」）；GLM 月度工具/30 天累计显示绝对值文本。
  - **7 天窗口健康配额线**：7 天额度按时间均摊（每天 1/7 ≈ 14.28%），进度条上叠加一条随时间连续推进的半透明灰刻度线（位置 = 窗口已过时间比例：重置时 0%，第 1 天末 14.28%、第 2 天末 28.57% … 第 6 天末 85.71%）。用量超过该线即超前消耗、挤占后续份额。线与进度条同高内嵌（不凸出、不改变原进度条高度）、约 2px、圆头；颜色为半透明灰（约 75% 不透明），浅/深主题下均柔和可见。由重置时间倒推窗口起点（起点 = 重置时间 − 7 天）动态计算，Widget 每次刷新时更新。文本兜底渲染时以「▎」刻度字符标记。
  - 错误态：卡片显示错误图标 + 中文提示；未配置 Key：引导文案。
- **刷新**：每 15 分钟自动刷新（后台任务）；刷新失败时显示上次缓存数据并标注缓存时间；支持点击小组件上「刷新」按钮手动刷新。
- **主题**：小组件背景与文字颜色随应用主题设置（跟随系统/浅色/深色）。Windows Widgets 面板的背景由系统托管，主题化通过 Adaptive Card 的显式前景/背景色实现。

### FR-3 主配置界面（设置窗口）

- **布局**：WinUI 3 NavigationView 左侧导航（遵循 Fluent Design）：
  - **概览 → 仪表盘**（仅当至少一个供应商启用时显示；全部关闭时自动隐藏）：聚合全部已启用供应商的用量卡片，每个供应商一行（大尺寸三行布局，与小组件 large 一致）。已配置 Key 的卡片显示实际用量（进入自动拉取 + 顶部「刷新」按钮），未配置的显示模拟数据 + 右上角橙色「模拟」角标；底部标注更新时间。关掉某供应商开关，该卡片立即从仪表盘消失
  - 供应商分组：DeepSeek / Kimi Code / GLM Coding（各含图标、名称、副标题、启用状态徽标）
  - 外观：主题选择（跟随系统 / 浅色 / 深色，三段式 Segmented 控件）
  - 通知：告警总开关 + 阈值下拉选择（50% / 60% / 70% / 80% / 90% / 95%，默认 80%）
  - 底部：版本号、数据目录快捷入口
- **供应商详情页**：
  - 「启用」开关（ToggleSwitch）
  - API Key 输入框（PasswordBox 带显示/隐藏切换），格式提示（如 sk-xxx），自动清洗（去首尾空白、剥离误粘贴的 "Bearer " 前缀）
  - 自定义显示名、自定义接口根地址（留空用默认）
  - Kimi 额外提供 kimi-auth Cookie 输入项（可选，注明用于月度总额，非官方接口）
  - 「测试连接」按钮：即时调用接口，显示成功（实际数据）或中文错误
  - 「打开控制台」按钮：跳转供应商官网 Key 管理页
  - **内嵌预览**：实时渲染小组件卡片预览——
    - 未配置 Key：显示模拟数据（内置占位数据），卡片右上角橙色「模拟」胶囊标记，底部文案「模拟数据，配置 API Key 后显示实际用量」
    - 已配置：进入页面自动拉取一次实际用量，底部文案「实际用量 · 更新于 HH:MM」
- **自动保存**：所有配置修改即时持久化（无「保存」按钮），保存后通知小组件刷新。
- **主题联动**：主界面切换主题立即生效；小组件在下一次刷新（或收到配置变更通知）后应用。
- **窗口**：默认 860×640，最小 720×520；支持最大化；标题栏遵循 Windows 11 Mica 材质。

### FR-4 用量告警通知

- 任一额度窗口（含 Kimi 月度、GLM 月度工具）使用百分比 ≥ 阈值时，发送 **Windows Toast 系统通知**：
  - 标题：「{供应商显示名} 用量告警」
  - 正文：「{窗口名}已使用 {N}%，达到告警阈值 {M}%。」
  - 默认提示音；点击通知打开主 App 对应供应商页面。
- **防重复**：同一「供应商-窗口」（key = {kind}-{windowTitle}）在越过阈值期间只通知一次。
- **回落重置**：该窗口回落到「阈值 − 10%」以下后清除已通知标记，下次冲高可再次提醒（覆盖额度周期重置场景）。
- **开关与阈值**：主界面可全局开关（默认开）并下拉选择阈值（默认 80%）；修改阈值后清空已通知记录并立即重评估。
- 余额型供应商（DeepSeek）不参与百分比告警。
- 首次启动 App 时注册通知渠道。

### FR-5 配置存储与共享

- **存储位置**：%LOCALAPPDATA%\AITokenUsageWidget\shared.json（主 App 与小组件 Provider 进程读写同一文件）。
- **文件格式**（JSON，与 macOS 版结构对齐）：

```json
{
  "configs": [
    {
      "id": "uuid",
      "kind": "deepseek | kimi | glm",
      "isEnabled": false,
      "apiKey": "",
      "baseURLOverride": "",
      "customName": "",
      "extraToken": ""
    }
  ],
  "cachedUsages": [],
  "theme": "system | light | dark",
  "alertedKeys": [],
  "alertsEnabled": true,
  "alertThreshold": 80
}
```

- **可靠性（必须复刻 macOS 版三层防护，避免配置丢失事故重演）**：
  1. **逐字段容错解码**：自定义反序列化，每个字段独立容错；任何单字段缺失或结构变化不得导致整个文件解码失败、更不得把空负载当作「无配置」覆盖写回。
  2. **corrupt 文件保留**：整体解码失败时，原文件改名 shared.corrupt-yyyyMMdd-HHmmss.json 保留，返回空负载。
  3. **写前备份**：每次写入前把现有文件复制为 shared.json.bak。
  4. 写入采用「临时文件 + 原子替换」（先写 shared.json.tmp 再替换），防断电写坏。
- **并发**：主 App 与小组件后台任务可能同时读写，使用命名 Mutex 串行化；读取失败（锁冲突）时短暂重试（最多 3 次，间隔 100ms）。
- **敏感信息保护**：API Key / Cookie 在磁盘上使用 **DPAPI**（ProtectedData，CurrentUser 范围）加密存储，JSON 中仅保存密文 Base64；内存中即用即清。
- **默认配置**：首次运行生成三家供应商的默认配置（isEnabled=false）；版本升级新增供应商时自动补齐，不覆盖已有配置。

### FR-6 用量缓存

- 每次成功抓取后写入 cachedUsages；小组件快照/刷新失败时回退显示缓存并标注时间；无任何缓存时显示占位数据（预览场景）。

---

## 3. 界面设计规范（UI）

### 3.1 设计体系

- 遵循 **Fluent Design System**（Windows 11）：Mica 窗口背景、圆角（控件 4px / 卡片 8px）、标准控件（NavigationView、ToggleSwitch、ComboBox、InfoBadge、ProgressBar、InfoBar、TeachingTip）。
- 字体：Segoe UI Variable Text；正文 14px，卡片数值 20–28px semibold，紧凑模式最小 12px。
- 图标：Segoe Fluent Icons（替代 macOS SF Symbols）：DeepSeek 水滴（Drop）、Kimi 月星（Moon/Stars）、GLM 闪光（Sparkle）。
- 品牌色（与 macOS 版一致）：
  - DeepSeek #3B82F5（RGB 0.23/0.51/0.96）
  - Kimi #8C5CF5（RGB 0.55/0.36/0.96）
  - GLM #059E87（RGB 0.02/0.62/0.53）

### 3.2 主题

- 三档：跟随系统（默认）/ 浅色 / 深色；监听系统主题变化动态切换。
- 主 App：WinUI RequestedTheme 切换。
- 小组件：Adaptive Card 元素显式配色——
  - 浅色：背景近白（#F7F7F7 等效），主文字深灰
  - 深色：背景深灰（#212121 等效，对应 macOS 的 Color(white: 0.13)），主文字近白
  - 前景文字、进度条轨道色均需随主题成对定义，禁止出现「白字落白底」（macOS 版曾踩此坑）。
- 高对比度模式（High Contrast）：尊重系统高对比主题，控件使用 ThemeResource 而非硬编码色。

### 3.3 主窗口布局

```
┌──────────────────────────────────────────────────────────┐
│ 标题栏（Mica）                                            │
├──────────────┬───────────────────────────────────────────┤
│ Navigation   │  供应商详情 / 外观 / 通知                   │
│  ▸ 供应商     │  ┌─ 启用 ───────────────[Toggle] ─┐        │
│    DeepSeek  │  │ API Key  [***********] [👁][测试] │        │
│    Kimi Code │  │ 显示名 [      ] 接口地址 [      ]  │        │
│    GLM       │  │ [打开控制台]                     │        │
│  ──────────  │  └────────────────────────────────┘        │
│  ▸ 外观      │  ┌─ 小组件预览 ───────────────[模拟]─┐      │
│    主题选择   │  │   （实时渲染的卡片预览）            │      │
│  ▸ 通知      │  │   模拟数据，配置后显示实际用量       │      │
│    开关/阈值  │  └────────────────────────────────┘        │
├──────────────┴───────────────────────────────────────────┤
│  v1.0.0                          数据目录  检查更新        │
└──────────────────────────────────────────────────────────┘
```

- 导航选中态：左侧指示条 + 浅强调色底（Fluent 标准）。
- 所有输入控件带标签与占位提示；错误用 InfoBar（红）内联展示，不用弹窗打断。

### 3.4 小组件布局（Adaptive Card）

- **中尺寸（3 供应商紧凑三列）**：三列等宽 ColumnSet，每列：品牌图标 + 名称（截断）、最大窗口百分比大字、短标题行（5h / 7d / 月度）、细进度条。
- **大尺寸**：每供应商一块：标题行（图标 + 名称 + 状态）、余额或窗口列表（标题 + 百分比 + 进度条 + 重置倒计时）。
- 顶部工具区：标题「AI 模型用量」+ 手动刷新按钮 + 最后更新时间（「x 分钟前」）。
- 卡片间距 8px、内边距 12px；角标「模拟」用橙色（#F97316）胶囊。

---

## 4. 交互流程（UX Flows）

### 4.1 首次启动

1. 启动主 App → 生成默认配置（三家供应商均停用）→ 注册通知渠道 → 注册后台刷新任务。
2. 显示欢迎引导（首启页/TeachingTip）：① 获取 API Key 链接 ② 配置步骤 ③ 如何固定小组件（Win+W 打开小组件面板 → 添加小组件 → 选择「AI 模型用量」；Windows 不允许应用自动固定，需用户手动一次）。
3. 未配置任何供应商时小组件显示引导态（「打开应用配置 API Key」按钮，点击拉起主 App）。

### 4.2 配置供应商

1. 左侧导航选择供应商 → 打开「启用」→ 粘贴 API Key（自动清洗、自动保存、DPAPI 加密落盘）。
2. 点「测试连接」→ 按钮进入加载态 → 成功显示绿色 InfoBar + 预览刷新为实际用量；失败显示红色 InfoBar + 中文错误。
3. 保存后立即触发小组件刷新（命名事件通知 Provider，或待其下一次读取共享文件）。

### 4.3 日常查看

- 用户按 Win+W 打开小组件面板 → 查看卡片 → 每 15 分钟自动更新；点刷新按钮立即更新。
- 告警触发：Toast 弹出 → 点击进入主 App 对应供应商页；同一窗口冲高不重复打扰，回落 10% 后重置。

### 4.4 主题切换

- 主 App「外观」→ 选择主题 → 主窗口即时切换 → 写入共享配置 → 小组件下次刷新应用显式配色。

### 4.5 错误与恢复

- 网络失败：小组件显示缓存 + 「更新失败，显示缓存数据（HH:MM）」；后台静默重试，不打扰用户。
- 配置文件损坏：保留 corrupt 文件，应用从空配置启动并提示用户（InfoBar：「配置文件已损坏并备份至 …，请重新配置」）。
- Key 失效（401）：卡片与主 App 预览均显示「API Key 无效」。

---

## 5. 小组件实现方案设计

### 5.1 技术选型

| 项目 | 方案 | 理由 |
|---|---|---|
| 应用框架 | **WinUI 3（Windows App SDK 1.6+）+ C# / .NET 8** | Fluent 原生、Widgets Board 官方支持 |
| 打包分发 | **MSIX** | Widget Provider 注册必须有包身份 |
| 小组件 | **Windows Widgets Provider（IWidgetProvider）+ Adaptive Cards（JSON 模板 + 数据绑定）** | 系统小组件面板唯一官方接入方式 |
| 后台刷新 | Widget 内 UpdateWidget 自调度 + **TimeTrigger 后台任务（15 分钟）** | 对齐 macOS 15 分钟刷新；Windows 后台任务最短间隔即 15 分钟，天然契合 |
| 通知 | **AppNotification（Windows App SDK）/ ToastNotificationManager** | 打包应用标准通知通道 |
| HTTP | HttpClient（单例、连接池、10s 超时） | 对齐 macOS APIClient 行为 |
| JSON | System.Text.Json（自定义容错反序列化） | 复刻逐字段容错 |
| 加密 | DPAPI（ProtectedData） | Key 落盘加密 |

### 5.2 进程与组件架构

```
┌─────────────────────────────┐        ┌──────────────────────────────┐
│  主 App（WinUI 3, 桌面进程） │        │  Widget Provider（后台进程）   │
│  - 设置窗口 / 预览           │        │  - IWidgetProvider 实现        │
│  - 测试连接 / 手动刷新        │        │  - Adaptive Card 模板渲染      │
│  - 注册后台任务 / 通知渠道    │        │  - 15min TimeTrigger 刷新      │
│                             │        │  - Toast 告警发送              │
└──────────┬──────────────────┘        └──────────────┬───────────────┘
           │            共享文件（Mutex 串行化）        │
           ▼                                           ▼
        %LOCALAPPDATA%\AITokenUsageWidget\shared.json（+ .bak / corrupt）
```

- **共享层（类库）**：Shared 项目包含 Models（ProviderKind / ProviderConfig / ProviderUsage / UsageWindow / ThemePreference）、ConfigStore（容错读写 + 备份 + DPAPI）、UsageService（三家抓取解析）、AlertEngine（阈值判定 + 防重复 + 回落重置）。主 App 与 Provider 均引用，保证行为一致。
- **Widget Provider**：实现 IWidgetProvider（CreateWidget / DeleteWidget / OnActionInvoked / GetWidgetTemplate / GetWidgetData）；模板 JSON 按尺寸（Medium/Large）与供应商数量（宽松/dense）生成；数据 JSON 从 UsageService 抓取或缓存填充。
- **刷新调度**：
  - Provider 激活时立即刷新一次；
  - 注册 TimeTrigger(15min) 后台任务：抓取 → 写缓存 → WidgetManager.UpdateWidget() → 告警评估；
  - 主 App 保存配置后通过命名 EventWaitHandle 通知立即刷新。
- **手动刷新**：Adaptive Card Action.Execute（verb=refresh）→ Provider OnActionInvoked 内抓取并 UpdateWidget。

### 5.3 数据流

```
ProviderConfig(共享JSON) ──► UsageService.fetchAll(并发抓取三家)
        │                                   │
        ▼                                   ▼
  主题/告警设置 ◄── ConfigStore ◄── ProviderUsage[]（写缓存）
                                        │
                          ┌─────────────┼──────────────┐
                          ▼             ▼              ▼
                    Widget 数据绑定   AlertEngine    主 App 预览
                    （卡片渲染）     （Toast 告警）   （实时卡片）
```

### 5.4 Adaptive Card 模板设计要点

- 模板与数据分离：body 结构通过 $data 绑定数据节点。
- 每供应商卡片对应一个数据节点（kind、displayName、状态、余额字段、windows 数组、isSimulated、fetchedAtText）。
- dense 模式：模板层根据 usages.Count >= 3 选择紧凑模板（单行短标题、隐藏倒计时）。
- 主题：模板生成时注入主题色板（前景/背景/轨道色），不依赖系统托管色。
- 本地化：模板文案全部来自资源（.resw），不在模板内硬编码中文。

### 5.5 Windows 10 与限制说明

- Windows Widgets Board 为 Windows 11 特性。**Windows 10 22H2 降级形态**：主 App + 可选「桌面悬浮窗小部件」（置顶、可拖动、无边框小窗，模拟小组件体验），通知与刷新逻辑不变；安装包按 OS 版本条件启用功能。
- Windows 小组件无法自定义任意背景材质（面板统一托管），深色/浅色通过卡片显式配色实现——这是与 macOS containerBackground 方案的平台差异，已在配色规范中兜底。

---

## 6. 非功能性需求（NFR）与性能指标

### 6.1 性能

| 指标 | 目标值 |
|---|---|
| 主 App 冷启动 | ≤ 2s（至首帧可交互） |
| 主 App 内存占用 | ≤ 150 MB（常规使用） |
| Widget Provider 后台进程内存 | ≤ 60 MB |
| 单次三家并发抓取总耗时 | ≤ 8s（单接口超时 10s，并发执行） |
| 小组件刷新（抓取→渲染→上屏） | ≤ 10s，失败时 0.5s 内回退缓存 |
| 主界面预览拉取 | ≤ 8s，期间显示加载态 |
| 配置文件读写 | 单次 ≤ 50ms；加锁等待 ≤ 300ms |
| CPU | 空闲时主 App 与 Provider 均 ≈ 0%（无轮询，全部事件/定时驱动） |
| 网络流量 | 每 15 分钟 ≈ 4 次 HTTPS 请求（DeepSeek 1 + Kimi 1~2 + GLM 2），单次 ≤ 10KB |

### 6.2 可靠性

- 配置零丢失：三层防护（容错解码 / corrupt 保留 / .bak 备份）+ 原子写入，100% 覆盖 macOS 版事故场景（新增字段、部分写坏、并发覆盖）。
- 抓取失败不丢旧数据：缓存回退；连续失败不清空缓存。
- 告警不重复、不漏报：阈值判定与回落重置逻辑需有单元测试覆盖（含恰好等于阈值、跨越阈值、回落边界、阈值修改后重评估等用例）。

### 6.3 安全与隐私

- API Key / Cookie：DPAPI 加密落盘；仅通过 HTTPS 发往对应供应商官方域名；日志必须脱敏，禁止打印 Key。
- 无遥测、无第三方统计、无崩溃上报 SDK（与 macOS 版一致）。
- 网络仅出站 HTTPS；不监听任何端口。
- 安装包签名：发布版使用有效代码签名证书（避免 SmartScreen 拦截警告；开发期可用自签测试证书）。

### 6.4 兼容性

- Windows 11 22H2+（小组件全功能）；Windows 10 22H2（降级形态，见 §5.5）。
- x64 必需；ARM64 提供原生构建。
- DPI：适配 100%–200% 缩放，矢量图标（Segoe Fluent Icons / SVG），无位图模糊。
- 语言：zh-CN 首发，en-US 资源完整；日期/倒计时本地化格式。

### 6.5 可维护性与扩展

- 新增供应商三步（与 macOS 版对齐）：① ProviderKind 增加枚举与元数据 ② UsageService 增加抓取解析分支 ③ UI/存储/卡片自动适配。
- 日志：本地滚动日志文件（%LOCALAPPDATA%\AITokenUsageWidget\logs\，保留 7 天，脱敏），主界面提供「打开日志目录」。
- 测试：共享层单元测试（解析、容错解码、告警引擎）覆盖率 ≥ 80%；三家解析用真实响应样本（脱敏）做 golden test。

---

## 7. 交付物与验收标准

### 7.1 交付物（开发阶段，本文档之外）

1. MSIX 安装包（x64 + ARM64）与安装说明
2. 源码仓库（含 README 中英双语、截图）
3. 单元测试与 golden test 样本

### 7.2 验收标准（对照 macOS v1.0 逐项验收）

| # | 验收项 | 标准 |
|---|---|---|
| 1 | 三家供应商配置与抓取 | 与 macOS 版同一账号下数据一致（余额/百分比/倒计时/30 天累计） |
| 2 | 小组件中/大尺寸 | 3 供应商中尺寸自动紧凑三列；大尺寸含进度条与倒计时 |
| 3 | 主题 | 三档切换，主 App 即时生效，小组件同步；深浅色下无文字不可见问题 |
| 4 | 告警 | 达到阈值弹 Toast、同窗口不重复、回落 10% 重置、阈值可下拉选择、开关生效 |
| 5 | 预览 | 未配置显示模拟数据 + 橙色「模拟」标记；已配置自动显示实际用量 |
| 6 | 配置可靠性 | 构造损坏 JSON / 旧版 JSON / 并发写入，配置均不丢失，corrupt/bak 文件正确生成 |
| 7 | 刷新 | 15 分钟自动刷新；失败回退缓存并标注时间；手动刷新可用 |
| 8 | 错误提示 | 401/超时/空响应均为中文友好提示 |
| 9 | 性能 | 满足 §6.1 全部指标 |
| 10 | 隐私 | Key 加密落盘，日志无 Key，无遥测 |

---

## 8. 风险与开放问题

| 风险 | 说明 | 应对 |
|---|---|---|
| Widget Provider 需 MSIX 身份 | 未打包的 exe 无法注册小组件 | 开发期用打包项目调试；分发走 MSIX（含自签证书安装说明） |
| 后台任务 15 分钟为系统最短间隔 | 无法更高频刷新 | 与 macOS 版对齐，可接受；提供手动刷新 |
| Kimi 月度接口非官方 | 依赖网页 Cookie，可能失效 | 接口失败静默降级（不显示月度窗口），文档注明 |
| SmartScreen 拦截未知发布者 | 无 EV 证书时首次安装有警告 | 说明文档引导；预算允许时购买代码签名证书 |
| Windows 10 无 Widgets Board | 功能形态差异 | §5.5 降级形态，需求文档明确告知用户 |
| 微软对 Widgets Board 长期投入存在不确定性 | 平台 API 可能调整 | 抽象 Provider 层，保留迁移到「系统托盘 + 悬浮窗」形态的能力 |

---

## 附录 A：占位（模拟）数据规范

与 macOS 版一致，预览与小组件空态使用：

- DeepSeek：CNY，总余额 ¥88.50（赠送 10.00 / 充值 78.50），可用
- Kimi：5h 42%（2 小时后重置）、7d 68%（3 天后重置）、本月 35%（12 天后重置）
- GLM：5h 21%（1 小时后重置）、7d 55%（4 天后重置）、月度工具 12%「126/1000 次」（18 天后重置）

## 附录 B：告警判定伪代码

```
threshold = 配置阈值(默认80); resetBelow = threshold - 10
for usage in usages where state == ok:
    for window in usage.windows where usedPercent != null:
        key = "{kind}-{window.title}"
        if percent >= threshold and key not in alerted:
            alerted.add(key)
            发送Toast("{displayName} 用量告警", "{title}已使用{p}%，达到告警阈值{t}%。")
        elif percent < resetBelow and key in alerted:
            alerted.remove(key)
save(alerted)
```

## 附录 C：参考

- macOS 原版仓库：https://github.com/chenxing890/AITokenUsageWidget
- Windows Widgets 文档：https://learn.microsoft.com/windows/apps/develop/widgets/
- Adaptive Cards：https://adaptivecards.io
- WinUI 3 / Windows App SDK：https://learn.microsoft.com/windows/apps/winui/
