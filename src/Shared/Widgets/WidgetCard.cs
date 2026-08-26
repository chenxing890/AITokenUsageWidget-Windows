using System.Text.Json;
using System.Text.Json.Serialization;
using AITokenUsageWidget.Shared.Models;
using AITokenUsageWidget.Shared.Services;

namespace AITokenUsageWidget.Shared.Widgets;

public enum WidgetSize
{
    Medium,
    Large,
}

/// <summary>
/// 小组件 Adaptive Card（v1.5）构建器（FR-2 / §3.4）。
/// 输出完整字面卡片 JSON（Template 与 Data 由本方法合成，Data 固定为 "{}"）。
/// 主题：Adaptive Cards 不支持任意十六进制前景色，采用「TextBlock.color 枚举 + 包内纯色
/// 背景图」实现显式明暗（浅色 → Dark 文字 + 浅背景图；深色 → Light 文字 + 深背景图；
/// 跟随系统 → Default 文字 + 不加背景，由 Widgets Board 按系统主题渲染）——
/// 避免「白字落白底」（§3.2 / §5.5 平台差异兜底）。
/// </summary>
public static class WidgetCard
{
    private const string BackgroundLight = "ms-appx:///Assets/CardBgLight.png";
    private const string BackgroundDark = "ms-appx:///Assets/CardBgDark.png";

    public static string Build(
        WidgetSize size,
        IReadOnlyList<ProviderUsage> usages,
        ThemePreference theme,
        bool systemDark,
        DateTimeOffset now)
    {
        var textColor = theme switch
        {
            ThemePreference.Light => "Dark",
            ThemePreference.Dark => "Light",
            _ => "Default",
        };
        var forcedDark = theme.ForcedDark() ?? systemDark;
        var background = theme switch
        {
            ThemePreference.Light => BackgroundLight,
            ThemePreference.Dark => BackgroundDark,
            _ => null,
        };

        var body = new List<object>();

        // 顶部标题行：标题 + 最后更新时间
        body.Add(new Dictionary<string, object?>
        {
            ["type"] = "ColumnSet",
            ["spacing"] = "None",
            ["columns"] = new object[]
            {
                Col("stretch", new object[]
                {
                    Text(L10n.Get("widgetTitle"), size: "Medium", weight: "Bolder", color: textColor),
                }),
                Col("auto", new object[]
                {
                    Text(UpdatedText(usages, now), size: "ExtraSmall", isSubtle: true,
                        color: textColor, horizontalAlignment: "right"),
                }),
            },
        });

        if (usages.Count == 0)
        {
            body.Add(Text(L10n.Get("emptyHint1"), size: "Default", color: textColor,
                horizontalAlignment: "center", spacing: "Padding"));
            body.Add(Text(L10n.Get("emptyHint2"), size: "Default", isSubtle: true, color: textColor,
                horizontalAlignment: "center"));
        }
        else if (size == WidgetSize.Medium)
        {
            var dense = usages.Count >= 3; // 3 个供应商自动切换紧凑三列（FR-2）
            var columns = usages.Take(3).Select(usage => Col("stretch", DenseOrCompactItems(usage, dense, textColor, now))).ToArray();
            body.Add(new Dictionary<string, object?>
            {
                ["type"] = "ColumnSet",
                ["spacing"] = "Padding",
                ["columns"] = columns,
            });
        }
        else
        {
            foreach (var (usage, index) in usages.Take(4).Select((u, i) => (u, i)))
            {
                body.Add(new Dictionary<string, object?>
                {
                    ["type"] = "Container",
                    ["separator"] = index > 0,
                    ["spacing"] = index > 0 ? "Padding" : "ExtraLarge",
                    ["items"] = LargeProviderItems(usage, textColor, now),
                });
            }
        }

        var card = new Dictionary<string, object?>
        {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["type"] = "AdaptiveCard",
            ["version"] = "1.5",
            ["body"] = body,
            ["actions"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "Action.Execute",
                    ["verb"] = "refresh",
                    ["title"] = L10n.Get("refresh"),
                },
            },
        };
        if (background != null) card["backgroundImage"] = background;

