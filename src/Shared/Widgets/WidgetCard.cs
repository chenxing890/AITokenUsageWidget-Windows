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
/// data: URI 图片，但能加载 http(s) 图片。因此图片（胶囊进度条 / 品牌图标）由
/// Provider 进程内嵌的 127.0.0.1 图片服务提供（imageBaseUrl 参数）；服务不可用
/// 时退化为纯文本渲染（emoji 图标 + 双色「█」块进度条）。
/// 文字颜色始终 Default，由 Board 按系统主题渲染（强制 Light/Dark 文字在
/// 背景不可控时会对比度失控）。
/// </summary>
public static class WidgetCard
{
    public static string Build(
        WidgetSize size,
        IReadOnlyList<ProviderUsage> usages,
        ThemePreference theme,
        bool systemDark,
        DateTimeOffset now,
        string? imageBaseUrl = null)
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
            // macOS 视觉：每个供应商一张独立小卡片（逐行背景堆叠）
            var columns = usages.Take(3).Select(usage => Col("stretch",
                ProviderCardRows(DenseOrCompactItems(usage, dense, textColor, now, imageBaseUrl, systemDark),
                    first: true, imageBaseUrl, systemDark))).ToArray();
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
                // macOS 视觉：每个供应商一张独立卡片（逐行背景堆叠成卡）
                body.AddRange(ProviderCardRows(
                    LargeProviderItems(usage, textColor, now, imageBaseUrl, systemDark),
                    first: index == 0, imageBaseUrl, systemDark));
            }
        }

        // macOS 视觉：底部更新时间（右对齐、次色小字）
        if (usages.Count > 0)
        {
            var latest = usages.Select(u => u.FetchedAt).Max().ToLocalTime();
            body.Add(Text(L10n.Get("updatedAt", latest.ToString("HH:mm")),
                size: "ExtraSmall", isSubtle: true, color: textColor,
                horizontalAlignment: "Right", spacing: "Medium"));
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

    /// <summary>
    /// 供应商独立卡片（macOS 卡片感）。实测 Board 渲染器把 Container 的
    /// backgroundImage 拉伸成「首行高度 × 内容宽度」的横带（与图片固有尺寸无关），
    /// 无法整体铺底——因此每个内容行一个独立容器（各自带背景横带），行间
    /// spacing=None 无缝堆叠成完整卡片；首行/末行各垫一个空行（圆角顶/底段）
    /// 作为卡片上下边距。无图片服务时退化为单个 emphasis 容器。
    /// </summary>
    private static object[] ProviderCardRows(object[] items, bool first, string? imageBaseUrl,
        bool dark)
    {
        if (imageBaseUrl == null)
        {
            return
            [
                new Dictionary<string, object?>
                {
                    ["type"] = "Container",
                    ["style"] = "emphasis",
                    ["spacing"] = first ? "None" : "Medium",
                    ["items"] = items,
                },
            ];
        }

        var rows = new List<object>
        {
            CardRow("top", Text(" ", size: "ExtraSmall"), first ? "None" : "Medium",
                imageBaseUrl, dark),
        };
        foreach (var item in items)
        {
            rows.Add(CardRow("mid", item, "None", imageBaseUrl, dark));
        }
        rows.Add(CardRow("bottom", Text(" ", size: "ExtraSmall"), "None", imageBaseUrl, dark));
        return rows.ToArray();
    }

    /// <summary>卡片的一行：独立容器 + 对应分段（顶/中/底）背景横带。</summary>
    private static object CardRow(string seg, object content, string spacing,
        string imageBaseUrl, bool dark) =>
        new Dictionary<string, object?>
        {
            ["type"] = "Container",
            ["spacing"] = spacing,
            ["backgroundImage"] = new Dictionary<string, object?>
            {
                ["url"] = $"{imageBaseUrl}/cardbg?dark={(dark ? 1 : 0)}&seg={seg}&v=4",
                ["fillMode"] = "Stretch",
            },
            ["items"] = new[] { content },
        };

    // ---- Medium：1–2 个供应商宽松双列；3 个自动 dense 三列 ----

    /// <summary>
    /// 图标 + 名称标题行（对齐 macOS 卡片头部）。有图片服务时用真实品牌图标
    /// （固有尺寸 == 声明尺寸，杜绝裁剪）；无服务时退化为 emoji 文本。
    /// </summary>
    private static object ProviderHeader(ProviderUsage usage, bool dense, string textColor,
        bool large = false, string? imageBaseUrl = null)
    {
        var nameSize = dense ? "ExtraSmall" : large ? "Medium" : "Small";
        if (imageBaseUrl == null)
        {
            return Text($"{EmojiFor(usage.Kind)} {usage.DisplayName}",
                size: nameSize, weight: "Bolder", color: textColor, wrap: false);
        }

        var iconSize = dense ? 14 : 18;
        return new Dictionary<string, object?>
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
                            ["url"] = $"{imageBaseUrl}/icon/{IconName(usage.Kind)}?s={iconSize}&v=2",
                            ["width"] = $"{iconSize}px",
                            ["height"] = $"{iconSize}px",
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
                        Text(usage.DisplayName, size: nameSize,
                            weight: "Default", color: textColor, wrap: false),
                    },
                },
            },
        };
    }

    private static string IconName(ProviderKind kind) => kind switch
    {
        ProviderKind.DeepSeek => "deepseek",
        ProviderKind.Kimi => "kimi",
        _ => "glm",
    };

    /// <summary>品牌 emoji（对齐 macOS 品牌图标的位置语义）。</summary>
    public static string EmojiFor(ProviderKind kind) => kind switch
    {
        ProviderKind.DeepSeek => "🐋",
        ProviderKind.Kimi => "🌙",
        _ => "🤖",
    };

    private static object[] DenseOrCompactItems(ProviderUsage usage, bool dense, string textColor, DateTimeOffset now, string? imageBaseUrl, bool dark)
    {
        var items = new List<object>
        {
            ProviderHeader(usage, dense, textColor, imageBaseUrl: imageBaseUrl),
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
                        size: dense ? "Medium" : "ExtraLarge", weight: "Default", color: textColor, wrap: false));
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
                        items.AddRange(DenseWindowItems(window, textColor, imageBaseUrl, dark));
                    }
                }
                else
                {
                    foreach (var window in usage.Windows)
                    {
                        items.AddRange(WindowItems(window, textColor, now, barWidth: 140, imageBaseUrl, dark)); // Medium 双列
                    }
                }
                break;
        }
        return items.ToArray();
    }

    /// <summary>dense 窗口行：短标题 + 百分比 + 胶囊进度条（无服务时双色块）；无百分比窗口退化为单行文本。</summary>
    private static IEnumerable<object> DenseWindowItems(UsageWindow window, string textColor, string? imageBaseUrl, bool dark)
    {
        if (window.UsedPercent is not { } percent)
        {
            yield return Text($"{window.ShortTitle}  {window.UsedText ?? "--"}",
                size: "ExtraSmall", isSubtle: true, color: textColor, wrap: false);
            yield break;
        }
        yield return Text($"{window.ShortTitle}  {Format.Percent(percent)}%",
            size: "ExtraSmall", isSubtle: true, color: textColor, wrap: false);
        yield return ProgressBar(percent, width: 84, height: 3, segments: 8, imageBaseUrl, dark); // dense 三列窄栏
    }

    // ---- Large：每个供应商一块，含进度条与重置倒计时 ----

    private static object[] LargeProviderItems(ProviderUsage usage, string textColor, DateTimeOffset now, string? imageBaseUrl, bool dark)
    {
        var items = new List<object>
        {
            ProviderHeader(usage, dense: false, textColor, large: true, imageBaseUrl: imageBaseUrl),
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
                        size: "ExtraLarge", weight: "Default", color: textColor));
                    items.Add(BalanceDetail(usage, textColor));
                }
                else
                {
                    foreach (var window in usage.Windows)
                    {
                        items.AddRange(WindowItems(window, textColor, now, barWidth: 480, imageBaseUrl, dark)); // Large 整宽
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
    private static IEnumerable<object> WindowItems(UsageWindow window, string textColor, DateTimeOffset now, int barWidth, string? imageBaseUrl, bool dark)
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
                        Text(tail, size: "ExtraSmall", weight: "Default", color: textColor, wrap: false),
                    },
                },
            },
        };
        yield return ProgressBar(percent, barWidth, height: 4, segments: 24, imageBaseUrl, dark);
    }

    /// <summary>
    /// 进度条：有图片服务时用 macOS 风格胶囊条 PNG（127.0.0.1 本地服务，固有尺寸 ==
    /// 声明尺寸，与 UsageCardControl.ProgressTrack 同 4px 圆角轨道 + 级别色填充）；
    /// 服务不可用时退化为双色「█」块。
    /// </summary>
    private static object ProgressBar(double percent, int width, int height, int segments,
        string? imageBaseUrl, bool dark)
    {
        if (imageBaseUrl != null)
        {
            var p = Math.Round(Math.Clamp(percent, 0, 100), 1)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
            return new Dictionary<string, object?>
            {
                ["type"] = "Image",
                ["url"] = $"{imageBaseUrl}/bar?p={p}&dark={(dark ? 1 : 0)}&w={width}&h={height}",
                ["width"] = $"{width}px",
                ["height"] = $"{height}px",
                ["horizontalAlignment"] = "Left",
                ["spacing"] = "Small",
            };
        }
        return BarBlocks(percent, segments);
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
