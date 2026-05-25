using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Tnzi.System.Configuration;
using Tnzi.Utilities;

namespace Tnzi.System.Tests.Configuration;

/// <summary>
/// Tests for SettingConfigurationSource / Provider — verifies excluded-key filter,
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

        // Reload contract requires a stable provider instance — SystemModule keeps
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

    [Fact]
    public async Task Reload_BeforeAttach_SilentNoOp()
    {
        var builder = new ConfigurationBuilder();
        builder.AddTnziSettings();
        var source = builder.Sources.OfType<SettingConfigurationSource>().Single();
        builder.Build();

        // Without AttachAsync, Reload must not throw — startup race tolerance.
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
