using AITokenUsageWidget.App.Controls;
using AITokenUsageWidget.App.Services;
using AITokenUsageWidget.Shared.Models;
using AITokenUsageWidget.Shared.Services;
using AITokenUsageWidget.Shared.Simulation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AITokenUsageWidget.App.Pages;

/// <summary>
/// 仪表盘（对齐 macOS DashboardView）：聚合全部已启用供应商的用量卡片，
/// 每个供应商一行（大尺寸三行布局，与小组件 large 一致）。
/// 已配置 Key 的显示实际用量（进入自动拉取）；未配置的显示模拟数据 + 橙色「模拟」角标。
/// </summary>
public sealed partial class DashboardPage : Page
{
    private readonly UsageService _usageService = new();
    private bool _fetching;

    public DashboardPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        RenderAndFetch();
    }

    /// <summary>重建卡片：先用缓存/占位渲染（区域不塌陷），再并发拉取实际数据。</summary>
    private void RenderAndFetch()
    {
        var enabled = EnabledConfigs();
        CardsPanel.Children.Clear();

        foreach (var config in enabled)
        {
            var card = new Controls.UsageCardControl();
            // 先上占位（模拟），拉取成功后替换为实际数据
            card.Usage = config.HasKey ? Placeholders.For(config.Kind) : Placeholders.For(config.Kind);
            CardsPanel.Children.Add(WrapWithBadge(card, showSimulated: !config.HasKey));
        }

        UpdateCaption(fetching: enabled.Any(c => c.HasKey));

        if (enabled.Any(c => c.HasKey)) _ = FetchAllAsync();
    }

    private static List<ProviderConfig> EnabledConfigs() =>
        ProviderKindExtensions.All
            .Select(AppSettings.Config)
            .Where(c => c.IsEnabled)
            .ToList();

    /// <summary>给卡片包一层 Grid，未配置时右上角叠橙色「模拟」角标。</summary>
    private static Grid WrapWithBadge(Controls.UsageCardControl card, bool showSimulated)
    {
        var grid = new Grid();
        grid.Children.Add(card);
        if (showSimulated)
        {
            var badge = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(6),
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SimulatedBadgeBrush"],
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(7, 3, 7, 3),
                Child = new TextBlock
                {
                    Text = "模拟",
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                },
            };
            grid.Children.Add(badge);
        }
        return grid;
    }

    private async Task FetchAllAsync()
    {
        if (_fetching) return;
        _fetching = true;
        RunOnUi(() =>
        {
            RefreshButton.IsEnabled = false;
            LoadingRing.Visibility = Visibility.Visible;
            LoadingRing.IsActive = true;
        });
        try
        {
            var configured = EnabledConfigs().Where(c => c.HasKey).ToList();
            var fetchTask = _usageService.FetchAllAsync(configured);
            var finished = await Task.WhenAny(fetchTask, Task.Delay(TimeSpan.FromSeconds(15)));
            if (!ReferenceEquals(finished, fetchTask))
            {
                RunOnUi(() => CaptionText.Text = "连接超时 · 请检查网络或代理设置");
                return;
            }
            var usages = await fetchTask;

            RunOnUi(() =>
            {
                // 用实际数据重建（保留未配置供应商的模拟卡片在最后）
                var enabled = EnabledConfigs();
                CardsPanel.Children.Clear();
                foreach (var config in enabled)
                {
                    var card = new Controls.UsageCardControl();
                    var live = usages.FirstOrDefault(u => u.Kind == config.Kind);
                    card.Usage = live ?? Placeholders.For(config.Kind);
                    CardsPanel.Children.Add(WrapWithBadge(card, showSimulated: live == null || live.IsSimulated));
                }
                var latest = usages.Count > 0 ? usages.Max(u => u.FetchedAt) : DateTimeOffset.UtcNow;
                CaptionText.Text = $"实际用量 · 更新于 {Format.ClockTime(latest)}";
            });
        }
        catch (Exception ex)
        {
            RunOnUi(() => CaptionText.Text = Shared.Networking.ApiHttp.FriendlyMessage(ex));
        }
        finally
        {
            _fetching = false;
            RunOnUi(() =>
            {
                RefreshButton.IsEnabled = true;
                LoadingRing.Visibility = Visibility.Collapsed;
                LoadingRing.IsActive = false;
            });
        }
    }

    private void UpdateCaption(bool fetching) =>
        CaptionText.Text = fetching ? "正在连接…" : "模拟数据 · 配置 API Key 后显示实际用量";

    private void OnRefresh(object sender, RoutedEventArgs e) => _ = FetchAllAsync();

    private void RunOnUi(Action action)
    {
        if (DispatcherQueue.HasThreadAccess) action();
        else DispatcherQueue.TryEnqueue(() => action());
    }
}
