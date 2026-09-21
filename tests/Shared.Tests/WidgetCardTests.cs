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
        Assert.Contains("\"style\":\"emphasis\"", json);   // macOS：每供应商独立卡片底色
        Assert.Contains("更新于", json);                    // macOS：底部更新时间
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
    public void WithImageServer_ProviderCardsRenderAsSingleImage()
    {
        const string server = "http://127.0.0.1:49231";
        var json = WidgetCard.Build(WidgetSize.Large, Placeholders.All(),
            ThemePreference.System, systemDark: true, Now, server);

        // 每个供应商整卡渲染为一张 PNG（圆角/padding/图标/进度条全在图内）
        var container = Parse(json).GetProperty("body").EnumerateArray().Single();
        var images = container.GetProperty("items").EnumerateArray()
            .Where(e => e.GetProperty("type").GetString() == "Image").ToList();
        Assert.Equal(3, images.Count); // DeepSeek / Kimi / GLM 各一张
        foreach (var image in images)
        {
            Assert.StartsWith($"{server}/pcard?w=340&d=", image.GetProperty("url").GetString());
            Assert.Equal("stretch", image.GetProperty("size").GetString());
        }
        Assert.DoesNotContain("backgroundImage", json); // 不再用分段背景堆叠
        Assert.DoesNotContain("█", json);               // 不再使用文本块兜底

        // 载荷 = base64url(JSON)，携带全部渲染数据（Board 按 URL 缓存，数据变则 URL 变）
        var url = images[0].GetProperty("url").GetString()!;
        var d = url.Split("&d=")[1];
        var b64 = d.Replace('-', '+').Replace('_', '/');
        b64 += b64.Length % 4 == 2 ? "==" : b64.Length % 4 == 3 ? "=" : "";
        var payload = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64));
        Assert.Contains("\"k\":\"deepseek\"", payload);
        Assert.Contains("\"d\":1", payload);      // 系统深色
        Assert.Contains("¥ 88.50", payload);      // 余额大字
        Assert.Contains("赠送 ¥10.00 · 充值 ¥78.50", payload);
    }

    [Fact]
    public void WithoutImageServer_FallsBackToTextBar()
    {
        var json = WidgetCard.Build(WidgetSize.Large, Placeholders.All(),
            ThemePreference.System, systemDark: true, Now, imageBaseUrl: null);
        Assert.Contains("█", json);
        Assert.DoesNotContain("/pcard?", json);
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

    // ---- 7 天窗口健康配额线（对齐 macOS healthyQuotaFraction） ----

    [Fact]
    public void HealthyQuota_ComputesFromResetTime()
    {
        // 重置时间 = 现在 + 3 天 → 起点 = 现在 - 4 天 → 已过 4/7 ≈ 57.14%
        var window = new UsageWindow { Title = "7 天", ResetTime = Now.AddDays(3) };
        var marker = window.HealthyQuotaPercent(Now);
        Assert.NotNull(marker);
        Assert.Equal(400.0 / 7, marker!.Value, precision: 1);
    }

    [Fact]
    public void HealthyQuota_WeeklyOnly_AndNullWithoutReset()
    {
        Assert.True(new UsageWindow { Title = "7 天" }.IsWeekly);
        Assert.False(new UsageWindow { Title = "5 小时" }.IsWeekly);
        Assert.False(new UsageWindow { Title = "本月总额" }.IsWeekly);
        Assert.Null(new UsageWindow { Title = "7 天" }.HealthyQuotaPercent(Now)); // 无重置时间
    }

    [Fact]
    public void WeeklyWindow_PayloadContainsMarker()
    {
        // 7 天窗口（重置时间 = 现在 + 3 天 → 健康线 ≈57%），用实时 now 使占位数据有效
        var now = DateTimeOffset.UtcNow;
        var json = WidgetCard.Build(WidgetSize.Large, [Placeholders.Kimi()],
            ThemePreference.System, false, now, "http://127.0.0.1:49231");
        // 解码 base64url 载荷，断言含健康线标记 "m":
        var url = Parse(json).GetProperty("body").EnumerateArray().Single()
            .GetProperty("items").EnumerateArray()
            .First(e => e.GetProperty("type").GetString() == "Image")
            .GetProperty("url").GetString()!;
        var d = url.Split("&d=")[1].Replace('-', '+').Replace('_', '/');
        d += d.Length % 4 == 2 ? "==" : d.Length % 4 == 3 ? "=" : "";
        var payload = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(d));
        Assert.Contains("\"m\":", payload);
    }

    [Fact]
    public void WeeklyWindow_TextFallbackShowsMarkerChar()
    {
        // 文本兜底：7 天进度条含刻度字符「▎」
        var now = DateTimeOffset.UtcNow;
        var json = WidgetCard.Build(WidgetSize.Large, [Placeholders.Kimi()],
            ThemePreference.System, false, now, imageBaseUrl: null);
        Assert.Contains("▎", json);
    }
}
