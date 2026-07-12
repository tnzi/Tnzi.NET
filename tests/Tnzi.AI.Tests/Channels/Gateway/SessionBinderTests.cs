using Tnzi.AI.Channels.Entities;
using Tnzi.AI.Channels.Gateway;
using Tnzi.AI.Channels.Gateway.Models;
using Tnzi.AI.Channels.Options;

namespace Tnzi.AI.Tests.Channels.Gateway;

public class SessionBinderTests
{
    private static readonly Guid DefaultAgentId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid RuleAgentId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid HighPriorityAgentId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static IOptionsMonitor<GatewayOptions> CreateOptions(Guid? defaultAgentId = null, SessionScope defaultScope = SessionScope.PerPeer)
    {
        return new StaticOptionsMonitor<GatewayOptions>(new GatewayOptions
        {
            DefaultAgentId = defaultAgentId ?? DefaultAgentId,
            DefaultScope = defaultScope
        });
    }

    private static SessionBindingContext CreateContext(
        string channel = "telegram",
        string chatId = "chat-1",
        string userId = "user-1",
        string? peerKind = "user",
        string? explicitAgentId = null)
    {
        return new SessionBindingContext
        {
            Channel = channel,
            ChatId = chatId,
            UserId = userId,
            PeerKind = peerKind,
            ExplicitAgentId = explicitAgentId
        };
    }

    [Fact]
    public void Resolve_NoRules_UsesDefault()
    {
        // Arrange
        var binder = new DefaultSessionBinder([], CreateOptions());
        var context = CreateContext();

        // Act
        var binding = binder.Resolve(context);

        // Assert
        binding.AgentId.ShouldBe(DefaultAgentId);
        binding.Scope.ShouldBe(SessionScope.PerPeer);
        binding.SessionKey.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void Resolve_ExactPeerMatch_HighestPriority()
    {
        // Arrange
        var rules = new List<SessionBindingRule>
        {
            new()
            {
                Channel = "telegram",
                PeerId = "user-1",
                AgentId = RuleAgentId,
                Scope = SessionScope.PerPeer,
                Priority = 10,
                IsEnabled = true
            },
            new()
            {
                Channel = "telegram",
                PeerId = "user-1",
                AgentId = HighPriorityAgentId,
                Scope = SessionScope.PerChannelPeer,
                Priority = 20,
                IsEnabled = true
            }
        };
        var binder = new DefaultSessionBinder(rules, CreateOptions());
        var context = CreateContext();

        // Act
        var binding = binder.Resolve(context);

        // Assert
        binding.AgentId.ShouldBe(HighPriorityAgentId);
        binding.Scope.ShouldBe(SessionScope.PerChannelPeer);
    }

    [Fact]
    public void Resolve_ChannelMatch_FallsThrough()
    {
        // Arrange — rule for "slack" only, request comes from "telegram"
        var rules = new List<SessionBindingRule>
        {
            new()
            {
                Channel = "slack",
                AgentId = RuleAgentId,
                Scope = SessionScope.Global,
                Priority = 10,
                IsEnabled = true
            }
        };
        var binder = new DefaultSessionBinder(rules, CreateOptions());
        var context = CreateContext(channel: "telegram");

        // Act
        var binding = binder.Resolve(context);

        // Assert — falls through to default
        binding.AgentId.ShouldBe(DefaultAgentId);
        binding.Scope.ShouldBe(SessionScope.PerPeer);
    }

    [Fact]
    public void Resolve_DisabledRule_Skipped()
    {
        // Arrange
        var rules = new List<SessionBindingRule>
        {
            new()
            {
                Channel = "telegram",
                AgentId = RuleAgentId,
                Scope = SessionScope.Global,
                Priority = 100,
                IsEnabled = false
            }
        };
        var binder = new DefaultSessionBinder(rules, CreateOptions());
        var context = CreateContext();

        // Act
        var binding = binder.Resolve(context);

        // Assert — disabled rule skipped, uses default
        binding.AgentId.ShouldBe(DefaultAgentId);
    }

    [Fact]
    public void Resolve_WildcardChannel_MatchesAll()
    {
        // Arrange — Channel=null matches any channel
        var rules = new List<SessionBindingRule>
        {
            new()
            {
                Channel = null,
                AgentId = RuleAgentId,
                Scope = SessionScope.PerPeer,
                Priority = 5,
                IsEnabled = true
            }
        };
        var binder = new DefaultSessionBinder(rules, CreateOptions());

        // Act — request from "discord"
        var binding = binder.Resolve(CreateContext(channel: "discord"));

        // Assert — wildcard rule matches
        binding.AgentId.ShouldBe(RuleAgentId);
    }

    [Fact]
    public void Resolve_ExplicitAgentId_OverridesBinding()
    {
        // Arrange — rules exist but explicit ID overrides
        var explicitId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var rules = new List<SessionBindingRule>
        {
            new()
            {
                Channel = "telegram",
                AgentId = RuleAgentId,
                Scope = SessionScope.Global,
                Priority = 100,
                IsEnabled = true
            }
        };
        var binder = new DefaultSessionBinder(rules, CreateOptions());
        var context = CreateContext(explicitAgentId: explicitId.ToString());

        // Act
        var binding = binder.Resolve(context);

        // Assert
        binding.AgentId.ShouldBe(explicitId);
    }

    [Theory]
    [InlineData(SessionScope.Global, "agent:{0}:global")]
    [InlineData(SessionScope.PerPeer, "agent:{0}:peer:user-1")]
    [InlineData(SessionScope.PerChannelPeer, "agent:{0}:telegram:peer:user-1")]
    [InlineData(SessionScope.PerThread, "agent:{0}:telegram:chat-1:thread")]
    public void Resolve_SessionKey_MatchesScope(SessionScope scope, string expectedPattern)
    {
        // Arrange
        var rules = new List<SessionBindingRule>
        {
            new()
            {
                Channel = null,
                AgentId = RuleAgentId,
                Scope = scope,
                Priority = 10,
                IsEnabled = true
            }
        };
        var binder = new DefaultSessionBinder(rules, CreateOptions());
        var context = CreateContext();

        // Act
        var binding = binder.Resolve(context);

        // Assert
        var expected = string.Format(expectedPattern, RuleAgentId.ToString("N"));
        binding.SessionKey.ShouldBe(expected);
    }

    [Fact]
    public void Resolve_PriorityOrdering()
    {
        // Arrange — 3 rules with different priorities
        var lowAgent = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var midAgent = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var highAgent = Guid.Parse("33333333-3333-3333-3333-333333333333");

        var rules = new List<SessionBindingRule>
        {
            new() { AgentId = lowAgent, Scope = SessionScope.PerPeer, Priority = 1, IsEnabled = true },
            new() { AgentId = highAgent, Scope = SessionScope.Global, Priority = 30, IsEnabled = true },
            new() { AgentId = midAgent, Scope = SessionScope.PerPeer, Priority = 15, IsEnabled = true }
        };
        var binder = new DefaultSessionBinder(rules, CreateOptions());
        var context = CreateContext();

        // Act
        var binding = binder.Resolve(context);

        // Assert — highest priority (30) wins
        binding.AgentId.ShouldBe(highAgent);
        binding.Scope.ShouldBe(SessionScope.Global);
    }
}
