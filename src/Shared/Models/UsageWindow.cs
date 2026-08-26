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
}
