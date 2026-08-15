namespace Tnzi.AI.Cli.Tests;

/// <summary>A host that loads nothing but this module and whatever it pulls in.</summary>
[DependsOn(typeof(AICliModule))]
internal sealed class CliBootStartupModule : TnziApplicationModule
{
}

/// <summary>
/// Loads the module the way a real application does.
/// </summary>
/// <remarks>
/// Everything else in this suite tests a class in isolation. Nothing exercised
/// <b>module composition</b>: the module registers 19 services and three
/// <see cref="BackgroundService"/>s, and no application in this repository loads it
/// (the reference app does not <c>[DependsOn]</c> it), so until this file existed the whole
/// wiring had never run once.
/// <para>
/// The failure it guards against does not show up in a build or in a unit test:
/// a hosted service that throws in <c>StartAsync</c>, or a service whose
/// dependencies cannot be resolved, takes down host startup - and the module is
/// meant to be safe to load while switched off, which is the default.
/// </para>
/// </remarks>
public class ModuleBootTests
{
    private static IConfiguration BuildConfiguration(bool enabled)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // No DbContext discovery: this test is about module composition and the
                // hosted-service lifecycle, and the repository layer already has its own
                // SQLite-backed coverage.
                ["Database:AutoDiscoverDbContexts"] = "false",
                ["AI:Cli:Enabled"] = enabled ? "true" : "false"
            })
            .Build();

    private static async Task<(ServiceProvider Provider, IServiceCollection Services)> ComposeAsync(bool enabled)
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(enabled);
        await services.AddTnziAsync<CliBootStartupModule>(configuration);
        return (services.BuildServiceProvider(), services);
    }

    [Fact]
    public async Task ModuleGraph_Composes_AndResolvesTheModuleOwnServices()
    {
        await using var provider = (await ComposeAsync(enabled: true)).Provider;

        // The stateless collaborators are resolvable on their own; anything that needs a
        // repository is left to the SQLite-backed tests, which already cover it.
        provider.GetRequiredService<ICliProviderRegistry>().ShouldNotBeNull();
        provider.GetRequiredService<ICliProtocolAdapterFactory>().ShouldNotBeNull();
        provider.GetRequiredService<ICliProcessHost>().ShouldNotBeNull();
        provider.GetRequiredService<ICliExecutableResolver>().ShouldNotBeNull();
        provider.GetRequiredService<ICliBriefComposer>().ShouldNotBeNull();
        provider.GetRequiredService<ICliMcpConfigComposer>().ShouldNotBeNull();
        provider.GetRequiredService<ICliWorkspacePreparer>().ShouldNotBeNull();
        provider.GetRequiredService<CliRunSignalHub>().ShouldNotBeNull();
        provider.GetRequiredService<CliRunCancellationRegistry>().ShouldNotBeNull();
    }

    [Fact]
    public async Task ModuleGraph_RegistersTheDispatchFacade_OverTheBuiltInOnlyFallback()
    {
        // Loading this module is what makes the facade able to route anywhere. If the
        // core fallback were still winning, every agent would silently run built-in and
        // the whole module would be inert with no error anywhere.
        var (provider, services) = await ComposeAsync(enabled: true);
        await using var _ = provider;

        // Both assertions read the descriptors rather than resolving. These types sit on
        // top of repositories, so constructing them would need a DbContext - and what the
        // test is actually asking is which implementation won the registration race,
        // which the descriptor answers directly.
        services.ShouldContain(d => d.ServiceType == typeof(IAgentDispatchFacade));

        services.LastOrDefault(d => d.ServiceType == typeof(ICliAgentBindingService))
            ?.ImplementationType
            .ShouldBe(
                typeof(CliAgentBindingService),
                "the core BuiltInOnly fallback is still winning, which would silently route " +
                "every agent to built-in execution and leave the module inert with no error");
    }

    [Fact]
    public async Task HostedServices_WithTheModuleDisabled_StartAndStopWithoutDoingWork()
    {
        // This is the default configuration, so it is the one that must not break a host.
        // All three self-gate and return immediately; if one of them started polling, or
        // threw because no database is configured, host startup would fail for every
        // deployment that merely references the package.
        await using var provider = (await ComposeAsync(enabled: false)).Provider;

        var hosted = provider.GetServices<IHostedService>().OfType<BackgroundService>().ToList();
        hosted.Count.ShouldBe(3);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        foreach (var service in hosted)
        {
            await service.StartAsync(cts.Token);
        }

        // Returning immediately is the observable form of "gated off". Waiting on the
        // task is what makes this an assertion rather than a hope: a service that ignored
        // the switch would still be running here.
        foreach (var service in hosted)
        {
            var executeTask = service.ExecuteTask;
            Assert.NotNull(executeTask);
            await executeTask.WaitAsync(TimeSpan.FromSeconds(5), cts.Token);
            executeTask.IsCompletedSuccessfully.ShouldBeTrue(
                $"{service.GetType().Name} kept running while AI:Cli:Enabled=false");
        }

        foreach (var service in hosted)
        {
            await service.StopAsync(cts.Token);
        }
    }

    [Fact]
    public async Task Options_AreBoundAndValidated_FromConfiguration()
    {
        await using var provider = (await ComposeAsync(enabled: true)).Provider;

        var options = provider.GetRequiredService<IOptionsMonitor<CliAgentOptions>>();
        options.CurrentValue.Enabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Permissions_AreDeclaredByTheModule()
    {
        // The codes ship with the module rather than the framework catalogue, so a host
        // that does not load it never seeds them. That only works if the provider is
        // actually registered.
        await using var provider = (await ComposeAsync(enabled: true)).Provider;

        provider.GetServices<IPermissionDefinitionProvider>()
            .ShouldContain(p => p is CliPermissions);
    }
}
