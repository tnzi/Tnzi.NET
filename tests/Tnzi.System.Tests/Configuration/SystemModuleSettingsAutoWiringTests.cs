using Tnzi.Modules;
using Tnzi.System.Configuration;

namespace Tnzi.System.Tests.Configuration;

/// <summary>
/// Tests for SystemModule's automatic SettingConfigurationSource wiring: when the host
/// configuration is a ConfigurationManager (the TnziApp flow), PreConfigureServicesAsync
/// registers the source itself so Program.cs no longer needs to call AddTnziSettings().
/// </summary>
public class SystemModuleSettingsAutoWiringTests
{
    private static ServiceConfigurationContext CreateContext(IConfiguration configuration)
        => new(new ServiceCollection(), configuration, "Development");

    [Fact]
    public async Task PreConfigure_AutoRegistersSource_WhenConfigurationIsBuilder()
    {
        var configuration = new ConfigurationManager();
        var module = new SystemModule();

        await module.PreConfigureServicesAsync(CreateContext(configuration));

        ((IConfigurationBuilder)configuration).Sources
            .OfType<SettingConfigurationSource>().Count().ShouldBe(1);
    }

    [Fact]
    public async Task PreConfigure_OptOutKey_SkipsAutoRegistration()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["System:Settings:EnableConfigurationSource"] = "false"
        });
        var module = new SystemModule();

        await module.PreConfigureServicesAsync(CreateContext(configuration));

        ((IConfigurationBuilder)configuration).Sources
            .OfType<SettingConfigurationSource>().ShouldBeEmpty();
    }

    [Fact]
    public async Task PreConfigure_ManualRegistrationFirst_IsIdempotentAndKeepsExcludedKeys()
    {
        var configuration = new ConfigurationManager();
        ((IConfigurationBuilder)configuration).AddTnziSettings("My:ProtectedKey");
        var module = new SystemModule();

        await module.PreConfigureServicesAsync(CreateContext(configuration));

        var source = ((IConfigurationBuilder)configuration).Sources
            .OfType<SettingConfigurationSource>().Single();
        source.ExcludedKeys.ShouldContain("My:ProtectedKey");
    }

    [Fact]
    public async Task PreConfigure_NonBuilderConfiguration_SkipsQuietly()
    {
        // Hosts that pass an already-built IConfigurationRoot (direct AddTnziAsync) cannot
        // be auto-wired; the module must skip without throwing (startup warning covers it).
        var configuration = new ConfigurationBuilder().Build();
        var module = new SystemModule();

        await Should.NotThrowAsync(() => module.PreConfigureServicesAsync(CreateContext(configuration)));

        ((IConfiguration)configuration).GetTnziSettingsSource().ShouldBeNull();
    }
}
