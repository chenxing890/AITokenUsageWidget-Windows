using AITokenUsageWidget.App.Pages;
using AITokenUsageWidget.App.Services;
using AITokenUsageWidget.Shared.Models;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace AITokenUsageWidget.App;

/// <summary>
/// 主窗口（FR-3）：NavigationView 左侧导航；默认 860×640、最小 720×520；Mica 标题栏。
/// </summary>
public sealed partial class MainWindow : Window
{
    private const int DefaultWidth = 860;
    private const int DefaultHeight = 640;
    private const int MinWidth = 720;
    private const int MinHeight = 520;

    public MainWindow()
    {
        InitializeComponent();

        SystemBackdrop = new MicaBackdrop();
        Title = "AI 模型用量";
        ThemeHelper.Attach(RootGrid);
        ConfigureWindowSize();
        ContentFrame.Navigate(typeof(ProviderPage), ProviderKind.DeepSeek);

        if (AppSettings.CorruptBackupNotice != null)
        {
            CorruptInfoBar.Message = AppSettings.CorruptBackupNotice;
            CorruptInfoBar.IsOpen = true;
            AppSettings.ClearCorruptNotice();
        }

        // Toast / 小组件引导拉起：定位到对应供应商页
        if (App.PendingProviderNavigation is { } kind)
        {
            App.PendingProviderNavigation = null;
            NavigateToProvider(kind);
        }
    }

    private void ConfigureWindowSize()
    {
        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        var scale = displayArea.WorkArea.Width > 0 ? 1.0 : 1.0;
        var width = Math.Min(DefaultWidth, displayArea.WorkArea.Width);
        var height = Math.Min(DefaultHeight, displayArea.WorkArea.Height);

        AppWindow.Resize(new SizeInt32((int)(width * scale), (int)(height * scale)));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = MinWidth;
            presenter.PreferredMinimumHeight = MinHeight;
        }
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag) return;

        switch (tag)
        {
            case "appearance":
                ContentFrame.Navigate(typeof(AppearancePage));
                break;
            case "notifications":
                ContentFrame.Navigate(typeof(NotificationsPage));
                break;
            case "about":
                ContentFrame.Navigate(typeof(AboutPage));
                break;
            default:
                if (ProviderKindExtensions.FromKindId(tag) is { } kind)
                {
                    ContentFrame.Navigate(typeof(ProviderPage), kind);
                }
                break;
        }
    }

    private void NavigateToProvider(ProviderKind kind)
    {
        foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>()
                     .Concat(NavView.FooterMenuItems.OfType<NavigationViewItem>()))
        {
            if (item.Tag is string tag && tag == kind.KindId())
            {
                item.IsSelected = true;
                break;
            }
        }
        ContentFrame.Navigate(typeof(ProviderPage), kind);
    }
}
