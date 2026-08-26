using System.Runtime.InteropServices;
using Microsoft.Windows.Widgets.Providers;
using WinRT;

namespace AITokenUsageWidget.App.Widgets;

/// <summary>
/// Widget Provider 的 COM 基础设施（对齐微软官方示例）：
/// IClassFactory 实现 + CoRegisterClassObject + Win32 STA 消息泵。
/// </summary>
internal static class WidgetComInterop
{
    private const uint CLSCTX_LOCAL_SERVER = 0x4;
    private const uint REGCLS_MULTIPLEUSE = 0x1;
    private const uint REGCLS_SUSPENDED = 0x4;
    private const int WM_QUIT = 0x0012;

    public sealed class WidgetProviderFactory : IClassFactory
    {
        private readonly Func<object> _create;

        public WidgetProviderFactory(Func<object> create) => _create = create;

        int IClassFactory.CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
        {
            ppvObject = IntPtr.Zero;
            if (pUnkOuter != IntPtr.Zero) Marshal.ThrowExceptionForHR(-2147221232); // CLASS_E_NOAGGREGATION
            if (riid == typeof(AiWidgetProvider).GUID
                || riid == new Guid("00000000-0000-0000-C000-000000000046")) // IUnknown
            {
                ppvObject = MarshalInspectable<IWidgetProvider>.FromManaged(_create());
                return 0;
            }
            Marshal.ThrowExceptionForHR(-2147467262); // E_NOINTERFACE
            return 0;
        }

        int IClassFactory.LockServer(bool fLock) => 0;
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("00000001-0000-0000-C000-000000000046")]
    internal interface IClassFactory
    {
        [PreserveSig]
        int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);

        [PreserveSig]
        int LockServer(bool fLock);
    }

    public static uint RegisterClassObject(Guid clsid, object factory)
    {
        var hr = CoRegisterClassObject(clsid, factory, CLSCTX_LOCAL_SERVER,
            REGCLS_MULTIPLEUSE | REGCLS_SUSPENDED, out var cookie);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        hr = CoResumeClassObjects();
        if (hr < 0)
        {
            CoRevokeClassObject(cookie);
            Marshal.ThrowExceptionForHR(hr);
        }
        return cookie;
    }

    public static void RevokeClassObject(uint cookie)
    {
        if (cookie != 0) CoRevokeClassObject(cookie);
    }

    /// <summary>
    /// 真正的 Win32 消息循环：打包应用的驻留线程不泵消息会被 PLM 以 MoAppHang 杀掉。
    /// </summary>
    public static void RunMessagePump()
    {
        while (true)
        {
            var result = GetMessageW(out var message, IntPtr.Zero, 0, 0);
            if (result <= 0) break; // WM_QUIT 或错误
            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }
    }

    public static void PostQuitToMainThread(int threadId)
    {
        if (threadId != 0) PostThreadMessageW(threadId, WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("ole32.dll")]
    private static extern int CoRegisterClassObject(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rclsid,
        [MarshalAs(UnmanagedType.IUnknown)] object pUnk,
        uint dwClsContext, uint flags, out uint lpdwRegister);

    [DllImport("ole32.dll")]
    private static extern int CoRevokeClassObject(uint dwRegister);

    [DllImport("ole32.dll")]
    private static extern int CoResumeClassObjects();

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out NativeMessage message, IntPtr hWnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref NativeMessage message);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessageW(int threadId, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    public static extern int GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Handle;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PointX;
        public int PointY;
    }
}