        return JsonSerializer.Serialize(card, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            // 中文等非 ASCII 字符不转义为 \uXXXX，便于阅读与断言
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    public static string EmptyData => "{}";

    // ---- Medium：1–2 个供应商宽松双列；3 个自动 dense 三列 ----

    private static object[] DenseOrCompactItems(ProviderUsage usage, bool dense, string textColor, DateTimeOffset now)
    {
        var items = new List<object>
        {
            // 名称（截断）
            Text(usage.DisplayName, size: dense ? "ExtraSmall" : "Small", weight: "Bolder",
                color: textColor, wrap: false),
        };

        switch (usage.State)
        {
            case UsageState.MissingKey:
                items.Add(Text(L10n.Get("missingKey"), size: "ExtraSmall", isSubtle: true, color: textColor, wrap: dense));
                break;
            case UsageState.Error:
                items.Add(Text(usage.ErrorMessage ?? "请求失败", size: "ExtraSmall",
                    color: "Attention", wrap: dense));
                break;
            default:
                if (usage.Kind.ShowsBalance())
                {
                    items.Add(Text(
                        usage.TotalBalance is { } total
                            ? $"{usage.CurrencySymbol} {Format.Amount(total)}"
                            : "--",
                        size: dense ? "Medium" : "ExtraLarge", weight: "Bolder", color: textColor, wrap: false));
                    if (!dense)
                        items.Add(BalanceDetail(usage, textColor));
                }
                else
                {
                    foreach (var window in usage.Windows.Take(dense ? 3 : usage.Windows.Count))
                    {
                        items.AddRange(WindowItems(window, dense, textColor, now));
                    }
                }
                break;
        }
        return items.ToArray();
    }

    // ---- Large：每个供应商一块，含进度条与重置倒计时 ----

    private static object[] LargeProviderItems(ProviderUsage usage, string textColor, DateTimeOffset now)
    {
        var items = new List<object>
        {
            Text(usage.DisplayName, size: "Medium", weight: "Bolder", color: textColor),
        };

        switch (usage.State)
        {
            case UsageState.MissingKey:
                items.Add(Text(L10n.Get("missingKey"), size: "Small", isSubtle: true, color: textColor));
                break;
            case UsageState.Error:
                items.Add(Text(usage.ErrorMessage ?? "请求失败", size: "Small", color: "Attention", wrap: true));
                break;
            default:
                if (usage.Kind.ShowsBalance())
                {
                    items.Add(Text(
                        usage.TotalBalance is { } total
                            ? $"{usage.CurrencySymbol} {Format.Amount(total)}"
                            : "--",
                        size: "ExtraLarge", weight: "Bolder", color: textColor));
                    items.Add(BalanceDetail(usage, textColor));
                }
                else
                {
                    foreach (var window in usage.Windows)
                    {
                        items.AddRange(WindowItems(window, dense: false, textColor, now));
                    }
                }
                break;
        }
        return items.ToArray();
    }

    private static object BalanceDetail(ProviderUsage usage, string textColor) => Text(
        usage.GrantedBalance is { } granted && usage.ToppedUpBalance is { } toppedUp
            ? L10n.Get("grantedToppedUp",
                $"{usage.CurrencySymbol}{Format.Amount(granted)}",
                $"{usage.CurrencySymbol}{Format.Amount(toppedUp)}")
            : "",
        size: "ExtraSmall", isSubtle: true, color: textColor, required: false);

    /// <summary>一个窗口的渲染行：percent 窗口 → 标题行 + 进度条；否则单行绝对值文本。</summary>
    private static IEnumerable<object> WindowItems(UsageWindow window, bool dense, string textColor, DateTimeOffset now)
    {
        if (window.UsedPercent is not { } percent)
        {
            // 无百分比窗口（如「30 天累计」）：单行文本，不画进度条
            yield return Text(
                $"{window.Title}   {window.UsedText ?? "--"}",
                size: "ExtraSmall", isSubtle: true, color: textColor, wrap: false);
            yield break;
        }

        var countdown = Format.ResetCountdown(window.ResetTime, now);
        if (dense)
        {
            // 极简单行：标题 + 百分比（或累计文本），无进度条
            var value = window.UsedPercent is { } p
                ? $"{Format.Percent(p)}%"
                : window.UsedText ?? "--";
            yield return Text($"{window.ShortTitle}  {value}", size: "ExtraSmall", color: textColor, wrap: false);
            yield break;
        }

        var head = $"{window.Title}   {Format.Percent(percent)}%";
        if (!string.IsNullOrEmpty(window.UsedText)) head += $"   {window.UsedText}";
        if (countdown.Length > 0) head += $"   · {countdown}";
        yield return Text(head, size: "ExtraSmall", isSubtle: true, color: textColor, wrap: false);
        yield return Text(ProgressBar(percent), size: "Small", color: LevelColor(percent), wrap: false);
    }

    /// <summary>Unicode 块字符进度条（10 格）。</summary>
    public static string ProgressBar(double percent)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        var filled = (int)Math.Round(clamped / 10, MidpointRounding.AwayFromZero);
        return new string('▰', filled) + new string('▱', 10 - filled);
    }

    /// <summary>用量级别色：≥80% 红（Attention）、≥50% 橙（Warning）、否则绿（Good）。</summary>
    public static string LevelColor(double percent) => percent switch
    {
        >= 80 => "Attention",
        >= 50 => "Warning",
        _ => "Good",
    };

    private static string UpdatedText(IReadOnlyList<ProviderUsage> usages, DateTimeOffset now)
    {
        if (usages.Count == 0) return "";
        var anyCache = usages.Any(u => u.FromCache);
        var oldest = usages.Min(u => u.FetchedAt);
        return anyCache
            ? L10n.Get("cacheFallback", Format.ClockTime(oldest))
            : L10n.Get("updatedAt", Format.TimeAgo(oldest, now));
    }

    // ---- JSON 节点辅助 ----

    private static object Text(string text, string size = "Default", string? weight = null,
        string? color = null, bool isSubtle = false, bool wrap = false,
        string? horizontalAlignment = null, string spacing = "None", bool required = true) =>
        new Dictionary<string, object?>
        {
            ["type"] = "TextBlock",
            ["text"] = text,
            ["size"] = size,
            ["weight"] = weight,
            ["color"] = color,
            ["isSubtle"] = isSubtle ? true : null,
            ["wrap"] = wrap ? true : null,
            ["horizontalAlignment"] = horizontalAlignment,
            ["spacing"] = spacing,
            ["isVisible"] = required || text.Length > 0 ? true : false,
        };

    private static object Col(string width, object[] items) => new Dictionary<string, object?>
    {
        ["type"] = "Column",
        ["width"] = width,
        ["items"] = items,
    };
}
