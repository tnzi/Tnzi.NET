using Tnzi.AI.Tests.Integration;

namespace Tnzi.AI.Tests.Services;

/// <summary>
/// 用量与模型策略：用户的单独设置 → 租户默认值 → 配置文件，逐项跟随；模型按用户的允许列表与 Agent 的 provider 过滤。
/// </summary>
public class UsagePolicyServiceTests : IntegratedTestBase<AiIntegrationDbContext>
{
    private const string Opus = "claude-opus-5-5";
    private const string Sonnet = "claude-sonnet-5-5";
    private const string Haiku = "claude-haiku-5-5";
    private const string Gpt = "gpt-5";

    private static readonly Guid CliAgent = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid BuiltInAgent = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    private readonly UsageLimitOptions _limits = new() { SessionLimitUsd = 5m, WeeklyLimitUsd = 40m };
    private readonly AiChatOptions _chat = new()
    {
        Models =
        [
            new ChatModelOption { Id = Opus, DisplayName = "Opus 5.5", Provider = "claude" },
            new ChatModelOption { Id = Sonnet, DisplayName = "Sonnet 5.5", Provider = "claude" },
            new ChatModelOption { Id = Haiku, DisplayName = "Haiku 5.5", Provider = "claude" },
            new ChatModelOption { Id = Gpt, Provider = "OpenAI" }
        ]
    };

