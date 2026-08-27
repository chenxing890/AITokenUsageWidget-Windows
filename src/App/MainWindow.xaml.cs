using AITokenUsageWidget.App.Pages;
using AITokenUsageWidget.App.Services;
using AITokenUsageWidget.Shared.Models;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace AITokenUsageWidget.App;

/// <summary>
/// 主窗口（对齐 macOS 设置窗口）：左侧边栏（供应商 / 外观 / 通知 分区 + 「n/3 已启用」状态栏），
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
    private ProviderKind? _currentPageKind; // 当前详情页供应商，避免重复导航

    /// <summary>侧栏供应商行视图模型。</summary>
    private sealed class ProviderRow
    {
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

        LoadSidebar(App.PendingProviderNavigation ?? ProviderKind.DeepSeek);
        if (App.PendingProviderNavigation != null)
        {
            App.PendingProviderNavigation = null;
        }

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
        var current = (ProviderList.SelectedItem as ProviderRow)?.Kind ?? ProviderKind.DeepSeek;
        LoadSidebar(current);
    }

    /// <summary>重建侧栏供应商行与底部「n/3 已启用」，并保持选中项。</summary>
    private void LoadSidebar(ProviderKind select)
    {
        var rows = ProviderKindExtensions.All.Select(kind =>
        {
            var enabled = AppSettings.Config(kind).IsEnabled;
            return new ProviderRow
            {
                Kind = kind,
                Name = kind.DisplayName(),
                Subtitle = enabled ? kind.Subtitle() : "未启用",
                SubtitleBrush = SidebarTextBrush(tertiary: !enabled),
                DotVisibility = enabled ? Visibility.Visible : Visibility.Collapsed,
            };
        }).ToList();

        ProviderList.ItemsSource = rows;
        ProviderList.SelectedItem = rows.FirstOrDefault(r => r.Kind == select) ?? rows[0];

        var enabledCount = rows.Count(r => r.DotVisibility == Visibility.Visible);
        EnabledSummary.Text = $"{enabledCount}/{rows.Count} 已启用";
    }

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
            if (_currentPageKind == row.Kind) return;
            _currentPageKind = row.Kind;
            ContentFrame.Navigate(typeof(ProviderPage), row.Kind);
        }
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
