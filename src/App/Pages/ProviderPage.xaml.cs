using AITokenUsageWidget.App.Controls;
using AITokenUsageWidget.App.Services;
using AITokenUsageWidget.Shared.Models;
using AITokenUsageWidget.Shared.Services;
using AITokenUsageWidget.Shared.Simulation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace AITokenUsageWidget.App.Pages;

/// <summary>
/// 供应商详情页（对齐 macOS ProviderDetailColumn）：
/// 头部（图标 + 名称 + 启用开关）→ 未启用提示 / 接口配置 / 连接测试 → 小组件实时预览。
/// 所有修改即时自动保存并通知小组件刷新。
/// </summary>
public sealed partial class ProviderPage : Page
{
    private ProviderKind _kind = ProviderKind.DeepSeek;
    private ProviderConfig _config = new(ProviderKind.DeepSeek);
    private bool _loading;
    private bool _fetching;
    private readonly UsageService _usageService = new();
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _autoConnectTimer;

    public ProviderPage()
    {
        InitializeComponent();

        // 输入 API Key / Cookie 后防抖 800ms 自动连接（§4.2：粘贴即测，无需手动点按钮）
        _autoConnectTimer = DispatcherQueue.CreateTimer();
        _autoConnectTimer.Interval = TimeSpan.FromMilliseconds(800);
        _autoConnectTimer.IsRepeating = false;
        _autoConnectTimer.Tick += (_, _) =>
        {
            if (!_loading && _config.HasKey) _ = FetchAndShowPreviewAsync();
        };
        Unloaded += (_, _) => _autoConnectTimer.Stop();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is ProviderKind kind) _kind = kind;
        LoadConfig();
    }

    private void LoadConfig()
    {
        _loading = true;
        _config = AppSettings.Config(_kind);

        HeaderChip.Kind = _kind;
        HeaderTitle.Text = _kind.DisplayName();
        HeaderSubtitle.Text = _kind.Subtitle();
        BuiltInUrlHint.Text = $"接口地址已内置：{_kind.DefaultBaseURL()}";
        ApiKeyLabel.Text = $"API Key（{_kind.ApiKeyHint()}）";
        NameLabel.Text = $"显示名称（留空使用 {_kind.DisplayName()}）";
        DisabledHint.Text = $"打开右上角开关启用 {_kind.DisplayName()}。配置并保存后，对应卡片会自动出现在 Windows 小组件面板中。";

        // 测试按钮用品牌色（对齐 macOS .borderedProminent tint）
        TestButton.Background = (Brush)Application.Current.Resources[$"Brand{_kind}"];

        EnableToggle.IsOn = _config.IsEnabled;
        ApiKeyBox.Password = _config.ApiKey;
        CookieSection.Visibility = _kind.SupportsCookie() ? Visibility.Visible : Visibility.Collapsed;
        CookieBox.Password = _config.ExtraToken;
        NameBox.Text = _config.CustomName;
        NameBox.PlaceholderText = _kind.DisplayName();
        _loading = false;

        UpdateCardsVisibility();

        // 已配置：自动拉取一次实际用量；未配置：显示模拟数据（附录 A）
        if (_config.HasKey)
        {
            ShowConnectingPreview();
            _ = FetchAndShowPreviewAsync();
        }
        else
        {
            ShowSimulatedPreview();
        }
    }

    private void UpdateCardsVisibility()
    {
        var enabled = _config.IsEnabled;
        DisabledCard.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        ConfigCard.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        TestCard.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowSimulatedPreview()
    {
        PreviewCard.Usage = Placeholders.For(_kind);
        SimulatedBadge.Visibility = Visibility.Visible;
        PreviewCaption.Text = "模拟数据 · 配置 API Key 后自动显示实际用量";
        TestResultPanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>连接中：先上占位卡片（预览区域不塌陷），并显示「正在连接」反馈。</summary>
    private void ShowConnectingPreview()
    {
        PreviewCard.Usage = Placeholders.For(_kind);
        SimulatedBadge.Visibility = Visibility.Collapsed;
        PreviewCaption.Text = "正在连接…";
        ShowTestResult("\uE895", Windows.UI.Color.FromArgb(0xFF, 0x75, 0x75, 0x75),
            "正在连接", "请稍候…");
    }

    private async Task FetchAndShowPreviewAsync()
    {
        if (_fetching) return;
        _fetching = true;
        RunOnUi(() =>
        {
            TestButton.IsEnabled = false;
            TestingRing.Visibility = Visibility.Visible;
            TestingRing.IsActive = true;
        });
        try
        {
            // 看门狗：无论底层因何挂起（网络栈僵死 / 取消信号不传播），
            // 15 秒内必须给用户一个确定结果，绝不无限「正在连接」
            var fetchTask = _usageService.FetchAsync(_config);
            var finished = await Task.WhenAny(fetchTask, Task.Delay(TimeSpan.FromSeconds(15)));
            if (!ReferenceEquals(finished, fetchTask))
            {
                RunOnUi(() =>
                {
                    PreviewCaption.Text = "连接超时";
                    ShowTestResult("\uEA39", Microsoft.UI.Colors.OrangeRed,
                        "连接超时", "请求超过 15 秒未返回，请检查网络或代理设置。");
                });
                return;
            }
            var usage = await fetchTask;

            // 关键：await 的 continuation 会落在线程池线程（SynchronizationContext
            // 不保证跨 await 存活），WinUI 控件只允许 UI 线程访问——
            // 直接更新会抛 RPC_E_WRONG_THREAD，被 fire-and-forget 吞掉，
            // 表现为「正在连接」永驻。所有 UI 更新必须 Marshal 回 UI 线程。
            RunOnUi(() =>
            {
                PreviewCard.Usage = usage;
                SimulatedBadge.Visibility = Visibility.Collapsed;
                switch (usage.State)
                {
                    case UsageState.Ok:
                        PreviewCaption.Text = $"实际用量 · 更新于 {Format.ClockTime(usage.FetchedAt)}";
                        ShowTestResult("\uE73E", Microsoft.UI.Colors.MediumSeaGreen,
                            "连接成功", DescribeUsage(usage));
                        break;
                    case UsageState.MissingKey:
                        PreviewCaption.Text = "未配置 API Key";
                        TestResultPanel.Visibility = Visibility.Collapsed;
                        break;
                    default:
                        PreviewCaption.Text = usage.ErrorMessage;
                        ShowTestResult("\uEA39", Microsoft.UI.Colors.OrangeRed,
                            "连接失败", usage.ErrorMessage ?? "请求失败");
                        break;
                }
            });
        }
        catch (Exception ex)
        {
            // fire-and-forget 的异常会被静默吞掉（表现为「正在连接」永驻），必须就地消化并反馈
            RunOnUi(() => ShowTestResult("\uEA39", Microsoft.UI.Colors.OrangeRed,
                "显示失败", Shared.Networking.ApiHttp.FriendlyMessage(ex)));
        }
        finally
        {
            _fetching = false;
            RunOnUi(() =>
            {
                TestButton.IsEnabled = true;
                TestingRing.Visibility = Visibility.Collapsed;
                TestingRing.IsActive = false;
            });
        }
    }

    /// <summary>已在 UI 线程则同步执行，否则派发到 UI 线程。</summary>
    private void RunOnUi(Action action)
    {
        if (DispatcherQueue.HasThreadAccess) action();
        else DispatcherQueue.TryEnqueue(() => action());
    }

    // ---- 配置编辑（自动保存 + 通知小组件） ----

    private void OnEnableToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _config.IsEnabled = EnableToggle.IsOn;
        UpdateCardsVisibility();
        Save();
        if (_config.IsEnabled && _config.HasKey) ScheduleAutoConnect();
    }

    private void OnApiKeyChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _config.ApiKey = ApiKeyBox.Password;
        Save();
        ScheduleAutoConnect();
    }

    private void OnCookieChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _config.ExtraToken = CookieBox.Password;
        Save();
        ScheduleAutoConnect();
    }

    /// <summary>输入 Key/Cookie 后防抖自动连接，给出明确的成功 / 失败反馈。</summary>
    private void ScheduleAutoConnect()
    {
        if (!_config.HasKey) return;
        ShowConnectingPreview();
        _autoConnectTimer.Stop();
        _autoConnectTimer.Start();
    }

    private void OnNameChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        _config.CustomName = NameBox.Text;
        Save();
    }

    private void Save()
    {
        AppSettings.SaveConfig(_config); // 即时持久化 + 通知小组件刷新
        if (!_config.HasKey && !_loading) ShowSimulatedPreview();
    }

    // ---- 测试连接（§4.2） ----

    private async void OnTestConnection(object sender, RoutedEventArgs e)
    {
        if (!_config.HasKey)
        {
            ShowTestResult("\uE8D7", Microsoft.UI.Colors.DarkOrange,
                "未配置 API Key", "请先粘贴 API Key 再测试连接。");
            return;
        }

        _fetching = false; // 手动测试优先：允许打断防抖排队的自动连接
        await FetchAndShowPreviewAsync();
    }

    private void ShowTestResult(string glyph, Windows.UI.Color color, string title, string message)
    {
        TestResultIcon.Glyph = glyph;
        TestResultIcon.Foreground = new SolidColorBrush(color);
        TestResultText.Text = $"{title}：{message}";
        TestResultPanel.Visibility = Visibility.Visible;
    }

    private static string DescribeUsage(ProviderUsage usage)
    {
        if (usage.Kind.ShowsBalance())
        {
            return $"当前余额 {usage.CurrencySymbol}{Format.Amount(usage.TotalBalance ?? 0)}";
        }
        var windows = usage.Windows
            .Select(w => w.UsedPercent is { } p ? $"{w.Title} {Format.Percent(p)}%" : w.UsedText)
            .Where(s => !string.IsNullOrEmpty(s));
        return string.Join(" · ", windows);
    }

    private async void OnOpenConsole(object sender, RoutedEventArgs e)
    {
        if (Uri.TryCreate(_kind.ConsoleURL(), UriKind.Absolute, out var uri))
        {
            await Launcher.LaunchUriAsync(uri);
        }
    }
}
