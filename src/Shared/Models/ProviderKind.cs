namespace AITokenUsageWidget.Shared.Models;

/// <summary>供应商种类。新增供应商时在此扩展枚举并补齐各属性（FR-1 / §6.5 三步扩展法第一步）。</summary>
public enum ProviderKind
{
    DeepSeek,
    Kimi,
    Glm,
}

public static class ProviderKindExtensions
{
    public static readonly ProviderKind[] All =
        [ProviderKind.DeepSeek, ProviderKind.Kimi, ProviderKind.Glm];

    /// <summary>存储 / 告警 key 中使用的字符串标识（与 macOS 版一致）。</summary>
    public static string KindId(this ProviderKind kind) => kind switch
    {
        ProviderKind.DeepSeek => "deepseek",
        ProviderKind.Kimi => "kimi",
        ProviderKind.Glm => "glm",
        _ => "deepseek",
    };

    public static ProviderKind? FromKindId(string? value)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "deepseek": return ProviderKind.DeepSeek;
            case "kimi": return ProviderKind.Kimi;
            case "glm": return ProviderKind.Glm;
            default: return null;
        }
    }

    /// <summary>默认显示名（用户可在配置中自定义）。</summary>
    public static string DisplayName(this ProviderKind kind) => kind switch
    {
        ProviderKind.DeepSeek => "DeepSeek",
        ProviderKind.Kimi => "Kimi Code",
        ProviderKind.Glm => "GLM Coding",
        _ => kind.ToString(),
    };

    /// <summary>副标题（侧边栏 / 详情页一行说明）。</summary>
    public static string Subtitle(this ProviderKind kind) => kind switch
    {
        ProviderKind.DeepSeek => "账户余额监控",
        ProviderKind.Kimi => "5 小时 · 7 天 · 月度额度",
        ProviderKind.Glm => "5 小时 · 7 天 · 月度工具",
        _ => "",
    };

    /// <summary>默认接口根地址（自定义地址留空时使用）。</summary>
    public static string DefaultBaseURL(this ProviderKind kind) => kind switch
    {
        ProviderKind.DeepSeek => "https://api.deepseek.com",
        ProviderKind.Kimi => "https://api.kimi.com/coding/v1",
        ProviderKind.Glm => "https://open.bigmodel.cn",
        _ => "",
    };

    /// <summary>品牌色（与 macOS 版一致，进度条 / 图标底 / 强调色）。</summary>
    public static string BrandHex(this ProviderKind kind) => kind switch
    {
        ProviderKind.DeepSeek => "#3B82F5", // RGB 0.23/0.51/0.96
        ProviderKind.Kimi => "#8C5CF5",     // RGB 0.55/0.36/0.96
        ProviderKind.Glm => "#059E87",      // RGB 0.02/0.62/0.53
        _ => "#3B82F5",
    };

    /// <summary>API Key 格式提示。</summary>
    public static string ApiKeyHint(this ProviderKind kind) => kind switch
    {
        ProviderKind.DeepSeek => "sk-xxx",
        ProviderKind.Kimi => "sk-kimi-xxx",
        ProviderKind.Glm => "与调用 /api/paas/v4 同一把",
        _ => "",
    };

    /// <summary>控制台地址（配置界面「打开控制台」按钮）。</summary>
    public static string ConsoleURL(this ProviderKind kind) => kind switch
    {
        ProviderKind.DeepSeek => "https://platform.deepseek.com/api_keys",
        ProviderKind.Kimi => "https://www.kimi.com/code/console",
        ProviderKind.Glm => "https://open.bigmodel.cn/apikey",
        _ => "",
    };

    /// <summary>是否支持附加凭证（当前仅 Kimi：kimi-auth Cookie 用于月度总额）。</summary>
    public static bool SupportsCookie(this ProviderKind kind) => kind == ProviderKind.Kimi;

    /// <summary>是否为余额型供应商（卡片按余额渲染，而非用量窗口；不参与百分比告警）。</summary>
    public static bool ShowsBalance(this ProviderKind kind) => kind == ProviderKind.DeepSeek;
}
