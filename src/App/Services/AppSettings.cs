using AITokenUsageWidget.Shared.Models;
using AITokenUsageWidget.Shared.Services;
using AITokenUsageWidget.Shared.Storage;

namespace AITokenUsageWidget.App.Services;

/// <summary>
/// 共享配置的应用侧门面（FR-3 / FR-5）：
/// 持有内存副本，所有修改即时持久化（自动保存），并通知小组件刷新。
/// </summary>
public static class AppSettings
{
    private static readonly ConfigStore Store = ConfigStore.Default;
    private static bool _initialized;

    public static SharedData Data { get; private set; } = SharedData.Empty();

    public static event Action? Changed;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        Data = Store.LoadWithDefaults();

        if (Store.LastQuarantinedFile is { } quarantined)
        {
            CorruptBackupNotice = $"配置文件已损坏并备份至 {quarantined}，请重新配置供应商。";
        }
    }

    /// <summary>配置文件损坏提示（一次性，主窗口 InfoBar 展示，§4.5）。</summary>
    public static string? CorruptBackupNotice { get; private set; }

    public static void ClearCorruptNotice() => CorruptBackupNotice = null;

    public static ProviderConfig Config(ProviderKind kind) =>
        Data.Configs.FirstOrDefault(c => c.Kind == kind) ?? new ProviderConfig(kind);

    // ---- 修改即保存（自动保存），并通知小组件 ----

    public static void SaveConfig(ProviderConfig config)
    {
        var index = Data.Configs.FindIndex(c => c.Kind == config.Kind);
        if (index >= 0) Data.Configs[index] = config;
        else Data.Configs.Add(config);
        Store.Save(Data);
        Commit();
    }

    public static void SetTheme(ThemePreference theme)
    {
        Data.Theme = theme;
        Store.SetTheme(theme);
        Commit();
    }

    public static void SetAlertsEnabled(bool enabled)
    {
        Data.AlertsEnabled = enabled;
        Store.SetAlertsEnabled(enabled);
        Commit();
    }

    /// <summary>修改阈值：清空已通知记录并立即重评估（FR-4）。</summary>
    public static void SetAlertThreshold(double threshold)
    {
        Data.AlertThreshold = threshold;
        Store.SetAlertThresholdAndReset(threshold);
        Commit();
    }

    /// <summary>保存后通知小组件立即重新拉取数据（命名事件，§5.2）。</summary>
    private static void Commit()
    {
        RefreshSignaler.Signal();
        Changed?.Invoke();
    }

    public static double[] AlertThresholdOptions => AlertEngine.ThresholdOptions;
}
