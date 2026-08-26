using AITokenUsageWidget.App.Services;
using AITokenUsageWidget.Shared.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AITokenUsageWidget.App.Pages;

/// <summary>外观页（FR-3 / §4.4）：主题三档切换，主窗口即时生效。</summary>
public sealed partial class AppearancePage : Page
{
    private bool _loading;

    public AppearancePage()
    {
        InitializeComponent();

        foreach (var theme in ThemePreferenceExtensions.All)
        {
            ThemeOptions.Items.Add(theme.Label());
        }
        _loading = true;
        ThemeOptions.SelectedIndex = (int)AppSettings.Data.Theme;
        _loading = false;
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (ThemeOptions.SelectedIndex < 0) return;

        var theme = ThemePreferenceExtensions.All[ThemeOptions.SelectedIndex];
        ThemeHelper.Apply(theme); // 主窗口即时切换
        AppSettings.SetTheme(theme); // 写共享配置 + 通知小组件
    }
}
