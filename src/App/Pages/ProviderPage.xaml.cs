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
/// 供应商详情页（FR-3）：启用开关、API Key（自动清洗、自动保存）、kimi Cookie、
/// 自定义名 / 接口地址、测试连接、打开控制台、内嵌实时预览（模拟 / 实际两种形态）。
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

        HeaderIcon.Glyph = _kind switch
        {
            ProviderKind.DeepSeek => "\uEC4C",
            ProviderKind.Kimi => "\uE708",
            _ => "\uEF83",
        };
        HeaderIcon.Foreground = (Brush)Application.Current.Resources[$"Brand{_kind}"];
        HeaderTitle.Text = _kind.DisplayName();
        HeaderSubtitle.Text = _kind.Subtitle();
        ApiKeyHint.Text = $"格式提示：{_kind.ApiKeyHint()} · 粘贴的 \"Bearer \" 前缀与首尾空白会自动清理";

        EnableToggle.IsOn = _config.IsEnabled;
        EnabledBadge.Visibility = _config.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
        ApiKeyBox.Password = _config.ApiKey;
        CookieSection.Visibility = _kind.SupportsCookie() ? Visibility.Visible : Visibility.Collapsed;
        CookieBox.Password = _config.ExtraToken;
        NameBox.Text = _config.CustomName;
        BaseURLBox.PlaceholderText = _kind.DefaultBaseURL();
        BaseURLBox.Text = _config.BaseURLOverride;
        _loading = false;

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

    private void ShowSimulatedPreview()
    {
        PreviewCard.Usage = Placeholders.For(_kind);
        SimulatedBadge.Visibility = Visibility.Visible;
        PreviewCaption.Text = "模拟数据，配置 API Key 后显示实际用量";
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
        EnabledBadge.Visibility = _config.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
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

    private void OnBaseURLChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        _config.BaseURLOverride = BaseURLBox.Text;
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
            TestInfoBar.Severity = InfoBarSeverity.Warning;
            TestInfoBar.Title = "未配置 API Key";
            TestInfoBar.Message = "请先粘贴 API Key 再测试连接。";
            TestInfoBar.IsOpen = true;
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
                TestInfoBar.Severity = InfoBarSeverity.Success;
                TestInfoBar.Title = "连接成功";
                TestInfoBar.Message = DescribeUsage(usage);
                PreviewCard.Usage = usage;
                SimulatedBadge.Visibility = Visibility.Collapsed;
                PreviewCaption.Text = $"实际用量 · 更新于 {Format.ClockTime(usage.FetchedAt)}";
            }
            else
            {
                TestInfoBar.Severity = InfoBarSeverity.Error;
                TestInfoBar.Title = "连接失败";
                TestInfoBar.Message = usage.ErrorMessage ?? "请求失败";
            }
            TestInfoBar.IsOpen = true;
        }
        finally
        {
            TestButton.IsEnabled = true;
            TestingRing.Visibility = Visibility.Collapsed;
            TestingRing.IsActive = false;
        }
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
