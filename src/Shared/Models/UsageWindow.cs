namespace AITokenUsageWidget.Shared.Models;

/// <summary>一个用量窗口（如「5 小时」「7 天」「本月总额」「30 天累计」）。</summary>
public sealed class UsageWindow
{
    /// <summary>窗口标题（完整显示名，如「5 小时」）。</summary>
    public string Title { get; init; } = "";

    /// <summary>已用百分比（0–100）；无百分比的窗口（如「30 天累计」）为 null。</summary>
    public double? UsedPercent { get; init; }

    /// <summary>用量文本（GLM 月度工具等有绝对值的场景，如 "126/1000 次"）。</summary>
    public string? UsedText { get; init; }

    /// <summary>重置时间（UTC）。</summary>
    public DateTimeOffset? ResetTime { get; init; }

    /// <summary>极简标题（dense 三列布局用）：「5 小时」→"5h" 等（与 macOS 版一致）。</summary>
    public string ShortTitle => Title switch
    {
        "5 小时" => "5h",
        "7 天" => "7d",
        "月度工具" => "工具",
        "本月总额" or "30 天累计" => "30d",
        _ => Title,
    };

    /// <summary>是否 7 天窗口（与 macOS 版 `title.contains("7")` 一致，Kimi「7 天」/ GLM「7 天」均命中）。</summary>
    public bool IsWeekly => Title.Contains('7');

    /// <summary>
    /// 7 天健康配额线位置（0–100），按天的「当天目标配额」（对齐用户预期）：
    /// 窗口起点 = 重置时间 − 7 天，当前处于第几天 day = floor(已过天数) + 1（1..7），
    /// 位置 = day / 7 × 100。第 1 天 14.28%、第 2 天 28.57% … 第 6 天 85.71%，窗口期内始终有线；
    /// 第 7 天（最后一天）封顶 85.71%，不画到 100%（进度条末端无意义）。无重置时间返回 null。
    /// </summary>
    public double? HealthyQuotaPercent(DateTimeOffset now)
    {
        if (ResetTime is not { } reset) return null;
        var start = reset.AddDays(-7);
        if (now <= start) return null;
        var elapsedDays = (now - start).TotalDays;
        // 当前是第几天（1 起）：第 1 天显示当日目标 1/7，第 7 天封顶第 6 天（85.71%）
        var day = Math.Min((int)Math.Floor(elapsedDays) + 1, 6);
        return day / 7.0 * 100.0;
    }
}
