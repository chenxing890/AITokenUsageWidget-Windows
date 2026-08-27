using System.Runtime.InteropServices;

namespace AITokenUsageWidget.Shared.Storage;

/// <summary>
/// DPAPI（ProtectedData）加密器。密文带 "dpapi:" 前缀存放；
/// 无前缀的值视为旧版明文原样返回（升级容错）。
/// 仅在 Windows 上可用（P/Invoke crypt32.dll）。
///
/// 用 CRYPTPROTECT_LOCAL_MACHINE 加密：小组件 Provider 由 Widgets 基础设施拉起，
/// 其进程上下文无法访问 CurrentUser 主密钥（实测 CryptUnprotectData 失败）——
/// 而 LocalMachine 范围的密文任何本机进程都可解。Unprotect 对两种范围均可解。
/// UI_FORBIDDEN：后台进程（无桌面）绝不能弹 UI，否则调用会挂起。
/// </summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    private const string Prefix = "dpapi:";
    private const int CryptProtectUiForbidden = 0x1;
    private const int CryptProtectLocalMachine = 0x4;

    public string Protect(string plain)
    {
        if (plain.Length == 0) return "";
        // 已是密文（例如 Provider 进程解密失败原样回传）时原样透传，避免二次加密损坏
        if (plain.StartsWith(Prefix, StringComparison.Ordinal)) return plain;
        return TryCryptProtect(plain, out var stored) ? stored : plain; // 极端失败退化为明文，避免丢配置
    }

    public string Unprotect(string stored)
    {
        if (stored.Length == 0 || !stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored;
        // 解密失败（如 Provider 进程无法访问旧 CurrentUser 密文）必须返回原值：
        // 上层读-改-写会把它原样写回，配置因此得以保留；返回 "" 会直接抹掉用户的 Key
        return TryCryptUnprotect(stored[Prefix.Length..], out var plain) ? plain : stored;
    }

    private static bool TryCryptProtect(string plain, out string stored)
    {
        stored = "";
        if (!OperatingSystem.IsWindows()) return false;
        using var input = Blob.Alloc(System.Text.Encoding.UTF8.GetBytes(plain));
        if (!CryptProtectData(ref input.Raw, "AITokenUsageWidget", IntPtr.Zero,
                IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden | CryptProtectLocalMachine, out var outRaw))
            return false;
        using var output = Blob.Wrap(outRaw);
        stored = Prefix + Convert.ToBase64String(output.Bytes());
        return true;
    }

    private static bool TryCryptUnprotect(string base64, out string plain)
    {
        plain = "";
        if (!OperatingSystem.IsWindows()) return false;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64); }
        catch (FormatException) { return false; }

        using var input = Blob.Alloc(bytes);
        if (!CryptUnprotectData(ref input.Raw, IntPtr.Zero, IntPtr.Zero,
                IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out var outRaw))
            return false;
        using var output = Blob.Wrap(outRaw);
        plain = System.Text.Encoding.UTF8.GetString(output.Bytes());
        return true;
    }

    /// <summary>CRYPT_INTEGER BLOB（DATA_BLOB）的托管包装。</summary>
    private sealed class Blob : IDisposable
    {
        public CryptBlob Raw;

        private Blob(CryptBlob raw) => Raw = raw;

        public static Blob Alloc(byte[] data)
        {
            var ptr = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, ptr, data.Length);
            return new Blob(new CryptBlob { Size = data.Length, Data = ptr });
        }

        /// <summary>包装非托管输出 BLOB（接管释放责任）。</summary>
        public static Blob Wrap(CryptBlob raw) => new(raw);

        public byte[] Bytes()
        {
            var result = new byte[Raw.Size];
            if (Raw.Size > 0) Marshal.Copy(Raw.Data, result, 0, Raw.Size);
            return result;
        }

        public void Dispose()
        {
            if (Raw.Data != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(Raw.Data);
                Raw.Data = IntPtr.Zero;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CryptBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref CryptBlob dataIn, string? description, IntPtr optionalEntropy,
        IntPtr reserved, IntPtr promptStruct, int flags, out CryptBlob dataOut);

    // 真实 API 为 7 参数：pDataIn, ppszDataDescr(可 NULL), pOptionalEntropy,
    // pvReserved, pPromptStruct, dwFlags, pDataOut —— 输出 blob 在最后一位！
    // 旧代码把输出写在第 3 位（实为 entropy 位），参数错位导致 ERROR_NOACCESS(998) 永远解密失败。
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref CryptBlob dataIn, IntPtr description, IntPtr optionalEntropy,
        IntPtr reserved, IntPtr promptStruct, int flags, out CryptBlob dataOut);
}
