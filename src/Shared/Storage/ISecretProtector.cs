namespace AITokenUsageWidget.Shared.Storage;

/// <summary>
/// 敏感信息（API Key / Cookie）落盘加密器。Windows 上使用 DPAPI（CurrentUser），
/// JSON 中仅保存密文 Base64；内存中即用即清（FR-5 / §6.3）。
/// </summary>
public interface ISecretProtector
{
    /// <summary>加密为可存放于 JSON 的字符串（带 "dpapi:" 前缀）。</summary>
    string Protect(string plain);

    /// <summary>解密；无法解密（含旧版明文）时返回原值，由调用方决定是否接受。</summary>
    string Unprotect(string stored);
}

public static class SecretProtector
{
    /// <summary>平台默认加密器：Windows 用 DPAPI，其余平台（单测）不加密。</summary>
    public static ISecretProtector Default =>
        OperatingSystem.IsWindows() ? new DpapiSecretProtector() : NullSecretProtector.Instance;
}

/// <summary>不加密实现（单元测试 / 非 Windows 环境使用）。</summary>
public sealed class NullSecretProtector : ISecretProtector
{
    public static readonly NullSecretProtector Instance = new();
    private NullSecretProtector() { }
    public string Protect(string plain) => plain;
    public string Unprotect(string stored) => stored;
}
