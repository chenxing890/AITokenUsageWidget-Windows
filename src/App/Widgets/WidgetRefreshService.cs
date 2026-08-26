using AITokenUsageWidget.App.Services;
using AITokenUsageWidget.Shared;
using AITokenUsageWidget.Shared.Models;
using AITokenUsageWidget.Shared.Services;
using AITokenUsageWidget.Shared.Storage;
using AITokenUsageWidget.Shared.Widgets;

namespace AITokenUsageWidget.App.Widgets;

/// <summary>
/// Widget Provider 刷新引擎（§5.2）：
/// - Provider 激活立即刷新；System.Threading.Timer 每 15 分钟自动刷新（对齐 macOS）；
/// - 监听命名事件：主 App 保存配置后立即刷新（含主题变更）；
/// - 抓取 → 写缓存（失败回退缓存并标注）→ 告警评估 → UpdateWidget；
/// - 手动刷新：卡片 Action.Execute(verb=refresh) → OnActionInvoked → RefreshNow。
/// </summary>
public static class WidgetRefreshService
{
    public const int RefreshIntervalMinutes = 15;

    private static readonly object Gate = new();
    private static Timer? _timer;
    private static Thread? _signalThread;
    private static CancellationTokenSource? _shutdown;
    private static int _pumpThreadId;
    private static int _refreshGeneration;

    /// <summary>widgetId → 当前尺寸（由 AiWidgetProvider 维护）。</summary>
    internal static readonly Dictionary<string, WidgetSize> Instances = new();

    private static readonly UsageService UsageService = new();
    private static readonly ConfigStore Store = ConfigStore.Default;

    public static void Start(int pumpThreadId)
    {
        lock (Gate)
        {
            if (_timer != null) return;
            _pumpThreadId = pumpThreadId;
            _shutdown = new CancellationTokenSource();

            _timer = new Timer(_ => RefreshAll("timer"), null,
                TimeSpan.Zero, TimeSpan.FromMinutes(RefreshIntervalMinutes));

            _signalThread = new Thread(ListenRefreshSignal) { IsBackground = true };
            _signalThread.Start();
        }
    }

    public static void Stop()
    {
        lock (Gate)
        {
            _shutdown?.Cancel();
            _timer?.Dispose();
            _timer = null;
            Instances.Clear();
        }
    }

    /// <summary>监听主 App 的刷新信号（配置/主题变更）。</summary>
    private static void ListenRefreshSignal()
    {
        try
        {
            using var signal = new EventWaitHandle(
                false, EventResetMode.AutoReset, RefreshSignaler.EventName);
            while (_shutdown?.IsCancellationRequested != true)
            {
                if (signal.WaitOne(TimeSpan.FromSeconds(1)))
                {
                    Thread.Sleep(300); // 轻微合并连续保存
                    RefreshAll("signal");
                }
            }
        }
        catch (PlatformNotSupportedException) { }
        catch (ObjectDisposedException) { }
    }

    /// <summary>手动刷新（卡片按钮）。仅刷新数据并上屏，不重置周期。</summary>
    public static void RefreshNow(string widgetId) => RefreshAll("action:" + widgetId);

    internal static void RegisterWidget(string widgetId, WidgetSize size)
    {
        lock (Gate) Instances[widgetId] = size;
    }

    internal static void UnregisterWidget(string widgetId)
    {
        lock (Gate)
        {
            Instances.Remove(widgetId);
            if (Instances.Count == 0)
            {
                // 无小组件存活：请求 COM 宿主线程退出（微软示例同款生命周期）
                WidgetComInterop.PostQuitToMainThread(_pumpThreadId);
            }
        }
    }

    /// <summary>全部已固定小组件刷新一次。</summary>
    public static async void RefreshAll(string reason)
    {
        try
        {
            await RefreshAllCoreAsync();
        }
        catch (Exception)
        {
            // Provider 进程内任何刷新异常都不应崩溃（下次定时/手动刷新自愈）
        }
    }

    private static async Task RefreshAllCoreAsync()
    {
        List<KeyValuePair<string, WidgetSize>> widgets;
        lock (Gate) widgets = Instances.ToList();
        if (widgets.Count == 0) return;

        var generation = Interlocked.Increment(ref _refreshGeneration);
        var data = Store.Load();
        var activeConfigs = data.Configs.Where(c => c.IsEnabled).ToList();

        List<ProviderUsage> usages;
        if (activeConfigs.Count == 0)
        {
            usages = [];
        }
        else
        {
            usages = await UsageService.FetchAllAsync(activeConfigs);
            if (generation != Volatile.Read(ref _refreshGeneration)) return; // 已有更新的刷新

            // 失败回退缓存并标注（FR-6：连续失败不清空缓存）
            for (var i = 0; i < usages.Count; i++)
            {
                if (usages[i].State != UsageState.Ok)
                {
                    var cached = data.CachedUsages.FirstOrDefault(
                        u => u.Kind == usages[i].Kind && u.State == UsageState.Ok);
                    if (cached != null)
                    {
                        usages[i] = new ProviderUsage
                        {
                            Kind = cached.Kind,
                            DisplayName = usages[i].DisplayName,
                            State = UsageState.Ok,
                            Currency = cached.Currency,
                            TotalBalance = cached.TotalBalance,
                            GrantedBalance = cached.GrantedBalance,
                            ToppedUpBalance = cached.ToppedUpBalance,
                            IsAvailable = cached.IsAvailable,
                            Windows = cached.Windows,
                            FetchedAt = cached.FetchedAt,
                            FromCache = true,
                        };
                    }
                }
            }
            Store.SaveCachedUsages(usages.Where(u => u.State == UsageState.Ok).ToList());

            EvaluateAlerts(usages, data);
        }

        foreach (var (widgetId, size) in widgets)
        {
            UpdateWidget(widgetId, size, usages, data.Theme);
        }
    }

    /// <summary>告警评估与 Toast 发送（FR-4 / 附录 B）。</summary>
    private static void EvaluateAlerts(List<ProviderUsage> usages, SharedData data)
    {
        var result = AlertEngine.Evaluate(usages, data.AlertsEnabled, data.AlertThreshold, data.AlertedKeys);
        foreach (var alert in result.Alerts)
        {
            Services.ToastService.Show(alert);
        }
        if (result.Changed)
        {
            Store.ReplaceAlertedKeys(result.AlertedKeys);
        }
    }

    /// <summary>渲染 Adaptive Card 并推送到指定小组件（模板与数据均携带完整字面卡片）。</summary>
    internal static void UpdateWidget(
        string widgetId, WidgetSize size, IReadOnlyList<ProviderUsage> usages, ThemePreference theme)
    {
        try
        {
            var card = Shared.Widgets.WidgetCard.Build(
                size, usages, theme, SystemIsDark(), DateTimeOffset.UtcNow);
            var options = new Microsoft.Windows.Widgets.Providers.WidgetUpdateRequestOptions(widgetId)
            {
                Template = card,
                Data = Shared.Widgets.WidgetCard.EmptyData,
            };
            Microsoft.Windows.Widgets.Providers.WidgetManager.GetDefault().UpdateWidget(options);
        }
        catch (Exception)
        {
            // 板面未就绪等场景忽略
        }
    }

    /// <summary>系统是否深色（Widget Board 背景由系统托管，仅 System 主题时使用）。</summary>
    internal static bool SystemIsDark()
    {
        try
        {
            var ui = new Windows.UI.ViewManagement.UISettings();
            var background = ui.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
            return background.R < 128;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
