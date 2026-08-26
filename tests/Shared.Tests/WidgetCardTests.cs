using System.Text.Json;
using AITokenUsageWidget.Shared.Models;
using AITokenUsageWidget.Shared.Services;
using AITokenUsageWidget.Shared.Simulation;
using AITokenUsageWidget.Shared.Widgets;
using Xunit;

namespace AITokenUsageWidget.Shared.Tests;

public class WidgetCardTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 9, 12, 0, 0, TimeSpan.Zero);

    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json); // 非法 JSON 会抛异常
        return doc.RootElement.Clone();
    }

    [Fact]
    public void Build_ProducesValidAdaptiveCard()
    {
        var json = WidgetCard.Build(WidgetSize.Medium, Placeholders.All(),
            ThemePreference.System, systemDark: false, Now);
        var root = Parse(json);

        Assert.Equal("AdaptiveCard", root.GetProperty("type").GetString());
        Assert.Equal("1.5", root.GetProperty("version").GetString());

        var actions = root.GetProperty("actions");
        var refresh = actions.EnumerateArray().Single();
        Assert.Equal("Action.Execute", refresh.GetProperty("type").GetString());
        Assert.Equal("refresh", refresh.GetProperty("verb").GetString()); // 手动刷新（FR-2）
    }

    [Fact]
    public void Medium_ThreeProviders_UsesDenseColumns()
    {
        var json = WidgetCard.Build(WidgetSize.Medium, Placeholders.All(),
            ThemePreference.System, false, Now);
        // 供应商区 ColumnSet 为三列（首部标题行是两列的另一个 ColumnSet）
        var providerColumns = Parse(json).GetProperty("body").EnumerateArray()
            .Where(e => e.GetProperty("type").GetString() == "ColumnSet")
            .Select(e => e.TryGetProperty("columns", out var cols) ? cols.GetArrayLength() : 0)
            .ToList();
        Assert.Equal([2, 3], providerColumns); // FR-2 / §3.4：3 供应商自动紧凑三列

        Assert.Contains("5h", json);   // dense 短标题
        Assert.Contains("工具", json);
        Assert.DoesNotContain("▰", json); // dense 模式不画进度条
    }

    [Fact]
    public void Medium_TwoProviders_ShowsProgressBars()
    {
        var usages = new List<ProviderUsage> { Placeholders.Kimi(), Placeholders.Glm() };
        var json = WidgetCard.Build(WidgetSize.Medium, usages, ThemePreference.System, false, Now);

        Assert.Contains("▰", json); // 进度条
        Assert.Contains("5 小时", json);
    }

    [Fact]
    public void Large_ShowsWindowsAndCountdown()
    {
        var json = WidgetCard.Build(WidgetSize.Large, Placeholders.All(),
            ThemePreference.System, false, Now);

        Assert.Contains("5 小时", json);
        Assert.Contains("月度工具", json);
        Assert.Contains("126/1000 次", json);
        Assert.Contains("后重置", json); // 重置倒计时
        Assert.Contains("▰", json);
    }

    [Fact]
    public void BalanceProvider_ShowsCurrencyAndDetail()
    {
        var json = WidgetCard.Build(WidgetSize.Medium, [Placeholders.DeepSeek()],
            ThemePreference.System, false, Now);

        Assert.Contains("¥ 88.50", json);
        Assert.Contains("赠送 ¥10.00 · 充值 ¥78.50", json);
    }

    [Fact]
    public void ErrorState_ShowsFriendlyMessage()
    {
        var usage = new ProviderUsage
        {
            Kind = ProviderKind.Glm,
            DisplayName = "GLM Coding",
            State = UsageState.Error,
            ErrorMessage = "认证失败（HTTP 401）：请检查 API Key 是否正确",
        };
        var json = WidgetCard.Build(WidgetSize.Medium, [usage], ThemePreference.System, false, Now);
        Assert.Contains("认证失败（HTTP 401）", json);
    }

    [Fact]
    public void EmptyState_ShowsGuidance()
    {
        var json = WidgetCard.Build(WidgetSize.Medium, [], ThemePreference.System, false, Now);
        Assert.Contains("打开「AI 模型用量」应用", json);
    }

    [Fact]
    public void DarkTheme_ForcesLightTextAndBackground()
    {
        var json = WidgetCard.Build(WidgetSize.Medium, Placeholders.All(),
            ThemePreference.Dark, systemDark: false, Now);
        var root = Parse(json);

        Assert.Equal("ms-appx:///Assets/CardBgDark.png",
            root.GetProperty("backgroundImage").GetString());
        Assert.Contains("\"Light\"", json); // 显式浅色文字，避免白字落白底（§3.2）
    }

    [Fact]
    public void LightTheme_ForcesDarkTextAndBackground()
    {
        var json = WidgetCard.Build(WidgetSize.Medium, Placeholders.All(),
            ThemePreference.Light, systemDark: true, Now);

        Assert.Contains("ms-appx:///Assets/CardBgLight.png", json);
        Assert.Contains("\"Dark\"", json);
    }

    [Fact]
    public void SystemTheme_NoBackgroundReliesOnHost()
    {
        var json = WidgetCard.Build(WidgetSize.Medium, Placeholders.All(),
            ThemePreference.System, systemDark: true, Now);

        Assert.DoesNotContain("backgroundImage", json);
    }

    [Fact]
    public void CacheFallback_Annotation()
    {
        var cached = Placeholders.Kimi();
        var usage = new ProviderUsage
        {
            Kind = cached.Kind, DisplayName = cached.Kind.DisplayName(), State = cached.State,
            Windows = cached.Windows, FetchedAt = Now.AddHours(-1), FromCache = true,
        };
        var json = WidgetCard.Build(WidgetSize.Medium, [usage], ThemePreference.System, false, Now);

        Assert.Contains("更新失败，显示缓存数据", json);
    }

    [Fact]
    public void English_Language_SwitchesTexts()
    {
        L10n.Language = "en";
        try
        {
            var json = WidgetCard.Build(WidgetSize.Medium, [], ThemePreference.System, false, Now);
            Assert.Contains("AI Usage", json);
            Assert.Contains("Refresh", json);
        }
        finally
        {
            L10n.Language = "zh";
        }
    }

    [Fact]
    public void ProgressBar_ReflectsPercent()
    {
        Assert.Equal("▰▰▰▰▰▱▱▱▱▱", WidgetCard.ProgressBar(50));
        Assert.Equal("▱▱▱▱▱▱▱▱▱▱", WidgetCard.ProgressBar(0));
        Assert.Equal("▰▰▰▰▰▰▰▰▰▰", WidgetCard.ProgressBar(100));
    }

    [Fact]
    public void ResetCountdown_Text()
    {
        Assert.Equal("2 小时后重置", Format.ResetCountdown(Now.AddHours(2), Now));
        Assert.Equal("3 天后重置", Format.ResetCountdown(Now.AddDays(3), Now));
        Assert.Equal("5 分钟后重置", Format.ResetCountdown(Now.AddMinutes(5), Now));
        Assert.Equal("即将重置", Format.ResetCountdown(Now.AddSeconds(3), Now));
    }
}
