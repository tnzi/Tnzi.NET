using Tnzi.AI.Channels.Controllers.Admin;
using Tnzi.AI.Channels.Dtos;
using Tnzi.AI.Channels.Entities;
using Tnzi.AI.Channels.Gateway;
using Tnzi.AI.Channels.Gateway.Models;

namespace Tnzi.AI.Tests.Channels.Gateway;

/// <summary>
/// The admin bindings view lists the rules the session binder actually routes with: configuration rules
/// (<c>AI:Channels:Gateway:BindingRules</c>) and database rules, in the binder's order. Listing only database
/// rows showed "0 bindings" while configured rules were routing every message.
/// </summary>
public class GatewayAdminBindingsTests
{
    private static readonly Guid ConfigAgent = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid DatabaseAgent = Guid.Parse("22222222-0000-0000-0000-000000000002");
    private static readonly Guid DatabaseRuleId = Guid.Parse("33333333-0000-0000-0000-000000000003");

    [Fact]
    public async Task GetBindings_ConfigRulesOnly_AreListedWithTheirSource()
    {
        var controller = CreateController(
            configRules: [new SessionBindingRule { Channel = "slack", AgentId = ConfigAgent, Scope = SessionScope.PerPeer, Priority = 5, IsEnabled = true }],
            databaseRules: null);

        var rules = (await controller.GetBindings()).Data.ShouldNotBeNull();

        var rule = rules.ShouldHaveSingleItem();
        rule.Source.ShouldBe(SessionBindingRuleSources.Config);
        rule.Channel.ShouldBe("slack");
        rule.AgentId.ShouldBe(ConfigAgent);
        rule.Id.ShouldBe(Guid.Empty);
    }

    [Fact]
    public async Task GetBindings_ConfigAndDatabaseRules_AreMergedInTheBindersOrder()
    {
        var controller = CreateController(
            configRules:
            [
                new SessionBindingRule { Channel = "slack", AgentId = ConfigAgent, Scope = SessionScope.PerPeer, Priority = 5, IsEnabled = true },
                new SessionBindingRule { Channel = "discord", AgentId = ConfigAgent, Scope = SessionScope.Global, Priority = 1, IsEnabled = true }
            ],
            databaseRules:
            [
                new SessionBindingRule { Id = DatabaseRuleId, Channel = "telegram", AgentId = DatabaseAgent, Scope = SessionScope.PerThread, Priority = 5, IsEnabled = false },
                new SessionBindingRule { Channel = "feishu", AgentId = DatabaseAgent, Scope = SessionScope.PerPeer, Priority = 9, IsEnabled = true }
            ]);

        var rules = (await controller.GetBindings()).Data.ShouldNotBeNull();

        // Priority descending; at equal priority the configuration rule comes first, as in DefaultSessionBinder
        rules.Select(r => (r.Channel, r.Source)).ShouldBe(
        [
            ("feishu", SessionBindingRuleSources.Database),
            ("slack", SessionBindingRuleSources.Config),
            ("telegram", SessionBindingRuleSources.Database),
            ("discord", SessionBindingRuleSources.Config)
        ]);

        var databaseRule = rules.Single(r => r.Channel == "telegram");
        databaseRule.Id.ShouldBe(DatabaseRuleId);
        databaseRule.IsEnabled.ShouldBeFalse();
        databaseRule.Scope.ShouldBe(SessionScope.PerThread);
    }

    private static DefaultGatewayAdminController CreateController(
        IReadOnlyList<SessionBindingRule> configRules, List<SessionBindingRule>? databaseRules)
    {
        Mock<IRepository<SessionBindingRule, Guid>>? repository = null;
        if (databaseRules != null)
        {
            repository = new Mock<IRepository<SessionBindingRule, Guid>>();
            repository
                .Setup(r => r.ToListAsync(It.IsAny<Expression<Func<SessionBindingRule, bool>>?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(databaseRules);
        }

        return new DefaultGatewayAdminController(
            new Mock<IGateway>().Object,
            new Mock<IPresenceTracker>().Object,
            bindingRuleRepository: repository?.Object,
            configBindingRules: configRules);
    }
}
