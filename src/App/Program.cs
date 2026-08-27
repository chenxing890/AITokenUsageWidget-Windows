using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace AITokenUsageWidget.App;

/// <summary>
/// 自定义入口（DISABLE_XAML_GENERATED_MAIN）：
/// - 正常启动 → WinUI 3 主窗口；
/// - Widgets Board 以 "-RegisterProcessAsComServer" 拉起 → 无头 Widget Provider
///   COM 服务器 + STA 消息泵（对齐微软官方 Widget Provider 模式）。
/// </summary>
public static class Program
{
    private const string ComServerArg = "-RegisterProcessAsComServer";

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Any(a => a.Equals(ComServerArg, StringComparison.OrdinalIgnoreCase)))
        {
            Widgets.WidgetHost.RunComServer();
            return;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ => new App());
    }
}
