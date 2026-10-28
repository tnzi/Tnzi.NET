using Microsoft.Extensions.Primitives;
using Tnzi.System.Configuration;

namespace Tnzi.System.Tests.Configuration;

/// <summary>
/// Tests for SettingConfigurationSource / Provider - verifies excluded-key filter,
/// reload OnChange propagation, and encrypted-setting exclusion.
/// </summary>
public class SettingConfigurationSourceTests
{
    [Fact]
    public void AddTnziSettings_Idempotent_ReturnsSameSourceInstance()
    {
        var builder = new ConfigurationBuilder();
        builder.AddTnziSettings();
        builder.AddTnziSettings();    // second call should be no-op

        // Each .Add() registers a source; idempotent helper should not double-register
        var settingSources = builder.Sources.OfType<SettingConfigurationSource>().ToList();
        settingSources.Count.ShouldBe(1);
    }

    [Fact]
    public void AddTnziSettings_PassesExcludedKeysToSource()
    {
        var builder = new ConfigurationBuilder();
        builder.AddTnziSettings("Outreach:IsTestMode", "Outreach:TestRedirectEmail");

        var source = builder.Sources.OfType<SettingConfigurationSource>().Single();
        source.ExcludedKeys.ShouldContain("Outreach:IsTestMode");
        source.ExcludedKeys.ShouldContain("Outreach:TestRedirectEmail");
        source.ExcludedKeys.ShouldNotContain("Outreach:MailboxSyncBatchSize");
    }

    [Fact]
    public void AddTnziSettings_ExcludedKeysIsCaseInsensitive()
    {
        var builder = new ConfigurationBuilder();
        builder.AddTnziSettings("Outreach:IsTestMode");

        var source = builder.Sources.OfType<SettingConfigurationSource>().Single();
        source.ExcludedKeys.ShouldContain("outreach:istestmode");    // case-insensitive lookup
        source.ExcludedKeys.ShouldContain("OUTREACH:ISTESTMODE");
    }

    [Fact]
    public void Build_ReturnsSameProviderInstanceOnRepeatedCalls()
    {
        var builder = new ConfigurationBuilder();
        builder.AddTnziSettings();
        var source = builder.Sources.OfType<SettingConfigurationSource>().Single();

        var p1 = source.Build(builder);
        var p2 = source.Build(builder);

        // Reload contract requires a stable provider instance - SystemModule keeps
        // a reference and triggers ReloadFromDatabaseAsync on the same provider.
        p1.ShouldBeSameAs(p2);
    }

    [Fact]
    public void GetTnziSettingsSource_FindsSourceFromConfigurationRoot()
    {
        var builder = new ConfigurationBuilder();
        builder.AddTnziSettings("ProtectedKey");

        var root = builder.Build();
        var found = ((IConfiguration)root).GetTnziSettingsSource();

        found.ShouldNotBeNull();
        found.ExcludedKeys.ShouldContain("ProtectedKey");
    }

    [Fact]
    public void GetTnziSettingsSource_ReturnsNullWhenNotRegistered()
    {
        var builder = new ConfigurationBuilder();
        var root = builder.Build();
        var found = ((IConfiguration)root).GetTnziSettingsSource();
        found.ShouldBeNull();
    }

    [Fact]
    public async Task ReloadFromDatabase_PopulatesDataAndFiltersExcludedAndEncrypted()
    {
        var dbSettings = new List<Setting>
        {
            new() { Key = "Outreach:MailboxSyncBatchSize", Value = "250", Scope = SettingScope.Global, IsEncrypted = false },
            new() { Key = "Outreach:MailboxSyncMonths", Value = "9", Scope = SettingScope.Global, IsEncrypted = false },
            new() { Key = "Outreach:IsTestMode", Value = "false", Scope = SettingScope.Global, IsEncrypted = false },    // excluded
            new() { Key = "Secret:ApiKey", Value = "ciphertext", Scope = SettingScope.Global, IsEncrypted = true },     // encrypted, filtered
            new() { Key = "Tenant:Key", Value = "x", Scope = SettingScope.Tenant, IsEncrypted = false },                  // wrong scope, filtered
        };
        var queryable = dbSettings.BuildMock();
        var repoMock = new Mock<IRepository<Setting, Guid>>();
        repoMock.Setup(r => r.AsQueryable()).Returns(queryable);

        var services = new ServiceCollection();
        services.AddSingleton(repoMock.Object);
        services.AddLogging();
        var sp = services.BuildServiceProvider();

        // Set up source + provider via the public extension to exercise the real path
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddTnziSettings("Outreach:IsTestMode");
        var source = configBuilder.Sources.OfType<SettingConfigurationSource>().Single();
        var root = configBuilder.Build();    // triggers Source.Build() → Provider constructed

        var reloadFired = 0;
        using var registration = ChangeToken.OnChange(
            () => root.GetReloadToken(),
            () => Interlocked.Increment(ref reloadFired));

        await source.AttachAsync(sp);

        // Verify excluded + encrypted + non-Global are filtered out
        root["Outreach:MailboxSyncBatchSize"].ShouldBe("250");
        root["Outreach:MailboxSyncMonths"].ShouldBe("9");
        root["Outreach:IsTestMode"].ShouldBeNull();
        root["Secret:ApiKey"].ShouldBeNull();
        root["Tenant:Key"].ShouldBeNull();

        // Reload token must have fired so subscribers (IOptionsMonitor) propagate
        reloadFired.ShouldBeGreaterThan(0);
    }

