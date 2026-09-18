namespace Tnzi.AI.Tests.Modules;

/// <summary>
/// DI wiring gate for skill-constraint enforcement. Unit tests construct the middlewares by hand,
/// so a missing registration would leave every one of them green while production enforced nothing
/// (that is exactly how the constraint pipeline stayed dead for months). This test asks the module
/// graph what it actually registers.
/// </summary>
public class SkillConstraintWiringTests
{
    [Fact]
    public void AISkillsModule_RegistersTheToolExecutionMiddleware()
    {
        var services = BuildServices();

        var descriptors = services.Where(d => d.ServiceType == typeof(IToolExecutionMiddleware)).ToList();

        descriptors.ShouldContain(d => d.ImplementationType == typeof(SkillConstraintToolMiddleware),
            "the tool-execution middleware is the enforcement point; without it the AI middleware only hides tools from the model");
        descriptors.Single(d => d.ImplementationType == typeof(SkillConstraintToolMiddleware)).Lifetime.ShouldBe(ServiceLifetime.Scoped);
    }

    [Fact]
    public void AIModule_RegistersOneScopedActivationTracker_SharedByProviderAndMiddlewares()
    {
        var services = BuildServices();

        var descriptor = services.Single(d => d.ServiceType == typeof(ISkillActivationTracker));
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Scoped, "activation state must be shared across every tool call of one request and must not leak between requests");
        descriptor.ImplementationType.ShouldBe(typeof(SkillActivationTracker));
    }

    [Fact]
    public void SkillConstraintMiddleware_ResolvesWithTheScopedTracker()
    {
        var services = BuildServices();
        // The tracker reaches the thread store and the skill registry, which sit on repositories;
        // stub only those so the resolution exercises the real registrations in between.
        services.AddScoped(_ => Mock.Of<IRepository<SkillEntity, Guid>>());
        services.AddScoped(_ => Mock.Of<IRepository<Entities.AgentThread, Guid>>());
        services.AddScoped(_ => Mock.Of<IRepository<AgentThreadMessage, Guid>>());
        services.AddScoped(_ => Mock.Of<IRepository<Agent, Guid>>());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Both middlewares must be constructible from the scope (their dependencies are all registered)...
        scope.ServiceProvider.GetRequiredService<SkillConstraintMiddleware>().ShouldNotBeNull();
        ActivatorUtilities.CreateInstance<SkillConstraintToolMiddleware>(scope.ServiceProvider).ShouldNotBeNull();

        // ...and the tracker they share is one instance per scope, never across scopes.
        var tracker = scope.ServiceProvider.GetRequiredService<ISkillActivationTracker>();
        scope.ServiceProvider.GetRequiredService<ISkillActivationTracker>().ShouldBeSameAs(tracker);
        using var otherScope = provider.CreateScope();
        otherScope.ServiceProvider.GetRequiredService<ISkillActivationTracker>().ShouldNotBeSameAs(tracker);
    }

    private static IServiceCollection BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Providers:openai:ApiKey"] = "test-key",
                ["AI:Providers:openai:DefaultModel"] = "gpt-4o"
            })
            .Build();
        TestHelpers.ConfigureModules(services, configuration, [new AIModule(), new AISkillsModule()]);
        return services;
    }
}