    protected override void ConfigureServices(IServiceCollection services)
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));

        services.AddScoped<IRepository<AiUsagePolicy, Guid>, EFCoreRepository<AiIntegrationDbContext, AiUsagePolicy, Guid>>();
        services.AddScoped<IRepository<Agent, Guid>, EFCoreRepository<AiIntegrationDbContext, Agent, Guid>>();
        services.AddSingleton<IOptionsMonitor<UsageLimitOptions>>(new StaticOptionsMonitor<UsageLimitOptions>(_limits));
        services.AddSingleton<IOptionsMonitor<AiChatOptions>>(new StaticOptionsMonitor<AiChatOptions>(_chat));
        services.AddSingleton<IOptionsMonitor<AIOptions>>(new StaticOptionsMonitor<AIOptions>(new AIOptions { DefaultProvider = "OpenAI" }));

        var bindings = new Mock<ICliAgentBindingService>();
        bindings.Setup(b => b.GetByAgentIdAsync(CliAgent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CliAgentBindingDto { AgentId = CliAgent, ProviderKey = "claude" });
        services.AddSingleton(bindings.Object);

        services.AddScoped<IUsagePolicyService, UsagePolicyService>();
    }

    private IUsagePolicyService Service() => ServiceProvider.GetRequiredService<IUsagePolicyService>();

    private static SaveUsagePolicyDto Policy(
        UsageLimitSettingDto? session = null, UsageLimitSettingDto? weekly = null,
        List<string>? models = null, string? defaultModel = null)
        => new()
        {
            SessionLimit = session ?? new UsageLimitSettingDto(),
            WeeklyLimit = weekly ?? new UsageLimitSettingDto(),
            AllowedModels = models,
            DefaultModel = defaultModel
        };

    private static UsageLimitSettingDto Limited(decimal amount) => new() { Mode = UsageLimitMode.Limited, AmountUsd = amount };

    private static readonly UsageLimitSettingDto Unlimited = new() { Mode = UsageLimitMode.Unlimited };

    // ── 三层取值 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task NothingSaved_EverythingFollowsTheConfiguration()
    {
        var effective = await Service().ResolveAsync(Guid.NewGuid());

        effective.SessionLimitUsd.ShouldBe(5m);
        effective.WeeklyLimitUsd.ShouldBe(40m);
        effective.AllowedModels.Select(m => m.Id).ShouldBe([Opus, Sonnet, Haiku, Gpt]);
        effective.DefaultModel.ShouldBe(Opus, "no default configured: the first allowed model");
    }

    [Fact]
    public async Task TenantDefaults_OverrideTheConfiguration_ItemByItem()
    {
        (await Service().SaveDefaultsAsync(Policy(session: Limited(3m), models: [Sonnet, Haiku], defaultModel: Haiku))).Succeeded.ShouldBeTrue();

        var effective = await Service().ResolveAsync(Guid.NewGuid());

        effective.SessionLimitUsd.ShouldBe(3m);
        effective.WeeklyLimitUsd.ShouldBe(40m, "weekly was left unset, so it follows the configuration");
        effective.AllowedModels.Select(m => m.Id).ShouldBe([Sonnet, Haiku]);
        effective.DefaultModel.ShouldBe(Haiku);
    }

    [Fact]
    public async Task UserSettings_OverrideTheDefaults_AndCanBeUnlimited()
    {
        await Service().SaveDefaultsAsync(Policy(session: Limited(3m), weekly: Limited(20m), models: [Sonnet]));
        var userId = Guid.NewGuid();

        var saved = await Service().SaveUserPolicyAsync(userId, Policy(session: Unlimited, models: [Opus, Sonnet], defaultModel: Opus));

        saved.Succeeded.ShouldBeTrue(saved.Message);
        saved.Data!.HasCustomPolicy.ShouldBeTrue();
        saved.Data.Effective.SessionLimitUsd.ShouldBeNull();
        saved.Data.Effective.WeeklyLimitUsd.ShouldBe(20m, "weekly follows the defaults");
        saved.Data.Effective.DefaultModel.ShouldBe(Opus);
        saved.Data.Defaults.SessionLimitUsd.ShouldBe(3m);

        var other = await Service().ResolveAsync(Guid.NewGuid());
        other.SessionLimitUsd.ShouldBe(3m, "one user's settings are theirs alone");
    }

    [Fact]
    public async Task ClearingAUsersSettings_ReturnsThemToTheDefaults()
    {
        var userId = Guid.NewGuid();
        await Service().SaveUserPolicyAsync(userId, Policy(session: Limited(1m)));

        await Service().ClearUserPolicyAsync(userId);

        var user = (await Service().GetUserPolicyAsync(userId)).Data!;
        user.HasCustomPolicy.ShouldBeFalse();
        user.Effective.SessionLimitUsd.ShouldBe(5m);
    }

    [Fact]
    public async Task SavingTheDefaultsTwice_UpdatesTheOneDefaultRow()
    {
        await Service().SaveDefaultsAsync(Policy(session: Limited(3m)));
        await Service().SaveDefaultsAsync(Policy(session: Limited(4m)));

        DbContext.ChangeTracker.Clear();
        (await DbContext.Set<AiUsagePolicy>().CountAsync(p => p.UserId == null)).ShouldBe(1);
        (await Service().ResolveAsync(null)).SessionLimitUsd.ShouldBe(4m);
    }

    // ── 校验 ──────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(InvalidPolicies))]
    public async Task InvalidPolicies_AreRefusedWith400(SaveUsagePolicyDto input, string reason)
    {
        var result = await Service().SaveUserPolicyAsync(Guid.NewGuid(), input);

        result.Succeeded.ShouldBeFalse(reason);
        result.Code.ShouldBe(400);
        result.ErrorCode.ShouldBe(ErrorCodes.UsagePolicyInvalid);
    }

    public static TheoryData<SaveUsagePolicyDto, string> InvalidPolicies() => new()
    {
        { Policy(session: new UsageLimitSettingDto { Mode = UsageLimitMode.Limited }), "a fixed limit without an amount" },
        { Policy(models: ["claude-mystery"]), "a model that is not in the model list" },
        { Policy(models: []), "an empty allowed list (unset means follow the default)" },
        { Policy(models: [Sonnet], defaultModel: Opus), "a default model outside the allowed list" }
    };

    // ── 模型选择 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChatModels_AreTheUsersAllowedModels_ThatBelongToTheAgentsProvider()
    {
        var userId = Guid.NewGuid();
        await Service().SaveUserPolicyAsync(userId, Policy(models: [Opus, Sonnet, Gpt], defaultModel: Sonnet));

        var onCli = (await Service().GetChatModelsAsync(userId, CliAgent)).Data!;
        onCli.Models.Select(m => m.Id).ShouldBe([Opus, Sonnet], "the CLI runtime runs claude models only");
        onCli.Models[0].DisplayName.ShouldBe("Opus 5.5");
        onCli.DefaultModel.ShouldBe(Sonnet);

        DbContext.Set<Agent>().Add(new Agent { Id = BuiltInAgent, Name = "assistant", Provider = "OpenAI" });
        await DbContext.SaveChangesAsync();
        var onBuiltIn = (await Service().GetChatModelsAsync(userId, BuiltInAgent)).Data!;
        onBuiltIn.Models.Select(m => m.Id).ShouldBe([Gpt]);
        onBuiltIn.DefaultModel.ShouldBe(Gpt, "the preferred default does not run on this agent");
    }

    [Fact]
    public async Task ResolveChatModel_AnAllowedModel_IsUsed()
    {
        var resolved = await Service().ResolveChatModelAsync(Guid.NewGuid(), CliAgent, null, "CLAUDE-OPUS-5-5");

        resolved.Succeeded.ShouldBeTrue();
        resolved.Data.ShouldBe(Opus);
    }

    [Fact]
    public async Task ResolveChatModel_NoModelRequested_IsTheUsersDefault()
    {
        var userId = Guid.NewGuid();
        await Service().SaveUserPolicyAsync(userId, Policy(models: [Sonnet, Haiku], defaultModel: Haiku));

        (await Service().ResolveChatModelAsync(userId, CliAgent, null, null)).Data.ShouldBe(Haiku);
    }

    [Theory]
    [InlineData(Opus, "not in this user's allowed list")]
    [InlineData(Gpt, "allowed, but not a model of this agent's provider")]
    [InlineData("claude-mystery", "not in the model list at all")]
    public async Task ResolveChatModel_AModelTheUserMayNotUseHere_IsRefusedWith403(string model, string reason)
    {
        var userId = Guid.NewGuid();
        await Service().SaveUserPolicyAsync(userId, Policy(models: [Sonnet, Gpt]));

        var resolved = await Service().ResolveChatModelAsync(userId, CliAgent, null, model);

        resolved.Succeeded.ShouldBeFalse(reason);
        resolved.Code.ShouldBe(403);
        resolved.ErrorCode.ShouldBe(ErrorCodes.ModelNotAllowed);
    }

    [Fact]
    public async Task WithoutAModelList_ThereIsNoSelection_AndTheRequestedModelIsLeftAlone()
    {
        _chat.Models = [];

        (await Service().GetChatModelsAsync(Guid.NewGuid(), CliAgent)).Data!.Models.ShouldBeEmpty();
        (await Service().ResolveChatModelAsync(Guid.NewGuid(), CliAgent, null, "anything")).Data.ShouldBe("anything");
    }
}
