namespace AITokenUsageWidget.Shared.Models;

public enum UsageState
{
    Ok,
    MissingKey,
    Error,
}

/// <summary>一次用量查询的结果快照。DeepSeek 走余额字段，Kimi / GLM 走窗口数组。</summary>
public sealed class ProviderUsage
{
    public ProviderKind Kind { get; init; }

    public string DisplayName { get; init; } = "";

    public UsageState State { get; init; } = UsageState.Ok;

    /// <summary>State == Error 时的中文友好错误信息。</summary>
    public string? ErrorMessage { get; init; }

    // ---- DeepSeek 余额（ShowsBalance 时使用） ----
    public string? Currency { get; init; }
    public double? TotalBalance { get; init; }
    public double? GrantedBalance { get; init; }
    public double? ToppedUpBalance { get; init; }
    public bool? IsAvailable { get; init; }

    // ---- Kimi / GLM 用量窗口 ----
    public List<UsageWindow> Windows { get; init; } = [];

    /// <summary>抓取时间（UTC）。</summary>
    public DateTimeOffset FetchedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>是否为占位（模拟）数据——仅预览场景使用，卡片显示橙色「模拟」标记。</summary>
    public bool IsSimulated { get; init; }

    /// <summary>是否来自缓存回退（抓取失败时小组件显示缓存并标注时间）。</summary>
    public bool FromCache { get; init; }

    public string CurrencySymbol => Currency == "USD" ? "$" : "¥";

    public string Id => $"{Kind.KindId()}|{DisplayName}";
}
