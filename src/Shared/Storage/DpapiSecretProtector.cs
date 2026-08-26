using System.Runtime.InteropServices;

namespace AITokenUsageWidget.Shared.Storage;

/// <summary>
/// DPAPI（ProtectedData，CurrentUser 范围）加密器。密文带 "dpapi:" 前缀存放；
/// 无前缀的值视为旧版明文原样返回（升级容错）。
/// 仅在 Windows 上可用（P/Invoke crypt32.dll）。
/// </summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    private const string Prefix = "dpapi:";

    public string Protect(string plain)
    {
        if (plain.Length == 0) return "";
        return TryCryptProtect(plain, out var stored) ? stored : plain; // 极端失败退化为明文，避免丢配置
    }

    public string Unprotect(string stored)
    {
        if (stored.Length == 0 || !stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored;
        return TryCryptUnprotect(stored[Prefix.Length..], out var plain) ? plain : "";
    }

    private static bool TryCryptProtect(string plain, out string stored)
    {
        stored = "";
        if (!OperatingSystem.IsWindows()) return false;
        using var input = Blob.Alloc(System.Text.Encoding.UTF8.GetBytes(plain));
        if (!CryptProtectData(ref input.Raw, "AITokenUsageWidget", IntPtr.Zero,
                IntPtr.Zero, IntPtr.Zero, 0, out var outRaw))
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
        if (!CryptUnprotectData(ref input.Raw, out _, out var outRaw,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0))
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

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref CryptBlob dataIn, out IntPtr description, out CryptBlob dataOut,
        IntPtr optionalEntropy, IntPtr reserved, IntPtr promptStruct, int flags);
}
