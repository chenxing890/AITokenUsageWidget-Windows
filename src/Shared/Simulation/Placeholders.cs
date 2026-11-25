using AITokenUsageWidget.Shared.Models;

namespace AITokenUsageWidget.Shared.Simulation;

/// <summary>占位（模拟）数据（附录 A）：预览与小组件空态使用，与 macOS 版一致。</summary>
public static class Placeholders
{
    /// <summary>按 kind 生成占位；同 kind 多实例时传 configId / name 以区分身份与显示名。</summary>
    public static ProviderUsage For(ProviderKind kind, Guid? configId = null, string? name = null) => kind switch
    {
        ProviderKind.DeepSeek => DeepSeek(configId, name),
        ProviderKind.Kimi => Kimi(configId, name),
        ProviderKind.Glm => Glm(configId, name),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>DeepSeek：CNY，总余额 ¥88.50（赠送 10.00 / 充值 78.50），可用。</summary>
    public static ProviderUsage DeepSeek(Guid? configId = null, string? name = null) => new()
    {
        Kind = ProviderKind.DeepSeek,
        ConfigId = configId,
        DisplayName = name is { Length: > 0 } ? name : ProviderKind.DeepSeek.DisplayName(),
        State = UsageState.Ok,
        Currency = "CNY",
        TotalBalance = 88.50,
        GrantedBalance = 10.00,
        ToppedUpBalance = 78.50,
        IsAvailable = true,
        Windows = [],
        FetchedAt = DateTimeOffset.UtcNow,
        IsSimulated = true,
    };

    /// <summary>Kimi：5h 42%（2 小时后重置）、7d 68%（3 天后重置）、本月 35%（12 天后重置）。</summary>
    public static ProviderUsage Kimi(Guid? configId = null, string? name = null) => new()
    {
        Kind = ProviderKind.Kimi,
        ConfigId = configId,
        DisplayName = name is { Length: > 0 } ? name : ProviderKind.Kimi.DisplayName(),
        State = UsageState.Ok,
        Windows =
        [
            new UsageWindow { Title = "5 小时", UsedPercent = 42, ResetTime = DateTimeOffset.UtcNow.AddHours(2) },
            new UsageWindow { Title = "7 天", UsedPercent = 68, ResetTime = DateTimeOffset.UtcNow.AddDays(3) },
            new UsageWindow { Title = "本月总额", UsedPercent = 35, ResetTime = DateTimeOffset.UtcNow.AddDays(12) },
        ],
        FetchedAt = DateTimeOffset.UtcNow,
        IsSimulated = true,
    };

    /// <summary>GLM：5h 21%（1 小时后重置）、7d 55%（4 天后重置）、月度工具 12%「126/1000 次」（18 天后重置）。</summary>
    public static ProviderUsage Glm(Guid? configId = null, string? name = null) => new()
    {
        Kind = ProviderKind.Glm,
        ConfigId = configId,
        DisplayName = name is { Length: > 0 } ? name : ProviderKind.Glm.DisplayName(),
        State = UsageState.Ok,
        Windows =
        [
            new UsageWindow { Title = "5 小时", UsedPercent = 21, ResetTime = DateTimeOffset.UtcNow.AddHours(1) },
            new UsageWindow { Title = "7 天", UsedPercent = 55, ResetTime = DateTimeOffset.UtcNow.AddDays(4) },
            new UsageWindow
            {
                Title = "月度工具",
                UsedPercent = 12,
                UsedText = "126/1000 次",
                ResetTime = DateTimeOffset.UtcNow.AddDays(18),
            },
        ],
        FetchedAt = DateTimeOffset.UtcNow,
        IsSimulated = true,
    };

    public static List<ProviderUsage> All() => [DeepSeek(), Kimi(), Glm()];
}
