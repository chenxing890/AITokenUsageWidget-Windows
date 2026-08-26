using System.Globalization;

namespace AITokenUsageWidget.Shared;

/// <summary>
/// 轻量本地化：zh-CN 首发，en-US 资源预留（FR-1 / §6.4）。
/// 小组件卡片文案全部经此处获取，不在模板内硬编码。
/// </summary>
public static class L10n
{
    private static readonly Dictionary<string, string> Zh = new()
    {
        ["widgetTitle"] = "AI 模型用量",
        ["widgetSubtitle"] = "DeepSeek 余额与 Kimi / GLM 额度",
        ["refresh"] = "刷新",
        ["emptyHint1"] = "打开「AI 模型用量」应用",
        ["emptyHint2"] = "启用并配置供应商",
        ["updatedAt"] = "更新于 {0}",
        ["justNow"] = "刚刚",
        ["minutesAgo"] = "{0} 分钟前",
        ["cacheFallback"] = "更新失败，显示缓存数据（{0}）",
        ["resetSoon"] = "即将重置",
        ["resetInMinutes"] = "{0} 分钟后重置",
        ["resetInHours"] = "{0} 小时后重置",
        ["resetInDays"] = "{0} 天后重置",
        ["grantedToppedUp"] = "赠送 {0} · 充值 {1}",
        ["missingKey"] = "未配置 API Key",
        ["simulated"] = "模拟",
        ["alertTitle"] = "{0} 用量告警",
        ["alertBody"] = "{1}已使用 {2}%，达到告警阈值 {3}%。",
    };

    private static readonly Dictionary<string, string> En = new()
    {
        ["widgetTitle"] = "AI Usage",
        ["widgetSubtitle"] = "DeepSeek balance & Kimi / GLM quotas",
        ["refresh"] = "Refresh",
        ["emptyHint1"] = "Open the AI Usage app to",
        ["emptyHint2"] = "enable and configure providers",
        ["updatedAt"] = "Updated {0}",
        ["justNow"] = "just now",
        ["minutesAgo"] = "{0} min ago",
        ["cacheFallback"] = "Update failed, showing cache ({0})",
        ["resetSoon"] = "resetting soon",
        ["resetInMinutes"] = "resets in {0}m",
        ["resetInHours"] = "resets in {0}h",
        ["resetInDays"] = "resets in {0}d",
        ["grantedToppedUp"] = "Granted {0} · Topped up {1}",
        ["missingKey"] = "API Key not configured",
        ["simulated"] = "SIM",
        ["alertTitle"] = "{0} usage alert",
        ["alertBody"] = "{1} at {2}%, reached threshold {3}%.",
    };

    private static volatile string _language = "zh";

    /// <summary>"zh" | "en"。App 启动时按系统语言设置一次。</summary>
    public static string Language
    {
        get => _language;
        set => _language = value == "en" ? "en" : "zh";
    }

    public static void UseSystemLanguage()
    {
        var name = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        Language = name.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en" : "zh";
    }

    public static string Get(string key, params string[] args)
    {
        var table = _language == "en" ? En : Zh;
        if (!table.TryGetValue(key, out var template)) template = Zh.GetValueOrDefault(key, key);
        return args.Length > 0 ? string.Format(CultureInfo.InvariantCulture, template, args) : template;
    }
}
