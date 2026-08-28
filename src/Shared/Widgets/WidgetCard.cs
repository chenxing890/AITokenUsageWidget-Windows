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
///
/// 平台约束（实测 Windows Widgets Board，2026-08）：Board 渲染器不加载任何
/// data: URI 图片（探针卡片 + 全屏像素扫描验证：显式像素尺寸 / size=Small /
/// 固有尺寸三种声明方式均不渲染；连 200×200 纯色块也无一像素）。因此卡片
/// 不使用任何 Image / backgroundImage：
/// - 品牌图标 → emoji 文本（🐋/🌙/🤖，随文本渲染，天然支持明暗主题）；
/// - 进度条 → 双色「█」块 ColumnSet（填充段按用量级别着色 Good/Warning/
///   Attention，轨道段 isSubtle 灰），对齐 UsageCardControl 的级别色板；
/// - 文字颜色始终 Default，由 Board 按系统主题渲染（强制 Light/Dark 文字在
///   背景图不渲染时会对比度失控）。
/// </summary>
public static class WidgetCard
{
    public static string Build(
        WidgetSize size,
        IReadOnlyList<ProviderUsage> usages,
        ThemePreference theme,
        bool systemDark,
        DateTimeOffset now)
    {
        const string textColor = "Default"; // 见类注释：不强制明暗文字色

        var body = new List<object>();

        // 对齐 macOS：无标题栏 / 无操作按钮，卡片即内容（点击整卡拉起主 App，
        // 数据由 Provider 自动刷新：激活即刷 + 15 分钟定时 + 配置变更事件）

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
            // 整卡 selectAction：点击任意位置拉起主 App（Action.Execute 由
            // Provider 的 OnActionInvoked 处理——OpenUrl 自定义协议会被 Board 拦截）
            ["body"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "Container",
                    ["selectAction"] = new Dictionary<string, object?>
                    {
                        ["type"] = "Action.Execute",
                        ["verb"] = "openApp",
                        ["title"] = L10n.Get("openApp"),
                    },
                    ["items"] = body,
                },
            },
        };

        return JsonSerializer.Serialize(card, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            // 中文等非 ASCII 字符不转义为 \uXXXX，便于阅读与断言
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    public static string EmptyData => "{}";

    // ---- Medium：1–2 个供应商宽松双列；3 个自动 dense 三列 ----

    /// <summary>emoji 图标 + 名称标题行（Board 不渲染 data: 图片，emoji 随文本渲染）。</summary>
    private static object ProviderHeader(ProviderUsage usage, bool dense, string textColor, bool large = false) =>
        Text($"{EmojiFor(usage.Kind)} {usage.DisplayName}",
            size: dense ? "ExtraSmall" : large ? "Medium" : "Small",
            weight: "Bolder", color: textColor, wrap: false);

    /// <summary>品牌 emoji（对齐 macOS 品牌图标的位置语义）。</summary>
    public static string EmojiFor(ProviderKind kind) => kind switch
    {
        ProviderKind.DeepSeek => "🐋",
        ProviderKind.Kimi => "🌙",
        _ => "🤖",
    };

    private static object[] DenseOrCompactItems(ProviderUsage usage, bool dense, string textColor, DateTimeOffset now)
    {
        var items = new List<object>
        {
            ProviderHeader(usage, dense, textColor),
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
                else if (dense)
                {
                    // §3.4：最大窗口百分比大字 + 短标题行（5h/7d/月度）+ 细进度条
                    var windows = usage.Windows.Take(3).ToList();
                    var max = windows.Where(w => w.UsedPercent.HasValue)
                        .Select(w => w.UsedPercent!.Value).DefaultIfEmpty(-1).Max();
                    if (max >= 0)
                    {
                        items.Add(Text($"{Format.Percent(max)}%", size: "Medium",
                            weight: "Bolder", color: textColor, wrap: false));
                    }
                    foreach (var window in windows)
                    {
                        items.AddRange(DenseWindowItems(window, textColor));
                    }
                }
                else
                {
                    foreach (var window in usage.Windows)
                    {
                        items.AddRange(WindowItems(window, textColor, now, segments: 16)); // Medium 双列
                    }
                }
                break;
        }
        return items.ToArray();
    }

    /// <summary>dense 窗口行：短标题 + 百分比 + 双色块细进度条；无百分比窗口退化为单行文本。</summary>
    private static IEnumerable<object> DenseWindowItems(UsageWindow window, string textColor)
    {
        if (window.UsedPercent is not { } percent)
        {
            yield return Text($"{window.ShortTitle}  {window.UsedText ?? "--"}",
                size: "ExtraSmall", isSubtle: true, color: textColor, wrap: false);
            yield break;
        }
        yield return Text($"{window.ShortTitle}  {Format.Percent(percent)}%",
            size: "ExtraSmall", isSubtle: true, color: textColor, wrap: false);
        yield return BarBlocks(percent, segments: 8); // dense 三列窄栏
    }

    // ---- Large：每个供应商一块，含进度条与重置倒计时 ----

    private static object[] LargeProviderItems(ProviderUsage usage, string textColor, DateTimeOffset now)
    {
        var items = new List<object>
        {
            ProviderHeader(usage, dense: false, textColor, large: true),
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
                        items.AddRange(WindowItems(window, textColor, now, segments: 24)); // Large 整宽
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

    /// <summary>
    /// 一个窗口的渲染行（对齐 UsageCardControl.RenderWindows）：
    /// percent 窗口 → 标题行（窗口名左对齐 + 百分比右对齐，可附带用量文本与倒计时）+ 双色块进度条；
    /// 否则单行「标题 + 绝对值」文本。
    /// </summary>
    private static IEnumerable<object> WindowItems(UsageWindow window, string textColor, DateTimeOffset now, int segments)
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
        var tail = string.IsNullOrEmpty(window.UsedText)
            ? $"{Format.Percent(percent)}%"
            : $"{window.UsedText}   {Format.Percent(percent)}%";
        if (countdown.Length > 0) tail += $"   · {countdown}";

        // 标题行：窗口名（左，次色）+ 百分比/用量/倒计时（右，对齐 macOS 卡片头部）
        yield return new Dictionary<string, object?>
        {
            ["type"] = "ColumnSet",
            ["spacing"] = "None",
            ["columns"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "Column",
                    ["width"] = "stretch",
                    ["items"] = new object[]
                    {
                        Text(window.Title, size: "ExtraSmall", isSubtle: true, color: textColor, wrap: false),
                    },
                },
                new Dictionary<string, object?>
                {
                    ["type"] = "Column",
                    ["width"] = "auto",
                    ["items"] = new object[]
                    {
                        Text(tail, size: "ExtraSmall", weight: "Bolder", color: textColor, wrap: false),
                    },
                },
            },
        };
        yield return BarBlocks(percent, segments);
    }

    /// <summary>
    /// 双色块进度条：填充段「█」按用量级别着色（Good/Warning/Attention），
    /// 轨道段「█」isSubtle 灰，两段紧挨成连续条（Board 不渲染图片，文本块是唯一
    /// 可靠的着色手段；Segoe 全角块字符横向无缝拼接）。
    /// </summary>
    private static object BarBlocks(double percent, int segments)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        var filled = (int)Math.Round(clamped / 100 * segments, MidpointRounding.AwayFromZero);

        var columns = new List<object>();
        if (filled > 0)
        {
            columns.Add(Col("auto",
                [Text(new string('█', filled), size: "ExtraSmall", color: LevelColor(clamped))]));
        }
        if (filled < segments)
        {
            columns.Add(Col("auto",
                [Text(new string('█', segments - filled), size: "ExtraSmall", isSubtle: true)]));
        }
        return new Dictionary<string, object?>
        {
            ["type"] = "ColumnSet",
            ["spacing"] = "Small",
            ["columns"] = columns.ToArray(),
        };
    }

    /// <summary>用量级别色（对齐 UsageCardControl.LevelBrush 语义）：≥80% 红、≥50% 橙、否则绿。</summary>
    public static string LevelColor(double percent) => percent switch
    {
        >= 80 => "Attention",
        >= 50 => "Warning",
        _ => "Good",
    };

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
