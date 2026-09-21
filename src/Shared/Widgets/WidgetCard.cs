using System.Text;
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
/// data: URI 图片，但能加载 http(s) 图片；且 Container 的 backgroundImage 会被
/// 拉伸成「首行高度 × 内容宽度」的横带，无法整体铺底。因此每个供应商卡片整体
/// 渲染为一张 PNG（圆角 / padding / 图标 / 进度条全在图内，URL 携带全部渲染
/// 数据的 base64url 载荷，数据变则 URL 变，Board 缓存安全），卡片里只放一个
/// Image 元素；服务不可用时退化为纯文本渲染（emoji 图标 + 双色「█」块进度条）。
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
            var cardWidth = usages.Count >= 3 ? 108 : usages.Count == 2 ? 166 : 340; // 逻辑宽（2x 渲染）
            // macOS 视觉：每个供应商一张独立小卡片
            var columns = usages.Take(3).Select(usage => Col("stretch",
                imageBaseUrl != null
                    ? [ProviderCardImage(usage, dense, now, imageBaseUrl, systemDark, cardWidth, "None")]
                    : [FallbackCard(DenseOrCompactItems(usage, dense, textColor, now), "None")])).ToArray();
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
                // macOS 视觉：每个供应商一张独立卡片（整卡渲染为一张 PNG）
                var spacing = index == 0 ? "None" : "Medium";
                body.Add(imageBaseUrl != null
                    ? ProviderCardImage(usage, dense: false, now, imageBaseUrl, systemDark, 340, spacing)
                    : FallbackCard(LargeProviderItems(usage, textColor, now), spacing));
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
    /// 供应商独立卡片（macOS 卡片感）：整卡渲染为一张 PNG（圆角 / padding / 品牌图标 /
    /// 胶囊进度条全在图内），卡片里只放一个 size=stretch 的 Image 元素。渲染数据以
    /// base64url JSON 载荷放在 URL 里——Board 按 URL 强缓存图片，数据变则 URL 变，
    /// 缓存自动失效。w 为逻辑宽度（服务端 2x 渲染，Board 等比缩放到实际列宽）。
    /// </summary>
    private static object ProviderCardImage(ProviderUsage usage, bool dense, DateTimeOffset now,
        string imageBaseUrl, bool dark, int width, string spacing)
    {
        var payload = new Dictionary<string, object?>
        {
            ["k"] = IconName(usage.Kind),
            ["n"] = usage.DisplayName,
            ["d"] = dark ? 1 : 0,
        };
        switch (usage.State)
        {
            case UsageState.MissingKey:
                payload["msg"] = L10n.Get("missingKey");
                payload["err"] = 0;
                break;
            case UsageState.Error:
                payload["msg"] = usage.ErrorMessage ?? "请求失败";
                payload["err"] = 1;
                break;
            default:
                if (usage.Kind.ShowsBalance())
                {
                    payload["big"] = usage.TotalBalance is { } total
                        ? $"{usage.CurrencySymbol} {Format.Amount(total)}"
                        : "--";
                    if (usage.GrantedBalance is { } granted && usage.ToppedUpBalance is { } toppedUp)
                    {
                        payload["sub"] = L10n.Get("grantedToppedUp",
                            $"{usage.CurrencySymbol}{Format.Amount(granted)}",
                            $"{usage.CurrencySymbol}{Format.Amount(toppedUp)}");
                    }
                }
                else
                {
                    var windows = usage.Windows.Take(dense ? 3 : 4).ToList();
                    if (dense)
                    {
                        var max = windows.Where(w => w.UsedPercent.HasValue)
                            .Select(w => w.UsedPercent!.Value).DefaultIfEmpty(-1).Max();
                        if (max >= 0) payload["max"] = $"{Format.Percent(max)}%";
                    }
                    var rows = new List<object>();
                    foreach (var window in windows)
                    {
                        if (window.UsedPercent is { } percent)
                        {
                            var row = new Dictionary<string, object?>
                            {
                                ["l"] = dense
                                    ? $"{window.ShortTitle}  {Format.Percent(percent)}%"
                                    : window.Title,
                                ["p"] = Math.Round(Math.Clamp(percent, 0, 100), 1),
                            };
                            // 7 天窗口健康配额线（对齐 macOS）：进度条上的灰色刻度线位置
                            if (window.IsWeekly && window.HealthyQuotaPercent(now) is { } marker && marker > 0 && marker < 100)
                                row["m"] = Math.Round(marker, 1);
                            if (!dense)
                            {
                                // 右侧：用量 + 百分比 + 重置倒计时（对齐 macOS 卡片）
                                var tail = string.IsNullOrEmpty(window.UsedText)
                                    ? $"{Format.Percent(percent)}%"
                                    : $"{window.UsedText}   {Format.Percent(percent)}%";
                                var countdown = Format.ResetCountdown(window.ResetTime, now);
                                if (countdown.Length > 0) tail += $"   · {countdown}";
                                row["r"] = tail;
                            }
                            rows.Add(row);
                        }
                        else
                        {
                            // 无百分比窗口（如「30 天累计」）：单行文本，不画进度条
                            rows.Add(new Dictionary<string, object?>
                            {
                                ["l"] = dense
                                    ? $"{window.ShortTitle}  {window.UsedText ?? "--"}"
                                    : $"{window.Title}   {window.UsedText ?? "--"}",
                            });
                        }
                    }
                    payload["rows"] = rows;
                }
                break;
        }

        var payloadJson = JsonSerializer.Serialize(payload, PayloadJsonOptions);
        var d = Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return new Dictionary<string, object?>
        {
            ["type"] = "Image",
            ["url"] = $"{imageBaseUrl}/pcard?w={width}&d={d}",
            ["size"] = "stretch",
            ["altText"] = usage.DisplayName,
            ["horizontalAlignment"] = "Center",
            ["spacing"] = spacing,
        };
    }

    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>无图片服务时的文本兜底卡片：单个 emphasis 容器。</summary>
    private static object FallbackCard(object[] items, string spacing) =>
        new Dictionary<string, object?>
        {
            ["type"] = "Container",
            ["style"] = "emphasis",
            ["spacing"] = spacing,
            ["items"] = items,
        };

    // ---- Medium：1–2 个供应商宽松双列；3 个自动 dense 三列 ----

    /// <summary>图标 + 名称标题行（对齐 macOS 卡片头部），文本兜底用品牌 emoji。</summary>
    private static object ProviderHeader(ProviderUsage usage, bool dense, string textColor,
        bool large = false)
    {
        var nameSize = dense ? "ExtraSmall" : large ? "Medium" : "Small";
        return Text($"{EmojiFor(usage.Kind)} {usage.DisplayName}",
            size: nameSize, weight: "Bolder", color: textColor, wrap: false);
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
                        items.AddRange(DenseWindowItems(window, textColor));
                    }
                }
                else
                {
                    foreach (var window in usage.Windows)
                    {
                        items.AddRange(WindowItems(window, textColor, now, segments: 12)); // Medium 双列
                    }
                }
                break;
        }
        return items.ToArray();
    }

    /// <summary>dense 窗口行：短标题 + 百分比 + 双色块进度条；无百分比窗口退化为单行文本。</summary>
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
        yield return BarBlocks(percent, segments: 8, window.HealthyQuotaPercent(DateTimeOffset.UtcNow)); // dense 三列窄栏
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
                        size: "ExtraLarge", weight: "Default", color: textColor));
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
                        Text(tail, size: "ExtraSmall", weight: "Default", color: textColor, wrap: false),
                    },
                },
            },
        };
        yield return BarBlocks(percent, segments, window.IsWeekly ? window.HealthyQuotaPercent(now) : null);
    }

    /// <summary>
    /// 双色块进度条（无图片服务时的文本兜底）：填充段「█」按用量级别着色
    /// （Good/Warning/Attention），轨道段「█」isSubtle 灰，两段紧挨成连续条
    /// （Segoe 全角块字符横向无缝拼接）。markerPercent 为 7 天窗口健康配额线
    /// 位置（0–100）：把该段块字符替换为「▎」灰色刻度线（对齐 macOS 健康线）。
    /// </summary>
    private static object BarBlocks(double percent, int segments, double? markerPercent = null)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        var filled = (int)Math.Round(clamped / 100 * segments, MidpointRounding.AwayFromZero);

        // 健康配额线：把对应段替换为刻度字符（在填充/轨道拼接处整体替换该段）
        int? markerSeg = null;
        if (markerPercent is { } m && m > 0 && m < 100)
        {
            var idx = (int)Math.Round(m / 100 * segments, MidpointRounding.AwayFromZero);
            markerSeg = Math.Clamp(idx, 0, segments); // 落在 0..segments，等于 filled 时叠在边界
        }

        string FillBlocks(int count)
        {
            if (markerSeg is not { } ms) return new string('█', count);
            var chars = Enumerable.Repeat('█', count).ToArray();
            // 刻度线落在填充段内：替换该位置块
            if (ms >= 0 && ms < count) chars[ms] = '▎';
            return new string(chars);
        }

        var columns = new List<object>();
        if (filled > 0)
        {
            columns.Add(Col("auto",
                [Text(FillBlocks(filled), size: "ExtraSmall", color: LevelColor(clamped))]));
        }
        if (filled < segments)
        {
            var trackCount = segments - filled;
            string track = new string('█', trackCount);
            if (markerSeg is { } ms && ms >= filled && ms < segments)
            {
                var chars = track.ToCharArray();
                chars[ms - filled] = '▎';
                track = new string(chars);
            }
            columns.Add(Col("auto",
                [Text(track, size: "ExtraSmall", isSubtle: true)]));
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
