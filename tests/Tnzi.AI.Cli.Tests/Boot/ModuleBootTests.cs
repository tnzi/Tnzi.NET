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
        // 没有消费方注册别的宿主时，本机宿主是默认（在 PostConfigure 阶段登记）
        provider.GetRequiredService<ICliHost>().ShouldBeOfType<LocalProcessHost>();
        provider.GetRequiredService<ICliBriefComposer>().ShouldNotBeNull();
        provider.GetRequiredService<ICliMcpConfigComposer>().ShouldNotBeNull();
        provider.GetRequiredService<ICliWorkspacePreparer>().ShouldNotBeNull();
        provider.GetRequiredService<CliRunSignalHub>().ShouldNotBeNull();
        provider.GetRequiredService<CliRunCancellationRegistry>().ShouldNotBeNull();
        provider.GetRequiredService<ICliAuthStatusProbe>().ShouldNotBeNull();
        provider.GetRequiredService<ICliLaunchEnvironmentComposer>().ShouldNotBeNull();
    }

    /// <summary>
    /// The executor needs repositories, so it cannot be resolved here; instead every
    /// required constructor dependency must at least be registered. A collaborator added
    /// to the constructor but not to the module fails the first claimed run, not startup.
    /// </summary>
    [Fact]
    public async Task ModuleGraph_RegistersEveryRequiredExecutorDependency()
    {
        var (provider, services) = await ComposeAsync(enabled: true);
        await using var _ = provider;

        var constructor = typeof(CliRunExecutor).GetConstructors().ShouldHaveSingleItem();
        var missing = constructor.GetParameters()
            .Where(p => !p.HasDefaultValue)
            .Select(p => p.ParameterType)
            // Repositories come from the database module, which this boot deliberately omits.
            .Where(type => !(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IRepository<,>)))
            // The container itself provides these; they never appear as descriptors.
            .Where(type => type != typeof(IServiceScopeFactory) && type != typeof(IServiceProvider))
            .Where(type => !services.Any(d => d.ServiceType == type
                || (type.IsGenericType && d.ServiceType == type.GetGenericTypeDefinition())))
            .Select(type => type.Name)
            .ToList();

        missing.ShouldBeEmpty();
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
    public async Task HostedServices_WithTheModuleDisabled_IdleWithoutFaulting_AndStopPromptly()
    {
        // This is the default configuration, so it is the one that must not break a host.
        // While AI:Cli:Enabled is off the three services idle and re-read the switch on every
        // iteration (switching it on at runtime starts them without a restart), so they do not
        // return on their own. What must hold is that starting them does not break the host,
        // idling never faults them, and stopping the host ends them promptly.
        //
        // The test used to wait for ExecuteAsync to return by itself, which was right while the
        // services exited when disabled. Since they idle, it only passed because the 60 second
        // start token is linked to the stopping token; under load the check could run before the
        // loop had observed that cancellation, and every run spent a full minute here.
        await using var provider = (await ComposeAsync(enabled: false)).Provider;

        var hosted = provider.GetServices<IHostedService>().OfType<BackgroundService>()
            // 只数本模块自己的三个：模块图里的 Tnzi.AI 也带 BackgroundService（AgentDefinitionSyncService），它不受 AI:Cli:Enabled 门控。
            .Where(s => s.GetType().Namespace?.StartsWith("Tnzi.AI.Cli", StringComparison.Ordinal) == true)
            .ToList();
        hosted.Count.ShouldBe(3);

        foreach (var service in hosted)
        {
            await service.StartAsync(CancellationToken.None);
        }

        // Give each loop time to run at least one idle iteration.
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        foreach (var service in hosted)
        {
            Assert.NotNull(service.ExecuteTask);
            service.ExecuteTask.IsFaulted.ShouldBeFalse($"{service.GetType().Name} faulted while AI:Cli:Enabled=false");
        }

        // The bound is for a loaded machine: each StopAsync returns the moment its loop ends.
        using var stopBound = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        foreach (var service in hosted)
        {
            await service.StopAsync(stopBound.Token);
            service.ExecuteTask!.IsCompletedSuccessfully.ShouldBeTrue(
                $"{service.GetType().Name} did not stop when the host stopped");
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
