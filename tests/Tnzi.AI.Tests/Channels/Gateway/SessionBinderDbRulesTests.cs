using Tnzi.AI.Channels.Entities;
using Tnzi.AI.Channels.Gateway;
using Tnzi.AI.Channels.Gateway.Models;
using Tnzi.AI.Channels.Options;

namespace Tnzi.AI.Tests.Channels.Gateway;

/// <summary>
/// PD-14 — DefaultSessionBinder must ALSO honor enabled DB SessionBindingRule rows
/// (in addition to config rules), loaded once and cached (no per-Resolve DB hit).
/// </summary>
public class SessionBinderDbRulesTests
{
    private static readonly Guid DbAgentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ConfigAgentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DefaultAgentId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static (Mock<IServiceScopeFactory> scopeFactory, Mock<IRepository<SessionBindingRule, Guid>> repo) CreateScopeFactory(
        List<SessionBindingRule> dbRules)
    {
        var repo = new Mock<IRepository<SessionBindingRule, Guid>>();
        // ToListAsync(predicate, ct) is a real IReadOnlyRepository interface method (mockable);
        // the parameterless ToListAsync(ct) is an EF extension method and cannot be mocked.
        repo.Setup(r => r.ToListAsync(It.IsAny<System.Linq.Expressions.Expression<Func<SessionBindingRule, bool>>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dbRules);

        var sp = new Mock<IServiceProvider>();
        sp.Setup(s => s.GetService(typeof(IRepository<SessionBindingRule, Guid>))).Returns(repo.Object);

        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(sp.Object);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        return (scopeFactory, repo);
    }

    private static SessionBindingContext Ctx(string channel = "telegram", string userId = "u1")
        => new() { Channel = channel, ChatId = "c1", UserId = userId };

    [Fact]
    public void Resolve_EnabledDbRule_IsHonored()
    {
        // Arrange — one enabled DB rule for telegram → DbAgentId
        var dbRules = new List<SessionBindingRule>
        {
            new() { Channel = "telegram", AgentId = DbAgentId, Scope = SessionScope.PerPeer, Priority = 10, IsEnabled = true }
        };
        var (scopeFactory, _) = CreateScopeFactory(dbRules);
        var options = Microsoft.Extensions.Options.Options.Create(new GatewayOptions { DefaultAgentId = DefaultAgentId });

        var binder = new DefaultSessionBinder([], options, scopeFactory.Object);

        // Act
        var binding = binder.Resolve(Ctx());

        // Assert — DB rule honored, not the default agent
        binding.AgentId.ShouldBe(DbAgentId);
    }

    [Fact]
    public void Resolve_DisabledDbRule_Ignored()
    {
        var dbRules = new List<SessionBindingRule>
        {
            new() { Channel = "telegram", AgentId = DbAgentId, Scope = SessionScope.PerPeer, Priority = 10, IsEnabled = false }
        };
        var (scopeFactory, _) = CreateScopeFactory(dbRules);
        var options = Microsoft.Extensions.Options.Options.Create(new GatewayOptions { DefaultAgentId = DefaultAgentId });

        var binder = new DefaultSessionBinder([], options, scopeFactory.Object);

        var binding = binder.Resolve(Ctx());

        binding.AgentId.ShouldBe(DefaultAgentId);
    }

    [Fact]
    public void Resolve_ConfigRuleWins_OnEqualPriority()
    {
        // Arrange — config + DB rule both match telegram at priority 10. Config must win.
        var configRules = new List<SessionBindingRule>
        {
            new() { Channel = "telegram", AgentId = ConfigAgentId, Scope = SessionScope.PerPeer, Priority = 10, IsEnabled = true }
        };
        var dbRules = new List<SessionBindingRule>
        {
            new() { Channel = "telegram", AgentId = DbAgentId, Scope = SessionScope.PerPeer, Priority = 10, IsEnabled = true }
        };
        var (scopeFactory, _) = CreateScopeFactory(dbRules);
        var options = Microsoft.Extensions.Options.Options.Create(new GatewayOptions { DefaultAgentId = DefaultAgentId });

        var binder = new DefaultSessionBinder(configRules, options, scopeFactory.Object);

        var binding = binder.Resolve(Ctx());

        binding.AgentId.ShouldBe(ConfigAgentId);
    }

    [Fact]
    public void Resolve_DbRulesQueriedOnce_NotPerResolve()
    {
        // Arrange
        var dbRules = new List<SessionBindingRule>
        {
            new() { Channel = "telegram", AgentId = DbAgentId, Scope = SessionScope.PerPeer, Priority = 10, IsEnabled = true }
        };
        var (scopeFactory, repo) = CreateScopeFactory(dbRules);
        var options = Microsoft.Extensions.Options.Options.Create(new GatewayOptions { DefaultAgentId = DefaultAgentId });

        // Long TTL so no refresh happens between resolves.
        var binder = new DefaultSessionBinder([], options, scopeFactory.Object, cacheTtl: TimeSpan.FromHours(1));

        // Act — many resolves
        for (var i = 0; i < 25; i++)
        {
            binder.Resolve(Ctx(userId: $"u{i}"));
        }

        // Assert — the DB was queried at most once (cached), NOT per-resolve.
        scopeFactory.Verify(f => f.CreateScope(), Times.AtMostOnce);
        repo.Verify(r => r.ToListAsync(
            It.IsAny<System.Linq.Expressions.Expression<Func<SessionBindingRule, bool>>?>(),
            It.IsAny<CancellationToken>()), Times.AtMostOnce);
    }

    [Fact]
    public void Resolve_NoScopeFactory_FallsBackToConfigOnly()
    {
        // Arrange — no scope factory (DB rules unavailable); config rule still works.
        var configRules = new List<SessionBindingRule>
        {
            new() { Channel = "telegram", AgentId = ConfigAgentId, Scope = SessionScope.PerPeer, Priority = 5, IsEnabled = true }
        };
        var options = Microsoft.Extensions.Options.Options.Create(new GatewayOptions { DefaultAgentId = DefaultAgentId });

        var binder = new DefaultSessionBinder(configRules, options, scopeFactory: null);

        var binding = binder.Resolve(Ctx());

        binding.AgentId.ShouldBe(ConfigAgentId);
    }
}
