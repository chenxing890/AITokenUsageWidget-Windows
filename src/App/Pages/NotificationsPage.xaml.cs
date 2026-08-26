using AITokenUsageWidget.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AITokenUsageWidget.App.Pages;

/// <summary>通知页（FR-4）：告警总开关 + 阈值下拉（默认 80%）。</summary>
public sealed partial class NotificationsPage : Page
{
    private bool _loading;

    public NotificationsPage()
    {
        InitializeComponent();

        foreach (var option in AppSettings.AlertThresholdOptions)
        {
            ThresholdBox.Items.Add($"{option:0}%");
        }
        _loading = true;
        AlertsToggle.IsOn = AppSettings.Data.AlertsEnabled;
        ThresholdBox.SelectedIndex = Array.IndexOf(AppSettings.AlertThresholdOptions, AppSettings.Data.AlertThreshold);
        if (ThresholdBox.SelectedIndex < 0) ThresholdBox.SelectedIndex = 3; // 80%
        _loading = false;
    }

    private void OnAlertsToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        AppSettings.SetAlertsEnabled(AlertsToggle.IsOn);
    }

    private void OnThresholdChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThresholdBox.SelectedIndex < 0) return;
        AppSettings.SetAlertThreshold(AppSettings.AlertThresholdOptions[ThresholdBox.SelectedIndex]);
    }
}
