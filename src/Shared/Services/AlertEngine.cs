using AITokenUsageWidget.Shared.Models;

namespace AITokenUsageWidget.Shared.Services;

/// <summary>一条待发送的用量告警。</summary>
public sealed record UsageAlert(string Key, ProviderKind Kind, string Title, string Body);

/// <summary>
/// 用量告警判定引擎（FR-4 / 附录 B 伪代码）：
/// - 任一百分比窗口 ≥ 阈值 → 通知一次（同一「供应商-窗口」key 防重复）
/// - 回落到「阈值 − 10%」以下后清除已通知标记，下次冲高可再次提醒
/// - 余额型供应商（DeepSeek）无百分比窗口，天然不参与
/// 纯函数实现，便于单元测试；Toast 发送由调用方完成。
/// </summary>
public static class AlertEngine
{
    public static readonly double[] ThresholdOptions = [50, 60, 70, 80, 90, 95];
    public const double DefaultThreshold = 80;
    public const double ResetMargin = 10;

    public sealed record Result(IReadOnlyList<UsageAlert> Alerts, IReadOnlyList<string> AlertedKeys, bool Changed);

    /// <summary>评估一批用量快照，返回需要发送的告警与新的已通知 key 集合。</summary>
    public static Result Evaluate(
        IEnumerable<ProviderUsage> usages,
        bool alertsEnabled,
        double threshold,
        IEnumerable<string> alertedKeys)
    {
        var alerted = new HashSet<string>(alertedKeys);
        if (!alertsEnabled)
            return new Result(Array.Empty<UsageAlert>(), alerted.ToList(), Changed: false);

        var resetBelow = threshold - ResetMargin;
        var alerts = new List<UsageAlert>();
        var changed = false;

        foreach (var usage in usages)
        {
            if (usage.State != UsageState.Ok) continue;
            foreach (var window in usage.Windows)
            {
                if (window.UsedPercent is not { } percent) continue;
                var key = $"{usage.Kind.KindId()}-{window.Title}";

                if (percent >= threshold)
                {
                    if (alerted.Contains(key)) continue; // 已通知过，防重复
                    alerted.Add(key);
                    changed = true;
                    alerts.Add(new UsageAlert(
                        Key: key,
                        Kind: usage.Kind,
                        Title: $"{usage.DisplayName} 用量告警",
                        Body: $"{window.Title}已使用 {Format.Percent(percent)}%，达到告警阈值 {Format.Percent(threshold)}%。"));
                }
                else if (percent < resetBelow && alerted.Remove(key))
                {
                    changed = true; // 回落重置
                }
            }
        }
        return new Result(alerts, alerted.ToList(), changed);
    }
}
