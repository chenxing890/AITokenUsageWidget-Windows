using AITokenUsageWidget.Shared.Models;
using AITokenUsageWidget.Shared.Storage;
using Xunit;

namespace AITokenUsageWidget.Shared.Tests;

/// <summary>可逆假加密器：验证「落盘为密文、读回为明文」的通道。</summary>
public sealed class FakeProtector : ISecretProtector
{
    public string Protect(string plain) => plain.Length == 0 ? "" : "fake:" + Convert.ToBase64String(
        System.Text.Encoding.UTF8.GetBytes(plain));
    public string Unprotect(string stored)
    {
        if (!stored.StartsWith("fake:")) return stored;
        try { return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(stored["fake:".Length..])); }
        catch (FormatException) { return ""; }
    }
}

public class ConfigStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly ConfigStore _store;

    public ConfigStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "aitw-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _store = new ConfigStore(_dir, new FakeProtector());
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private string SharedPath => AppPaths.SharedJsonPath(_dir);

    [Fact]
    public void RoundTrip_SecretsEncryptedOnDisk()
    {
        var kimi = new ProviderConfig(ProviderKind.Kimi)
        {
            IsEnabled = true,
            ApiKey = "sk-kimi-secret",
            ExtraToken = "kimi-auth-cookie",
            CustomName = "我的 Kimi",
            BaseURLOverride = "https://proxy.example.com",
        };
        _store.Save(new SharedData { Configs = [kimi], Theme = ThemePreference.Dark, AlertThreshold = 90 });

        var onDisk = File.ReadAllText(SharedPath);
        Assert.DoesNotContain("sk-kimi-secret", onDisk); // 密文落盘（DPAPI 通道验证）
        Assert.DoesNotContain("kimi-auth-cookie", onDisk);
        Assert.Contains("fake:", onDisk);

        var loaded = _store.Load();
        var loadedKimi = Assert.Single(loaded.Configs);
        Assert.Equal("sk-kimi-secret", loadedKimi.ApiKey);
        Assert.Equal("kimi-auth-cookie", loadedKimi.ExtraToken);
        Assert.Equal("我的 Kimi", loadedKimi.Name);
        Assert.Equal("https://proxy.example.com/usages", loadedKimi.BuildApiURL("/usages"));
        Assert.Equal(ThemePreference.Dark, loaded.Theme);
        Assert.Equal(90, loaded.AlertThreshold);
    }

    [Fact]
    public void MissingFile_ReturnsEmpty()
    {
        var data = _store.Load();
        Assert.Empty(data.Configs);
        Assert.Equal(ThemePreference.System, data.Theme);
        Assert.True(data.AlertsEnabled);
        Assert.Equal(80, data.AlertThreshold);
    }

    [Fact]
    public void PartialFields_ToleratedWithoutTotalFailure()
    {
        File.WriteAllText(SharedPath, """{"theme":"dark"}""");
        var data = _store.Load();
        Assert.Equal(ThemePreference.Dark, data.Theme);
        Assert.Empty(data.Configs); // 单字段缺失不导致整体失败

        File.WriteAllText(SharedPath,
            """{"configs":[{"kind":"kimi","isEnabled":"yes","apiKey":123}],"alertThreshold":"abc"}""");
        data = _store.Load();
        var kimi = Assert.Single(data.Configs); // 类型异常字段回退默认值
        Assert.Equal(ProviderKind.Kimi, kimi.Kind);
        Assert.False(kimi.IsEnabled);
        Assert.Equal("", kimi.ApiKey);
        Assert.Equal(80, data.AlertThreshold);
    }

    [Fact]
    public void CorruptFile_QuarantinedAndEmptyReturned()
    {
        File.WriteAllText(SharedPath, "{ this is not json !!!");
        var data = _store.Load();

        Assert.Empty(data.Configs);
        Assert.False(File.Exists(SharedPath)); // 原文件被改名保留
        var quarantined = Directory.GetFiles(_dir, "shared.corrupt-*.json");
        Assert.Single(quarantined);
        Assert.Contains("{ this is not json !!!", File.ReadAllText(quarantined[0]));
        Assert.Equal(quarantined[0], _store.LastQuarantinedFile);
    }

    [Fact]
    public void Save_CreatesBackupThenAtomicReplace()
    {
        _store.Save(new SharedData { AlertThreshold = 50 });
        var first = File.ReadAllText(SharedPath);

        _store.Save(new SharedData { AlertThreshold = 70 });
        Assert.Contains("70", File.ReadAllText(SharedPath));
        Assert.Equal(first, File.ReadAllText(AppPaths.BackupJsonPath(_dir))); // .bak 为上一次内容
        Assert.False(File.Exists(SharedPath + ".tmp")); // 无残留临时文件
    }

    [Fact]
    public void LoadWithDefaults_CreatesThreeDisabledProviders()
    {
        var data = _store.LoadWithDefaults();
        Assert.Equal(3, data.Configs.Count);
        Assert.All(data.Configs, c => Assert.False(c.IsEnabled));
        Assert.Equal(3, _store.Load().Configs.Count); // 已持久化
    }

    [Fact]
    public void LoadWithDefaults_AddsMissingProvidersWithoutOverwriting()
    {
        var existing = new ProviderConfig(ProviderKind.DeepSeek) { IsEnabled = true, ApiKey = "sk-keep" };
        _store.Save(new SharedData { Configs = [existing] });

        var data = _store.LoadWithDefaults();
        Assert.Equal(3, data.Configs.Count);
        var deep = data.Configs.Single(c => c.Kind == ProviderKind.DeepSeek);
        Assert.True(deep.IsEnabled); // 已有配置不覆盖
        Assert.Equal("sk-keep", deep.ApiKey);
        Assert.Contains(data.Configs, c => c.Kind == ProviderKind.Kimi && !c.IsEnabled);
    }

    [Fact]
    public void UnknownProviderKind_SkippedNotFatal()
    {
        File.WriteAllText(SharedPath,
            """{"configs":[{"kind":"openai","apiKey":"x"},{"kind":"glm","isEnabled":true}]}""");
        var data = _store.Load();
        var glm = Assert.Single(data.Configs);
        Assert.Equal(ProviderKind.Glm, glm.Kind);
    }

    [Fact]
    public void CachedUsages_RoundTrip()
    {
        var usage = new ProviderUsage
        {
            Kind = ProviderKind.Glm,
            DisplayName = "GLM Coding",
            State = UsageState.Error,
            ErrorMessage = "网络连接失败，请检查网络",
            Windows =
            [
                new UsageWindow { Title = "5 小时", UsedPercent = 88.4, ResetTime = DateTimeOffset.Parse("2026-01-09T15:23:13Z") },
                new UsageWindow { Title = "30 天累计", UsedText = "5524万 tokens · 99 次" },
            ],
            FetchedAt = DateTimeOffset.Parse("2026-01-09T10:00:00Z"),
        };
        _store.SaveCachedUsages([usage]);

        var loaded = _store.Load().CachedUsages;
        var round = Assert.Single(loaded);
        Assert.Equal(UsageState.Error, round.State);
        Assert.Equal("网络连接失败，请检查网络", round.ErrorMessage);
        Assert.Equal(88.4, round.Windows[0].UsedPercent);
        Assert.Equal("5524万 tokens · 99 次", round.Windows[1].UsedText);
    }

    [Fact]
    public void SetAlertThresholdAndReset_ClearsAlertedKeys()
    {
        _store.ReplaceAlertedKeys(["kimi-5 小时"]);
        _store.SetAlertThresholdAndReset(95);
        var data = _store.Load();
        Assert.Equal(95, data.AlertThreshold);
        Assert.Empty(data.AlertedKeys);
    }

    [Fact]
    public void InvalidThreshold_FallsBackToDefault()
    {
        File.WriteAllText(SharedPath, """{"alertThreshold": 0}""");
        Assert.Equal(80, _store.Load().AlertThreshold);

        File.WriteAllText(SharedPath, """{"alertThreshold": 300}""");
        Assert.Equal(80, _store.Load().AlertThreshold);
    }

    [Fact]
    public void LegacyPlaintextApiKey_AcceptedOnLoad()
    {
        File.WriteAllText(SharedPath, """{"configs":[{"kind":"deepseek","apiKey":"sk-legacy"}]}""");
        var data = _store.Load();
        Assert.Equal("sk-legacy", data.Configs.Single().ApiKey);
    }
}
