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
/// 主题：Adaptive Cards 不支持任意十六进制前景色，采用「TextBlock.color 枚举 + 内联纯色
/// 背景图（data URI）」实现显式明暗（浅色 → Dark 文字 + 浅背景图；深色 → Light 文字 + 深背景图；
/// 跟随系统 → Default 文字 + 不加背景，由 Widgets Board 按系统主题渲染）——
/// 避免「白字落白底」（§3.2 / §5.5 平台差异兜底）。
/// </summary>
public static class WidgetCard
{
    // Widgets Board 无法加载卡片内 ms-appx:/// 背景图（白底 + 浅字 = 全白不可读），
    // 改用内联 data URI（64×64 纯色 PNG，与包内 Assets/CardBg*.png 同色）。
    private const string BackgroundLight = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAAZElEQVR42u3QMQ0AAAgDsPk3vBN0EHrUQNN2PosAAQIECBAgQIAAAQIECBAgQIAAAQIECBAgQIAAAQIECBAgQIAAAQIECBAgQIAAAQIECBAgQIAAAQIECBBw3wKzxEOjqBNbBQAAAABJRU5ErkJggg==";
    private const string BackgroundDark = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAAZElEQVR42u3QQREAAAgDoFVZ/5Caw5MHBUjb+SwCBAgQIECAAAECBAgQIECAAAECBAgQIECAAAECBAgQIECAAAECBAgQIECAAAECBAgQIECAAAECBAgQIEDAfQsW9yFLBuocpQAAAABJRU5ErkJggg==";

    // 品牌图标（96×96 圆角渐变 PNG，与主 App ProviderChip 同图形）：
    // Adaptive Card 无矢量图，用内联 data URI 对齐 macOS 卡片的品牌图标（§3.4）。
    private const string IconDeepSeek = "data:image/png;base64,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";
    private const string IconKimi = "data:image/png;base64,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";
    private const string IconGlm = "data:image/png;base64,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";

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
            var columns = usages.Take(3).Select(usage => Col("stretch", DenseOrCompactItems(usage, dense, textColor, now, forcedDark))).ToArray();
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
                    ["items"] = LargeProviderItems(usage, textColor, now, forcedDark),
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

    /// <summary>图标 + 名称（截断）标题行，对齐 macOS 卡片头部（§3.4）。</summary>
    private static object ProviderHeader(ProviderUsage usage, bool dense, string textColor, bool large = false) =>
        new Dictionary<string, object?>
        {
            ["type"] = "ColumnSet",
            ["spacing"] = "None",
            ["columns"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "Column",
                    ["width"] = "auto",
                    ["verticalContentAlignment"] = "Center",
                    ["items"] = new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["type"] = "Image",
                            ["url"] = IconFor(usage.Kind),
                            ["width"] = dense ? "14px" : "18px",
                            ["height"] = dense ? "14px" : "18px",
                        },
                    },
                },
                new Dictionary<string, object?>
                {
                    ["type"] = "Column",
                    ["width"] = "stretch",
                    ["spacing"] = "Small",
                    ["verticalContentAlignment"] = "Center",
                    ["items"] = new object[]
                    {
                        Text(usage.DisplayName,
                            size: dense ? "ExtraSmall" : large ? "Medium" : "Small",
                            weight: "Bolder", color: textColor, wrap: false),
                    },
                },
            },
        };

    private static string IconFor(ProviderKind kind) => kind switch
    {
        ProviderKind.DeepSeek => IconDeepSeek,
        ProviderKind.Kimi => IconKimi,
        _ => IconGlm,
    };

    private static object[] DenseOrCompactItems(ProviderUsage usage, bool dense, string textColor, DateTimeOffset now, bool dark)
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
                        items.AddRange(DenseWindowItems(window, textColor, dark));
                    }
                }
                else
                {
                    foreach (var window in usage.Windows)
                    {
                        items.AddRange(WindowItems(window, textColor, now, dark));
                    }
                }
                break;
        }
        return items.ToArray();
    }

    /// <summary>dense 窗口行：短标题 + 百分比 + 3px 胶囊进度条；无百分比窗口退化为单行文本。</summary>
    private static IEnumerable<object> DenseWindowItems(UsageWindow window, string textColor, bool dark)
    {
        if (window.UsedPercent is not { } percent)
        {
            yield return Text($"{window.ShortTitle}  {window.UsedText ?? "--"}",
                size: "ExtraSmall", isSubtle: true, color: textColor, wrap: false);
            yield break;
        }
        yield return Text($"{window.ShortTitle}  {Format.Percent(percent)}%",
            size: "ExtraSmall", isSubtle: true, color: textColor, wrap: false);
        yield return BarImage(percent, dark, displayHeight: 3);
    }

    // ---- Large：每个供应商一块，含进度条与重置倒计时 ----

    private static object[] LargeProviderItems(ProviderUsage usage, string textColor, DateTimeOffset now, bool dark)
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
                        items.AddRange(WindowItems(window, textColor, now, dark));
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
    /// percent 窗口 → 标题行（窗口名左对齐 + 百分比右对齐，可附带用量文本与倒计时）+ 4px 胶囊进度条；
    /// 否则单行「标题 + 绝对值」文本。
    /// </summary>
    private static IEnumerable<object> WindowItems(UsageWindow window, string textColor, DateTimeOffset now, bool dark)
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
        yield return BarImage(percent, dark, displayHeight: 4);
    }

    /// <summary>
    /// macOS 风格胶囊进度条：运行时生成 PNG data URI（2x 高清，SDF 抗锯齿），
    /// Image.size=Stretch 拉满列宽。填充色按用量级别（≥80 红 / ≥50 橙 / 其余绿），
    /// 轨道色随明暗主题（对齐 UsageCardControl.ProgressTrack / TrackBrush）。
    /// </summary>
    private static object BarImage(double percent, bool dark, int displayHeight) =>
        new Dictionary<string, object?>
        {
            ["type"] = "Image",
            ["url"] = PngBar.DataUri(480, displayHeight * 2, percent,
                LevelRgb(percent), dark ? new PngBar.Rgb(60, 60, 60) : new PngBar.Rgb(229, 229, 229)),
            ["height"] = $"{displayHeight}px",
            ["size"] = "Stretch",
            ["spacing"] = "Small",
        };

    /// <summary>用量级别色（与 UsageCardControl.LevelBrush 同色板）：≥80% 红、≥50% 橙、否则绿。</summary>
    public static PngBar.Rgb LevelRgb(double percent) => percent switch
    {
        >= 80 => new PngBar.Rgb(255, 69, 0),    // OrangeRed
        >= 50 => new PngBar.Rgb(255, 140, 0),   // DarkOrange
        _ => new PngBar.Rgb(60, 179, 113),      // MediumSeaGreen
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
