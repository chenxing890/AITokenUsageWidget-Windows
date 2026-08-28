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
    public void Build_ProducesValidAdaptiveCard_NoActions()
    {
        var json = WidgetCard.Build(WidgetSize.Medium, Placeholders.All(),
            ThemePreference.System, systemDark: false, Now);
        var root = Parse(json);

        Assert.Equal("AdaptiveCard", root.GetProperty("type").GetString());
        Assert.Equal("1.5", root.GetProperty("version").GetString());
        // 对齐 macOS：卡片无任何操作按钮（刷新自动进行，点击卡片打开主 App）
        Assert.False(root.TryGetProperty("actions", out _));
    }

    [Fact]
    public void Medium_ThreeProviders_UsesDenseColumns()
    {
        var json = WidgetCard.Build(WidgetSize.Medium, Placeholders.All(),
            ThemePreference.System, false, Now);
        // body 整体包裹在带 selectAction 的 Container 中（点击卡片拉起主 App）
        var container = Parse(json).GetProperty("body").EnumerateArray().Single();
        Assert.Equal("Container", container.GetProperty("type").GetString());
        // 无标题行：供应商区是唯一一个 ColumnSet，三列
        var items = container.GetProperty("items").EnumerateArray().ToList();
        var columnSets = items.Where(e => e.GetProperty("type").GetString() == "ColumnSet").ToList();
        Assert.Single(columnSets);
        Assert.Equal(3, columnSets[0].GetProperty("columns").GetArrayLength()); // FR-2 / §3.4：紧凑三列
        Assert.DoesNotContain("AI 模型用量", json); // 无标题行（对齐 macOS）

        Assert.Contains("5h", json);   // dense 短标题
        Assert.Contains("工具", json);
        Assert.Contains("█", json);    // §3.4：dense 也要双色块进度条
        Assert.Contains("68%", json);  // Kimi 最大窗口百分比大字
        // 品牌 emoji 图标（Board 不渲染 data: 图片）；emoji 为非 BMP 字符，
        // System.Text.Json 会转义为代理对（🐋 = D83D DC0B），Board 端正常解码
        Assert.Contains("\\uD83D\\uDC0B", json);
        Assert.DoesNotContain("\"type\":\"Image\"", json); // 卡片不含任何图片元素
    }

    [Fact]
    public void Card_SelectAction_OpensMainApp()
    {
        var json = WidgetCard.Build(WidgetSize.Medium, Placeholders.All(),
            ThemePreference.System, false, Now);
        var container = Parse(json).GetProperty("body").EnumerateArray().Single();
        var select = container.GetProperty("selectAction");
        // Action.Execute（Provider 的 OnActionInvoked 拉起主 App）——
        // Board 会静默拦截 Action.OpenUrl 的自定义协议
        Assert.Equal("Action.Execute", select.GetProperty("type").GetString());
        Assert.Equal("openApp", select.GetProperty("verb").GetString());
    }

    [Fact]
    public void EmptyState_StillClickable()
    {
        var root = Parse(WidgetCard.Build(WidgetSize.Medium, [], ThemePreference.System, false, Now));
        var container = root.GetProperty("body").EnumerateArray().Single();
        var select = container.GetProperty("selectAction");
        Assert.Equal("openApp", select.GetProperty("verb").GetString());
        Assert.False(root.TryGetProperty("actions", out _)); // 空态也无按钮，引导文案 + 可点击
    }

    [Fact]
    public void Medium_TwoProviders_ShowsProgressBars()
    {
        var usages = new List<ProviderUsage> { Placeholders.Kimi(), Placeholders.Glm() };
        var json = WidgetCard.Build(WidgetSize.Medium, usages, ThemePreference.System, false, Now);

        Assert.Contains("█", json); // 双色块进度条
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
        Assert.Contains("█", json); // 双色块进度条
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
    public void AnyTheme_NoBackgroundNoForcedColor_ReliesOnHost()
    {
        // Board 不渲染背景图：任何主题下都不强制文字色/背景，跟随系统主题渲染
        foreach (var theme in new[] { ThemePreference.System, ThemePreference.Light, ThemePreference.Dark })
        {
            var json = WidgetCard.Build(WidgetSize.Medium, Placeholders.All(), theme, false, Now);
            Assert.DoesNotContain("backgroundImage", json);
            Assert.DoesNotContain("\"Light\"", json);
            Assert.DoesNotContain("\"Dark\"", json);
        }
    }

    [Fact]
    public void CacheFallback_ShowsDataWithoutToolbar()
    {
        var cached = Placeholders.Kimi();
        var usage = new ProviderUsage
        {
            Kind = cached.Kind, DisplayName = cached.Kind.DisplayName(), State = cached.State,
            Windows = cached.Windows, FetchedAt = Now.AddHours(-1), FromCache = true,
        };
        var json = WidgetCard.Build(WidgetSize.Medium, [usage], ThemePreference.System, false, Now);

        // 缓存数据照常展示（无标题栏 / 无操作按钮，对齐 macOS）
        Assert.Contains("Kimi Code", json);
        Assert.Contains("█", json); // 双色块进度条
    }

    [Fact]
    public void English_Language_SwitchesTexts()
    {
        L10n.Language = "en";
        try
        {
            var json = WidgetCard.Build(WidgetSize.Medium, [], ThemePreference.System, false, Now);
            Assert.Contains("Open the", json); // 空态引导文案随语言切换
        }
        finally
        {
            L10n.Language = "zh";
        }
    }

    [Fact]
    public void WithImageServer_BarsAndIconsUseLocalHttpImages()
    {
        const string server = "http://127.0.0.1:49231";
        var json = WidgetCard.Build(WidgetSize.Large, Placeholders.All(),
            ThemePreference.System, systemDark: true, Now, server);

        // 进度条：胶囊条 PNG 由本地服务提供，固有尺寸 == 声明尺寸（Large 480x4）
        Assert.Contains($"\"url\":\"{server}/bar?", json);
        Assert.Contains("\"width\":\"480px\"", json);
        Assert.Contains("\"height\":\"4px\"", json);
        Assert.Contains("dark=1", json); // 系统深色 → 深轨道
        // 品牌图标：18px 固有尺寸
        Assert.Contains($"\"url\":\"{server}/icon/kimi?s=18\"", json);
        Assert.Contains($"\"url\":\"{server}/icon/deepseek?s=18\"", json);
        Assert.DoesNotContain("█", json); // 不再使用文本块兜底
    }

    [Fact]
    public void WithoutImageServer_FallsBackToTextBar()
    {
        var json = WidgetCard.Build(WidgetSize.Large, Placeholders.All(),
            ThemePreference.System, systemDark: true, Now, imageBaseUrl: null);
        Assert.Contains("█", json);
        Assert.DoesNotContain("/bar?", json);
    }

    [Fact]
    public void LevelColor_MatchesUsageCardControlLevels()
    {
        Assert.Equal("Attention", WidgetCard.LevelColor(80)); // 红
        Assert.Equal("Warning", WidgetCard.LevelColor(50));   // 橙
        Assert.Equal("Good", WidgetCard.LevelColor(49));      // 绿
    }

    [Fact]
    public void BarBlocks_FilledPartUsesLevelColor()
    {
        var low = WidgetCard.Build(WidgetSize.Medium, [Placeholders.Kimi()], ThemePreference.System, false, Now);
        Assert.Contains("\"Good\"", low); // Kimi 窗口 < 50% → 绿

        var high = Placeholders.Glm();
        var json = WidgetCard.Build(WidgetSize.Medium, [high], ThemePreference.System, false, Now);
        Assert.Contains("\"Warning\"", json); // GLM 有 ≥50% 窗口 → 橙
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
