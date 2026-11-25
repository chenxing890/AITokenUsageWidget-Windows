using AITokenUsageWidget.App.Pages;
using AITokenUsageWidget.App.Services;
using AITokenUsageWidget.Shared.Models;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace AITokenUsageWidget.App;

/// <summary>
/// 主窗口（对齐 macOS 设置窗口）：左侧边栏（仪表盘 / 供应商实例（同模型可多个，
/// 「+」新增 / 右键删除）/ 外观 / 通知 分区 + 「n/3 已启用」状态栏），
/// 右侧供应商详情；标题栏 Mica + 右上角「刷新小组件」。
/// 默认 860×640、最小 720×520。
/// </summary>
public sealed partial class MainWindow : Window
{
    private const int DefaultWidth = 860;
    private const int DefaultHeight = 640;
    private const int MinWidth = 720;
    private const int MinHeight = 520;

    private bool _loading;
    private Guid? _currentPageId; // 当前详情页实例（ProviderConfig.Id），避免重复导航
    private bool _onDashboard; // 当前是否停留在仪表盘页

    /// <summary>侧栏供应商行视图模型（一个配置实例一行，同 kind 多实例各占一行）。</summary>
    private sealed class ProviderRow
    {
        public required Guid ConfigId { get; init; }
        public required ProviderKind Kind { get; init; }
        public required string Name { get; init; }
        public required string Subtitle { get; init; }
        public required Brush SubtitleBrush { get; init; }
        public required Visibility DotVisibility { get; init; }
    }

    public MainWindow()
    {
        InitializeComponent();

        SystemBackdrop = new MicaBackdrop();
        Title = "AI 模型用量";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarGrid);
        RefreshButton.Margin = new Thickness(0, 0, AppWindow.TitleBar.RightInset + 4, 0);

        ThemeHelper.Attach(RootGrid);
        ConfigureWindowSize();

        _loading = true;
        foreach (var theme in ThemePreferenceExtensions.All)
        {
            ThemeOptions.Items.Add(theme.Label());
        }
        ThemeOptions.SelectedIndex = (int)AppSettings.Data.Theme;

        foreach (var option in AppSettings.AlertThresholdOptions)
        {
            ThresholdBox.Items.Add($"{option:0}%");
        }
        AlertsToggle.IsOn = AppSettings.Data.AlertsEnabled;
        ThresholdBox.SelectedIndex = Array.IndexOf(AppSettings.AlertThresholdOptions, AppSettings.Data.AlertThreshold);
        if (ThresholdBox.SelectedIndex < 0) ThresholdBox.SelectedIndex = 3; // 80%
        ThresholdBox.IsEnabled = AlertsToggle.IsOn;
        _loading = false;

        // 概览（仪表盘）行：单一数据项，选中即导航
        DashboardList.ItemsSource = new[] { new object() };

        // 深链（aitokenusage://provider/<kind>）定位到该 kind 的第一个实例
        var initialKind = App.PendingProviderNavigation;
        App.PendingProviderNavigation = null;
        var initialId = (initialKind is { } kind
                ? AppSettings.Data.Configs.FirstOrDefault(c => c.Kind == kind)?.Id
                : null)
            ?? AppSettings.Data.Configs.FirstOrDefault()?.Id
            ?? Guid.Empty;
        LoadSidebar(initialId);

        if (AppSettings.CorruptBackupNotice != null)
        {
            CorruptInfoBar.Message = AppSettings.CorruptBackupNotice;
            CorruptInfoBar.IsOpen = true;
            AppSettings.ClearCorruptNotice();
        }

