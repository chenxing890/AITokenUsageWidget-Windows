namespace AITokenUsageWidget.Shared.Models;

/// <summary>
/// shared.json 的单文件负载：主 App 与小组件 Provider 共享的全部状态（FR-5）。
/// </summary>
public sealed class SharedData
{
    /// <summary>全部供应商配置（含未启用的）。</summary>
    public List<ProviderConfig> Configs { get; set; } = [];

    /// <summary>最近一次成功抓取的用量缓存（Widget 刷新失败时回退显示）。</summary>
    public List<ProviderUsage> CachedUsages { get; set; } = [];

    public ThemePreference Theme { get; set; } = ThemePreference.System;

    /// <summary>已通知过的「供应商-窗口」key 集合（回落重置后移除）。</summary>
    public List<string> AlertedKeys { get; set; } = [];

    public bool AlertsEnabled { get; set; } = true;

    /// <summary>告警阈值百分比（默认 80）。</summary>
    public double AlertThreshold { get; set; } = 80;

    public static SharedData Empty() => new();
}
