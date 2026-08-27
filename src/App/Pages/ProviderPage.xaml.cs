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

    public ProviderPage()
    {
        InitializeComponent();
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
    }

    private async Task FetchAndShowPreviewAsync()
    {
        if (_fetching) return;
        _fetching = true;
        TestButton.IsEnabled = false;
        TestingRing.Visibility = Visibility.Visible;
        TestingRing.IsActive = true;
        try
        {
            var usage = await _usageService.FetchAsync(_config);
            PreviewCard.Usage = usage;
            SimulatedBadge.Visibility = Visibility.Collapsed;
            PreviewCaption.Text = usage.State switch
            {
                UsageState.Ok => $"实际用量 · 更新于 {Format.ClockTime(usage.FetchedAt)}",
                UsageState.MissingKey => "未配置 API Key",
                _ => usage.ErrorMessage,
            };
        }
        finally
        {
            _fetching = false;
            TestButton.IsEnabled = true;
            TestingRing.Visibility = Visibility.Collapsed;
            TestingRing.IsActive = false;
        }
    }

    // ---- 配置编辑（自动保存 + 通知小组件） ----

    private void OnEnableToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _config.IsEnabled = EnableToggle.IsOn;
        UpdateCardsVisibility();
        Save();
    }

    private void OnApiKeyChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _config.ApiKey = ApiKeyBox.Password;
        Save();
    }

    private void OnCookieChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _config.ExtraToken = CookieBox.Password;
        Save();
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

        TestButton.IsEnabled = false;
        TestingRing.Visibility = Visibility.Visible;
        TestingRing.IsActive = true;
        try
        {
            var usage = await _usageService.FetchAsync(_config);
            if (usage.State == UsageState.Ok)
            {
                ShowTestResult("\uE73E", Microsoft.UI.Colors.MediumSeaGreen,
                    "连接成功", DescribeUsage(usage));
                PreviewCard.Usage = usage;
                SimulatedBadge.Visibility = Visibility.Collapsed;
                PreviewCaption.Text = $"实际用量 · 更新于 {Format.ClockTime(usage.FetchedAt)}";
            }
            else
            {
                ShowTestResult("\uEA39", Microsoft.UI.Colors.OrangeRed,
                    "连接失败", usage.ErrorMessage ?? "请求失败");
            }
        }
        finally
        {
            TestButton.IsEnabled = true;
            TestingRing.Visibility = Visibility.Collapsed;
            TestingRing.IsActive = false;
        }
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