        AppSettings.Changed += OnSettingsChanged;
        Closed += (_, _) => AppSettings.Changed -= OnSettingsChanged;
    }

    private void OnSettingsChanged()
    {
        if (_onDashboard)
        {
            LoadSidebarDashboard();
            return;
        }

        // 当前实例可能已被删除：回退到第一行并重新导航
        var currentId = (ProviderList.SelectedItem as ProviderRow)?.ConfigId ?? _currentPageId;
        var stillExists = currentId != null
            && AppSettings.Data.Configs.Any(c => c.Id == currentId);
        var select = stillExists == true
            ? currentId!.Value
            : AppSettings.Data.Configs.FirstOrDefault()?.Id ?? Guid.Empty;
        LoadSidebar(select);

        if (!stillExists)
        {
            _currentPageId = select;
            var first = AppSettings.Data.Configs.FirstOrDefault();
            if (first != null) ContentFrame.Navigate(typeof(ProviderPage), first.Id);
        }
    }

    /// <summary>是否有已启用实例（仪表盘仅在此条件下显示）。</summary>
    private static bool AnyEnabled() => AppSettings.Data.Configs.Any(c => c.IsEnabled);

    /// <summary>侧栏行：一个配置实例一行（同 kind 多实例各占一行）。</summary>
    private List<ProviderRow> BuildRows() => AppSettings.Data.Configs.Select(config =>
    {
        var enabled = config.IsEnabled;
        return new ProviderRow
        {
            ConfigId = config.Id,
            Kind = config.Kind,
            Name = config.Name,
            Subtitle = enabled ? config.Kind.Subtitle() : "未启用",
            SubtitleBrush = SidebarTextBrush(tertiary: !enabled),
            DotVisibility = enabled ? Visibility.Visible : Visibility.Collapsed,
        };
    }).ToList();

    /// <summary>重建侧栏供应商行与底部「n/3 已启用」，并保持选中项。</summary>
    private void LoadSidebar(Guid select)
    {
        _onDashboard = false;
        var rows = BuildRows();

        ProviderList.ItemsSource = rows;
        UpdateDashboardVisibility();
        DashboardList.SelectedIndex = -1;
        ProviderList.SelectedItem = rows.FirstOrDefault(r => r.ConfigId == select)
            ?? rows.FirstOrDefault();

        var enabledCount = rows.Count(r => r.DotVisibility == Visibility.Visible);
        EnabledSummary.Text = $"{enabledCount}/{rows.Count} 已启用";
    }

    /// <summary>重建侧栏并选中仪表盘（有启用才显示；否则回落到第一个实例）。</summary>
    private void LoadSidebarDashboard()
    {
        var rows = BuildRows();

        ProviderList.ItemsSource = rows;
        var enabledCount = rows.Count(r => r.DotVisibility == Visibility.Visible);
        EnabledSummary.Text = $"{enabledCount}/{rows.Count} 已启用";

        if (!AnyEnabled())
        {
            // 全部关闭：隐藏仪表盘并回落到第一个实例页
            _onDashboard = false;
            UpdateDashboardVisibility();
            DashboardList.SelectedIndex = -1;
            ProviderList.SelectedItem = rows.FirstOrDefault();
            return;
        }

        _onDashboard = true;
        UpdateDashboardVisibility();
        ProviderList.SelectedIndex = -1;
        DashboardList.SelectedIndex = 0;
    }

    /// <summary>仪表盘行只在至少一个供应商启用时显示。</summary>
    private void UpdateDashboardVisibility() =>
        DashboardList.Visibility = AnyEnabled() ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// 侧栏副标题画刷：Application.Resources 只会解析启动时主题（快照），
    /// 按窗口 ActualTheme 取值保证深色模式可读（LoadSidebar 随主题切换重建）。
    /// </summary>
    private Brush SidebarTextBrush(bool tertiary)
    {
        var dark = RootGrid.ActualTheme == ElementTheme.Dark;
        var alpha = tertiary ? (byte)0x8A : (byte)0xC5;
        return new SolidColorBrush(dark
            ? Windows.UI.Color.FromArgb(alpha, 255, 255, 255)
            : Windows.UI.Color.FromArgb(tertiary ? (byte)0x72 : (byte)0x9D, 0, 0, 0));
    }

    private void OnProviderSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ProviderList.SelectedItem is ProviderRow row)
        {
            // 编辑配置会触发 AppSettings.Changed → 侧栏重建 → 选中项被重设；
            // 若不加守卫，每敲一个字符都会重新导航、销毁正在输入的页面
            //（焦点丢失、按键落入虚空、预览反复重置为「正在连接」）。
            if (!_onDashboard && _currentPageId == row.ConfigId) return;
            _onDashboard = false;
            _currentPageId = row.ConfigId;
            DashboardList.SelectedIndex = -1; // 切到供应商：取消概览选中
            ContentFrame.Navigate(typeof(ProviderPage), row.ConfigId);
        }
    }

    private void OnDashboardSelected(object sender, SelectionChangedEventArgs e)
    {
        if (DashboardList.SelectedIndex < 0) return;
        if (!AnyEnabled()) return; // 无启用实例时不进入
        if (_onDashboard) return; // 已在仪表盘，避免重复导航
        _onDashboard = true;
        _currentPageId = null;
        ProviderList.SelectedIndex = -1; // 切到概览：取消供应商选中
        ContentFrame.Navigate(typeof(DashboardPage));
    }

    // ---- 实例新增 / 删除（对齐 macOS：同模型可多个，右键删除带确认） ----

    /// <summary>「+」菜单：新增一个所选模型的实例。</summary>
    private void OnAddInstance(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag }
            || !Enum.TryParse<ProviderKind>(tag, out var kind)) return;

        var config = new ProviderConfig(kind);
        AppSettings.AddConfig(config); // 触发 Changed → 侧栏重建
        LoadSidebar(config.Id); // 选中并进入新实例
        _currentPageId = config.Id;
        ContentFrame.Navigate(typeof(ProviderPage), config.Id);
    }

    /// <summary>右键行：弹出「删除」菜单。</summary>
    private void OnProviderRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not ProviderRow row) return;

        var deleteItem = new MenuFlyoutItem
        {
            Text = "删除",
            Tag = row,
            Icon = new FontIcon { Glyph = "\uE74D" },
        };
        deleteItem.Click += OnDeleteInstanceClick;
        var flyout = new MenuFlyout();
        flyout.Items.Add(deleteItem);
        flyout.ShowAt(ProviderList, e.GetPosition(ProviderList));
    }

    /// <summary>删除确认弹窗（对齐 macOS：明确不可撤销）。</summary>
    private async void OnDeleteInstanceClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ProviderRow row) return;

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = $"删除 {row.Name}？",
            Content = "该实例的 API Key 与自定义名称将被移除，此操作不可撤销。",
            PrimaryButtonText = "删除",
            SecondaryButtonText = "取消",
            DefaultButton = ContentDialogButton.Secondary,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        AppSettings.DeleteConfig(row.ConfigId); // Changed → OnSettingsChanged 回退导航
    }

    // ---- 外观（对齐 macOS sidebar「外观」分区） ----

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeOptions.SelectedIndex < 0) return;

        var theme = ThemePreferenceExtensions.All[ThemeOptions.SelectedIndex];
        ThemeHelper.Apply(theme); // 主窗口即时切换
        AppSettings.SetTheme(theme); // 写共享配置 + 通知小组件
    }

    // ---- 通知（对齐 macOS sidebar「通知」分区） ----

    private void OnAlertsToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        ThresholdBox.IsEnabled = AlertsToggle.IsOn;
        AppSettings.SetAlertsEnabled(AlertsToggle.IsOn);
    }

    private void OnThresholdChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThresholdBox.SelectedIndex < 0) return;
        AppSettings.SetAlertThreshold(AppSettings.AlertThresholdOptions[ThresholdBox.SelectedIndex]);
    }

    // ---- 标题栏「刷新小组件」（对齐 macOS 工具栏按钮 + 胶囊 Toast） ----

    private void OnRefreshWidgets(object sender, RoutedEventArgs e)
    {
        RefreshSignaler.Signal();
        RefreshToast.Visibility = Visibility.Visible;

        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(2);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => RefreshToast.Visibility = Visibility.Collapsed;
        timer.Start();
    }

    // ---- 窗口尺寸（最小尺寸通过 Win32 子类化实现） ----

    private void ConfigureWindowSize()
    {
        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        var width = Math.Min(DefaultWidth, displayArea.WorkArea.Width);
        var height = Math.Min(DefaultHeight, displayArea.WorkArea.Height);
        AppWindow.Resize(new SizeInt32(width, height));

        // WinUI 3 无最小尺寸托管 API：子类化窗口过程处理 WM_GETMINMAXINFO
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _wndProc = WndProc;
        _oldWndProc = SetWindowWndProc(hwnd, Marshal.GetFunctionPointerForDelegate(_wndProc));
    }

    private static IntPtr SetWindowWndProc(IntPtr hwnd, IntPtr newProc) =>
        IntPtr.Size == 8
            ? SetWindowLongPtr64(hwnd, GwlWndProc, newProc)
            : SetWindowLong32(hwnd, GwlWndProc, newProc);

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmGetMinMaxInfo)
        {
            var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            info.MinTrackSize = new Point32(MinWidth, MinHeight);
            Marshal.StructureToPtr(info, lParam, false);
        }
        return CallWindowProcW(_oldWndProc, hwnd, msg, wParam, lParam);
    }

    private const uint WmGetMinMaxInfo = 0x0024;
    private const int GwlWndProc = -4;
    private IntPtr _oldWndProc;
    private WndProcDelegate? _wndProc; // 保持委托存活，防止 GC 回收

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32
    {
        public int X;
        public int Y;
        public Point32(int x, int y) => (X, Y) = (x, y);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point32 Reserved;
        public Point32 MaxSize;
        public Point32 MaxPosition;
        public Point32 MinTrackSize;
        public Point32 MaxTrackSize;
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern IntPtr SetWindowLong32(IntPtr hwnd, int index, IntPtr newProc);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr newProc);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProcW(IntPtr prevProc, IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
