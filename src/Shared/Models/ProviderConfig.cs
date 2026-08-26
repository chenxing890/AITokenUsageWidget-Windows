namespace AITokenUsageWidget.Shared.Models;

/// <summary>
/// 单个供应商的配置（主 App 编辑，写入共享 JSON 供小组件 Provider 读取）。
/// 内存中 ApiKey / ExtraToken 为明文；落盘时由 ConfigStore 用 DPAPI 加密（FR-5）。
/// </summary>
public sealed class ProviderConfig
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public ProviderKind Kind { get; init; }

    public bool IsEnabled { get; set; }

    /// <summary>API Key（明文，仅内存中；序列化时加密）。</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>自定义接口根地址，留空使用 Kind.DefaultBaseURL()。</summary>
    public string BaseURLOverride { get; set; } = "";

    /// <summary>自定义显示名，留空使用 Kind.DisplayName()。</summary>
    public string CustomName { get; set; } = "";

    /// <summary>附加凭证：Kimi 的 kimi-auth Cookie（可选，用于查询月度总额）。</summary>
    public string ExtraToken { get; set; } = "";

    public ProviderConfig(ProviderKind kind)
    {
        Kind = kind;
    }

    public bool HasKey => CleanAPIKey.Length > 0;

    /// <summary>去除首尾空白/换行，并剥离误粘贴的 "Bearer " 前缀（与 macOS 版一致）。</summary>
    public string CleanAPIKey
    {
        get
        {
            var trimmed = ApiKey.Trim();
            return trimmed.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? trimmed["Bearer ".Length..].Trim()
                : trimmed;
        }
    }

    public bool HasExtraToken => ExtraToken.Trim().Length > 0;

    public string Name
    {
        get
        {
            var trimmed = CustomName.Trim();
            return trimmed.Length == 0 ? Kind.DisplayName() : trimmed;
        }
    }

    /// <summary>拼接接口地址：base（自定义优先）+ path（path 以 "/" 开头）。</summary>
    public string BuildApiURL(string path)
    {
        var @base = BaseURLOverride.Trim();
        if (@base.Length == 0) @base = Kind.DefaultBaseURL();
        while (@base.EndsWith('/')) @base = @base[..^1];
        return @base + path;
    }
}