    private static (SettingConfigurationSource Source, IConfigurationRoot Root, Mock<IRepository<Setting, Guid>> Repository, IServiceProvider Services)
        Arrange(IEnumerable<Setting> rows, params string[] excludedKeys)
    {
        var repoMock = new Mock<IRepository<Setting, Guid>>();
        repoMock.Setup(r => r.AsQueryable()).Returns(rows.ToList().BuildMock());

        var services = new ServiceCollection();
        services.AddSingleton(repoMock.Object);
        services.AddLogging();

        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddTnziSettings(excludedKeys);
        var source = configBuilder.Sources.OfType<SettingConfigurationSource>().Single();
        return (source, configBuilder.Build(), repoMock, services.BuildServiceProvider());
    }

    /// <summary>
    /// 排除项挡住它下面的整棵子树（IConfiguration 的节语义）。文档一直让宿主「排除 <c>Appearance:Theme:</c> 前缀」，
    /// 而实现此前只做精确匹配：没有任何键等于这个字符串，每份最大 64 KB 的主题快照照常进入 IConfiguration，且没有任何提示。
    /// </summary>
    [Theory]
    [InlineData("Appearance:Theme")]
    [InlineData("Appearance:Theme:")]
    [InlineData("appearance:theme")]
    public async Task An_excluded_section_keeps_its_whole_subtree_out(string excludedEntry)
    {
        var (source, root, _, services) = Arrange(
        [
            new() { Key = "Appearance:Theme:admin", Value = "{}", Scope = SettingScope.Global },
            new() { Key = "Appearance:Theme:chat", Value = "{}", Scope = SettingScope.Global },
            new() { Key = "Appearance:Theme", Value = "root", Scope = SettingScope.Global },
            new() { Key = "Appearance:ThemeColor", Value = "blue", Scope = SettingScope.Global },
            new() { Key = "Mail:Host", Value = "smtp.example.com", Scope = SettingScope.Global },
        ], excludedEntry);

        await source.AttachAsync(services);

        root["Appearance:Theme:admin"].ShouldBeNull();
        root["Appearance:Theme:chat"].ShouldBeNull();
        root["Appearance:Theme"].ShouldBeNull();
        root["Appearance:ThemeColor"].ShouldBe("blue");
        root["Mail:Host"].ShouldBe("smtp.example.com");
    }

    /// <summary>
    /// 变更后的重载读库失败要让事件处理器知道（事件总线据此重试），配置保持上一次成功读到的值。
    /// 此前一律只记一条 Warning：IOptionsMonitor 停在旧值直到下一次任意设置变更或重启，而事件总线以为处理成功。
    /// </summary>
    [Fact]
    public async Task A_reload_that_cannot_read_the_database_throws_and_keeps_the_previous_values()
    {
        var (source, root, repository, services) = Arrange([new() { Key = "Audit:RetentionDays", Value = "30", Scope = SettingScope.Global }]);
        await source.AttachAsync(services);
        root["Audit:RetentionDays"].ShouldBe("30");

        repository.Setup(r => r.AsQueryable()).Throws(new InvalidOperationException("database unavailable"));

        await Should.ThrowAsync<InvalidOperationException>(() => source.ReloadAsync());
        root["Audit:RetentionDays"].ShouldBe("30");
    }

    /// <summary>启动期的首次填充仍然只记日志、不打断启动（沿用 appsettings 的值）。</summary>
    [Fact]
    public async Task The_startup_load_that_cannot_read_the_database_does_not_throw()
    {
        var (source, _, repository, services) = Arrange([]);
        repository.Setup(r => r.AsQueryable()).Throws(new InvalidOperationException("database unavailable"));

        await Should.NotThrowAsync(() => source.AttachAsync(services));
    }

    [Fact]
    public async Task Reload_BeforeAttach_SilentNoOp()
    {
        var builder = new ConfigurationBuilder();
        builder.AddTnziSettings();
        var source = builder.Sources.OfType<SettingConfigurationSource>().Single();
        builder.Build();

        // Without AttachAsync, Reload must not throw - startup race tolerance.
        await Should.NotThrowAsync(async () => await source.ReloadAsync());
    }

    [Fact]
    public async Task AttachAsync_BeforeBuild_NoOpAndDoesNotThrow()
    {
        // Edge case: someone calls AttachAsync on a source that was never added to a builder
        var source = new SettingConfigurationSource();
        var services = new ServiceCollection().BuildServiceProvider();
        await Should.NotThrowAsync(async () => await source.AttachAsync(services));
    }
}
