using AITokenUsageWidget.Shared.Models;
using AITokenUsageWidget.Shared.Services;
using Xunit;

namespace AITokenUsageWidget.Shared.Tests;

public class AlertEngineTests
{
    private static ProviderUsage Usage(ProviderKind kind, params (string Title, double? Percent)[] windows) => new()
    {
        Kind = kind,
        DisplayName = kind.DisplayName(),
        State = UsageState.Ok,
        Windows = windows.Select(w => new UsageWindow { Title = w.Title, UsedPercent = w.Percent }).ToList(),
    };

    [Fact]
    public void CrossingThreshold_AlertsOnce()
    {
        var result = AlertEngine.Evaluate(
            [Usage(ProviderKind.Kimi, ("5 小时", 81.0))],
            alertsEnabled: true, threshold: 80, alertedKeys: []);

        var alert = Assert.Single(result.Alerts);
        Assert.Equal("kimi-5 小时", alert.Key);
        Assert.Equal("Kimi Code 用量告警", alert.Title);
        Assert.Equal("5 小时已使用 81%，达到告警阈值 80%。", alert.Body);
        Assert.Contains("kimi-5 小时", result.AlertedKeys);
    }

    [Fact]
    public void ExactlyAtThreshold_Alerts()
    {
        var result = AlertEngine.Evaluate(
            [Usage(ProviderKind.Glm, ("7 天", 80.0))], true, 80, []);
        Assert.Single(result.Alerts);
    }

    [Fact]
    public void SecondCrossing_DoesNotRepeat()
    {
        var first = AlertEngine.Evaluate(
            [Usage(ProviderKind.Kimi, ("5 小时", 85.0))], true, 80, []);
        var second = AlertEngine.Evaluate(
            [Usage(ProviderKind.Kimi, ("5 小时", 95.0))], true, 80, first.AlertedKeys);

        Assert.Empty(second.Alerts); // 防重复
        Assert.Single(second.AlertedKeys);
    }

    [Fact]
    public void FallbackBelowThresholdMinus10_ResetsKey()
    {
        // 仍高于阈值-10（70）：不重置
        var still = AlertEngine.Evaluate(
            [Usage(ProviderKind.Kimi, ("5 小时", 72.0))], true, 80, ["kimi-5 小时"]);
        Assert.Contains("kimi-5 小时", still.AlertedKeys);

        // 恰好等于阈值-10（70）：不重置（需严格低于）
        var boundary = AlertEngine.Evaluate(
            [Usage(ProviderKind.Kimi, ("5 小时", 70.0))], true, 80, ["kimi-5 小时"]);
        Assert.Contains("kimi-5 小时", boundary.AlertedKeys);

        // 回落到 69.9：重置，下次冲高可再次提醒
        var reset = AlertEngine.Evaluate(
            [Usage(ProviderKind.Kimi, ("5 小时", 69.9))], true, 80, ["kimi-5 小时"]);
        Assert.DoesNotContain("kimi-5 小时", reset.AlertedKeys);

        var again = AlertEngine.Evaluate(
            [Usage(ProviderKind.Kimi, ("5 小时", 90.0))], true, 80, reset.AlertedKeys);
        Assert.Single(again.Alerts);
    }

    [Fact]
    public void ErrorAndMissingKeyUsages_Skipped()
    {
        var errored = new ProviderUsage
        {
            Kind = ProviderKind.Kimi,
            DisplayName = "Kimi Code",
            State = UsageState.Error,
            ErrorMessage = "x",
            Windows = [new UsageWindow { Title = "5 小时", UsedPercent = 99 }],
        };
        var result = AlertEngine.Evaluate([errored], true, 80, []);
        Assert.Empty(result.Alerts);
    }

    [Fact]
    public void BalanceProvider_HasNoPercentWindows_NoAlerts()
    {
        var deep = new ProviderUsage
        {
            Kind = ProviderKind.DeepSeek,
            DisplayName = "DeepSeek",
            State = UsageState.Ok,
            TotalBalance = 0.5,
        };
        Assert.Empty(AlertEngine.Evaluate([deep], true, 80, []).Alerts);
    }

    [Fact]
    public void Disabled_NoAlertsAndKeysUntouched()
    {
        var result = AlertEngine.Evaluate(
            [Usage(ProviderKind.Kimi, ("5 小时", 99.0))], false, 80, ["glm-7 天"]);
        Assert.Empty(result.Alerts);
        Assert.Equal(["glm-7 天"], result.AlertedKeys);
    }

    [Fact]
    public void MultipleWindowsAndProviders_IndependentKeys()
    {
        var usages = new[]
        {
            Usage(ProviderKind.Kimi, ("5 小时", 82.0), ("7 天", 91.0)),
            Usage(ProviderKind.Glm, ("7 天", 55.0), ("月度工具", 100.0)),
        };
        var result = AlertEngine.Evaluate(usages, true, 80, []);

        Assert.Equal(3, result.Alerts.Count);
        Assert.Equal(["kimi-5 小时", "kimi-7 天", "glm-月度工具"], result.Alerts.Select(a => a.Key).ToList());
    }

    // ---- 同 kind 多实例（对齐 macOS 9d0a8bb：告警 key 改用 configID）----

    [Fact]
    public void SameKindInstances_IndependentAlertKeys()
    {
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        ProviderUsage Instance(Guid id, double percent) => new()
        {
            Kind = ProviderKind.Kimi,
            ConfigId = id,
            DisplayName = id == idA ? "Kimi 主力" : "Kimi 备用",
            State = UsageState.Ok,
            Windows = [new UsageWindow { Title = "5 小时", UsedPercent = percent }],
        };

        var result = AlertEngine.Evaluate(
            [Instance(idA, 82.0), Instance(idB, 82.0)], true, 80, []);

        Assert.Equal(2, result.Alerts.Count); // 同窗口各自告警，互不吞并
        Assert.Equal([$"{idA}-5 小时", $"{idB}-5 小时"], result.Alerts.Select(a => a.Key));
        Assert.Equal([$"{idA}-5 小时", $"{idB}-5 小时"], result.AlertedKeys);
    }

    [Fact]
    public void ConfigIdMissing_FallsBackToKindKey()
    {
        // 旧缓存/占位无 ConfigId：回退 kind key，与历史 alertedKeys 兼容
        var result = AlertEngine.Evaluate(
            [Usage(ProviderKind.Kimi, ("5 小时", 82.0))], true, 80, ["kimi-5 小时"]);
        Assert.Empty(result.Alerts); // 已记录过，不重复
        Assert.Equal(["kimi-5 小时"], result.AlertedKeys);
    }
}
