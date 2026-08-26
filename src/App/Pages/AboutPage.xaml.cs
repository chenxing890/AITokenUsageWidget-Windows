using AITokenUsageWidget.Shared.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AITokenUsageWidget.App.Pages;

/// <summary>关于页（FR-3 底部）：版本号、数据目录快捷入口、固定小组件引导。</summary>
public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        var version = typeof(AboutPage).Assembly.GetName().Version;
        VersionText.Text = $"AITokenUsageWidget for Windows v{version?.ToString(3)}";
    }

    private void OnOpenDataDir(object sender, RoutedEventArgs e)
    {
        try
        {
            AppPaths.EnsureDirectory();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = AppPaths.DefaultBaseDir,
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            // 打开失败忽略
        }
    }

    private void OnShowPinTip(object sender, RoutedEventArgs e) => PinTipBar.IsOpen = true;
}
