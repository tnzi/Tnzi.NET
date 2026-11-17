using Tnzi.AI.Tests.Integration;

namespace Tnzi.AI.Tests.Services;

/// <summary>
/// 每个模型可选的推理强度：策略（用户 → 默认值）→ 模型清单 → 运行时握手逐个模型往下找；聊天请求的强度按这一轮的模型校验。
/// </summary>
public class ChatModelEffortTests : IntegratedTestBase<AiIntegrationDbContext>
{
    private const string Opus = "claude-opus-5-5";
    private const string Sonnet = "claude-sonnet-5-5";
    private const string Haiku = "claude-haiku-4-5";
    private const string Codex = "gpt-5-codex";

    private static readonly Guid ClaudeAgent = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid CodexAgent = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid CodexRuntime = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
    private static readonly Guid KimiRuntime = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
    private static readonly Guid OwnThread = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");

    /// <summary>使用方说的这段对话的落点（每个测试自己设）；收到的请求记在 <see cref="_targetRequests"/>。</summary>
    private ChatModelTarget? _target;
    private Func<ChatModelTargetRequest, ChatModelTarget?>? _targetFor;
    private readonly List<ChatModelTargetRequest> _targetRequests = [];

    private sealed class FakeTargetResolver(ChatModelEffortTests owner) : IChatModelTargetResolver
    {
        public Task<ChatModelTarget?> ResolveAsync(ChatModelTargetRequest request, CancellationToken cancellationToken = default)
        {
            owner._targetRequests.Add(request);
            return Task.FromResult(owner._targetFor is { } answer ? answer(request) : owner._target);
        }
    }

    private readonly AiChatOptions _chat = new()
    {
        Models =
        [
            new ChatModelOption
            {
                Id = Opus, DisplayName = "Opus 5.5", Provider = "claude",
                Efforts = ["low", "medium", "high", "xhigh", "max"], DefaultEffort = "medium"
            },
            new ChatModelOption { Id = Sonnet, DisplayName = "Sonnet 5.5", Provider = "claude", Efforts = ["low", "medium", "high"], DefaultEffort = "high" },
            new ChatModelOption { Id = Haiku, DisplayName = "Haiku 4.5", Provider = "claude" },
            new ChatModelOption { Id = Codex, Provider = "codex" },
            new ChatModelOption { Id = "gpt-5", Provider = "codex", Efforts = ["low", "medium", "high"], DefaultEffort = "medium" }
        ]
    };

    protected override void ConfigureServices(IServiceCollection services)
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));

        services.AddScoped<IRepository<AiUsagePolicy, Guid>, EFCoreRepository<AiIntegrationDbContext, AiUsagePolicy, Guid>>();
        services.AddScoped<IRepository<Agent, Guid>, EFCoreRepository<AiIntegrationDbContext, Agent, Guid>>();
        services.AddSingleton<IOptionsMonitor<UsageLimitOptions>>(new StaticOptionsMonitor<UsageLimitOptions>(new UsageLimitOptions()));
        services.AddSingleton<IOptionsMonitor<AiChatOptions>>(new StaticOptionsMonitor<AiChatOptions>(_chat));
        services.AddSingleton<IOptionsMonitor<AIOptions>>(new StaticOptionsMonitor<AIOptions>(new AIOptions { DefaultProvider = "OpenAI" }));

        var bindings = new Mock<ICliAgentBindingService>();
        bindings.Setup(b => b.GetByAgentIdAsync(ClaudeAgent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CliAgentBindingDto { AgentId = ClaudeAgent, ProviderKey = "claude", CliRuntimeId = Guid.NewGuid() });
        bindings.Setup(b => b.GetByAgentIdAsync(CodexAgent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CliAgentBindingDto { AgentId = CodexAgent, ProviderKey = "codex", CliRuntimeId = CodexRuntime, ThinkingLevel = "medium" });
        services.AddSingleton(bindings.Object);

        // stream-json 运行时握手不报档位（Capabilities 为 null）；ACP 运行时报 thought_level 的可选值
        var runtimes = new Mock<ICliRuntimeService>();
        runtimes.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => Result<CliRuntimeDto>.Success(new CliRuntimeDto
            {
                Id = id,
                Capabilities = id == CodexRuntime ? new CliAgentCapabilities { ReasoningEfforts = ["low", "medium", "high"] }
                    : id == KimiRuntime ? new CliAgentCapabilities { ReasoningEfforts = ["off", "on"] }
                    : null
            }));
        services.AddSingleton(runtimes.Object);

        var threads = new Mock<IAgentThreadService>();
        threads.Setup(t => t.IsOwnerAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
            .ReturnsAsync((Guid thread, Guid user) => thread == OwnThread && user == UserId);
        services.AddSingleton(threads.Object);
        services.AddSingleton<IChatModelTargetResolver>(new FakeTargetResolver(this));

        services.AddScoped<IUsagePolicyService, UsagePolicyService>();
    }

    private IUsagePolicyService Service() => ServiceProvider.GetRequiredService<IUsagePolicyService>();

    private static SaveUsagePolicyDto Policy(Dictionary<string, ChatModelEffortsDto>? efforts)
        => new() { SessionLimit = new UsageLimitSettingDto(), WeeklyLimit = new UsageLimitSettingDto(), ModelEfforts = efforts };

    private static ChatModelEffortsDto Efforts(string? defaultEffort, params string[] efforts) => new() { Efforts = [.. efforts], DefaultEffort = defaultEffort };

    private async Task<ChatModelDto> ModelAsync(string id, Guid? userId = null, Guid? agentId = null)
        => (await Service().GetChatModelsAsync(userId ?? Guid.NewGuid(), agentId ?? ClaudeAgent)).Data!.Models.Single(m => m.Id == id);

    // ── 可选档位从哪里来 ──────────────────────────────────────────────────────

    [Fact]
    public async Task TheModelList_DeclaresEachModelsEfforts_AndItsDefault()
    {
        var opus = await ModelAsync(Opus);
        opus.Efforts.ShouldBe(["low", "medium", "high", "xhigh", "max"]);
        opus.DefaultEffort.ShouldBe("medium");

        var haiku = await ModelAsync(Haiku);
        haiku.Efforts.ShouldBeEmpty("Haiku declares none and the stream-json runtime reports none: no effort choice");
        haiku.DefaultEffort.ShouldBeNull();
    }

    [Fact]
    public async Task TheDefaults_OverrideTheModelList_ModelByModel()
    {
        (await Service().SaveDefaultsAsync(Policy(new() { [Opus] = Efforts("low", "low", "medium") }))).Succeeded.ShouldBeTrue();

        var opus = await ModelAsync(Opus);
        opus.Efforts.ShouldBe(["low", "medium"]);
        opus.DefaultEffort.ShouldBe("low");
        (await ModelAsync(Sonnet)).Efforts.ShouldBe(["low", "medium", "high"], "Sonnet is not listed in the defaults: it follows the model list");
    }

    [Fact]
    public async Task AUsersSettings_OverrideTheDefaults_ForTheModelsTheyList()
    {
        await Service().SaveDefaultsAsync(Policy(new() { [Opus] = Efforts("low", "low", "medium"), [Sonnet] = Efforts(null, "low") }));
        var userId = Guid.NewGuid();
        (await Service().SaveUserPolicyAsync(userId, Policy(new() { [Opus] = Efforts("max", "high", "max") }))).Succeeded.ShouldBeTrue();

        (await ModelAsync(Opus, userId)).Efforts.ShouldBe(["high", "max"]);
        (await ModelAsync(Opus, userId)).DefaultEffort.ShouldBe("max");
        (await ModelAsync(Sonnet, userId)).Efforts.ShouldBe(["low"], "Sonnet follows the defaults");
        (await ModelAsync(Opus)).Efforts.ShouldBe(["low", "medium"], "one user's settings are theirs alone");

        var saved = (await Service().GetUserPolicyAsync(userId)).Data!;
        saved.Policy.ModelEfforts!.Keys.ShouldBe([Opus]);
        saved.Effective.AllowedModels.Single(m => m.Id == Opus).Efforts.ShouldBe(["high", "max"]);
    }

    [Fact]
    public async Task WithNothingDeclared_TheRuntimesReportedEffortsAreUsed_DefaultingToTheBindings()
    {
        var codex = await ModelAsync(Codex, agentId: CodexAgent);

        codex.Efforts.ShouldBe(["low", "medium", "high"]);
        codex.DefaultEffort.ShouldBe("medium", "the binding's ThinkingLevel is among the reported efforts");
    }

    [Fact]
    public async Task AnEmptyListInThePolicy_TurnsTheChoiceOff_EvenWhenTheRuntimeReportsEfforts()
    {
        await Service().SaveDefaultsAsync(Policy(new() { [Codex] = Efforts(null) }));

        (await ModelAsync(Codex, agentId: CodexAgent)).Efforts.ShouldBeEmpty();
    }

    // ── 一轮的强度 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnEffortTheModelOffers_IsAccepted_InTheListsSpelling()
    {
        var turn = await Service().ResolveChatTurnAsync(Guid.NewGuid(), ClaudeAgent, null, Opus, "XHigh");

        turn.Succeeded.ShouldBeTrue(turn.Message);
        turn.Data.ShouldBe(new ChatTurnModel(Opus, "xhigh"));
    }

    [Fact]
    public async Task AnEffortTheModelDoesNotOffer_IsRefusedWithItsOwnCode()
    {
        var turn = await Service().ResolveChatTurnAsync(Guid.NewGuid(), ClaudeAgent, null, Sonnet, "xhigh");

        turn.Succeeded.ShouldBeFalse();
        turn.Code.ShouldBe(403);
        turn.ErrorCode.ShouldBe(ErrorCodes.EffortNotAllowed);
    }

    [Fact]
    public async Task AnyEffort_OnAModelWithoutAChoice_IsRefused()
    {
        var turn = await Service().ResolveChatTurnAsync(Guid.NewGuid(), ClaudeAgent, null, Haiku, "low");

        turn.ErrorCode.ShouldBe(ErrorCodes.EffortNotAllowed);
    }

    [Fact]
    public async Task WithoutAModel_TheEffortIsCheckedAgainstTheDefaultModel()
    {
        var accepted = await Service().ResolveChatTurnAsync(Guid.NewGuid(), ClaudeAgent, null, null, "max");
        accepted.Data.ShouldBe(new ChatTurnModel(Opus, "max"), "Opus is the default model (first allowed)");

        await Service().SaveDefaultsAsync(new SaveUsagePolicyDto
        {
            SessionLimit = new UsageLimitSettingDto(), WeeklyLimit = new UsageLimitSettingDto(), DefaultModel = Sonnet
        });
        var refused = await Service().ResolveChatTurnAsync(Guid.NewGuid(), ClaudeAgent, null, null, "max");
        refused.ErrorCode.ShouldBe(ErrorCodes.EffortNotAllowed, "Sonnet, now the default, has no max");
    }

    [Fact]
    public async Task WithoutAnEffort_TheTurnCarriesNone_AsBefore()
    {
        var turn = await Service().ResolveChatTurnAsync(Guid.NewGuid(), ClaudeAgent, null, Opus, null);

        turn.Data.ShouldBe(new ChatTurnModel(Opus, null), "the model's default effort is the client's preselection, not a server override");
        (await Service().ResolveChatModelAsync(Guid.NewGuid(), ClaudeAgent, null, Opus)).Data.ShouldBe(Opus);
    }

    [Fact]
    public async Task AModelRefusal_StillComesFirst()
    {
        var turn = await Service().ResolveChatTurnAsync(Guid.NewGuid(), ClaudeAgent, null, Codex, "low");

        turn.ErrorCode.ShouldBe(ErrorCodes.ModelNotAllowed, "Codex belongs to another provider than this agent's");
    }

    [Fact]
    public async Task WithNoModelList_AnyEffortIsRefused()
    {
        _chat.Models = [];

        var turn = await Service().ResolveChatTurnAsync(Guid.NewGuid(), ClaudeAgent, null, null, "high");
        turn.ErrorCode.ShouldBe(ErrorCodes.EffortNotAllowed);

        (await Service().ResolveChatTurnAsync(Guid.NewGuid(), ClaudeAgent, null, "whatever", null)).Data
            .ShouldBe(new ChatTurnModel("whatever", null), "without an effort nothing changes");
    }

    [Fact]
    public async Task AReplacedPolicyServiceThatKnowsOnlyModels_RefusesAnEffort()
    {
        var legacy = new Mock<IUsagePolicyService> { CallBase = true };
        legacy.Setup(s => s.ResolveChatModelAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string?>.Success(Opus));

        (await legacy.Object.ResolveChatTurnAsync(null, null, null, null, null)).Data.ShouldBe(new ChatTurnModel(Opus, null));
        (await legacy.Object.ResolveChatTurnAsync(null, null, null, null, "high")).ErrorCode.ShouldBe(ErrorCodes.EffortNotAllowed);
    }

    // ── 保存时的校验 ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("not-a-model", "low", "low")]
    [InlineData(Opus, "turbo", "low,high")]
    [InlineData(Opus, null, "low,LOW")]
    [InlineData(Opus, null, "low, ")]
    public async Task InvalidEfforts_AreRefusedOnSave(string model, string? defaultEffort, string efforts)
    {
        var saved = await Service().SaveDefaultsAsync(Policy(new() { [model] = Efforts(defaultEffort, efforts.Split(',')) }));

        saved.Succeeded.ShouldBeFalse();
        saved.ErrorCode.ShouldBe(ErrorCodes.UsagePolicyInvalid);
    }

    // ── 配置与内建路径 ────────────────────────────────────────────────────────

    [Fact]
    public void TheOptionsValidator_RefusesADefaultOutsideTheList()
    {
        var errors = new AiChatOptionsValidator().Validate(null, new AiChatOptions
        {
            Models = [new ChatModelOption { Id = Opus, Efforts = ["low", "high"], DefaultEffort = "max" }]
        });

        errors.Failed.ShouldBeTrue();
        errors.FailureMessage.ShouldContain(Opus);
    }

    [Theory]
    [InlineData("low", ReasoningEffort.Low)]
    [InlineData("MAX", ReasoningEffort.Max)]
    [InlineData("none", ReasoningEffort.None)]
    [InlineData("xhigh", null)]
    [InlineData("on", null)]
    [InlineData("2", null)]
    [InlineData(null, null)]
    public void OnBuiltInAgents_OnlyEffortsNamedLikeTheEnumApply(string? value, ReasoningEffort? expected)
        => ChatModelEfforts.ToBuiltIn(value).ShouldBe(expected);

    // ── 对话实际落在别家 CLI 上（IChatModelTargetResolver）────────────────────

    private async Task<ChatModelsDto> ModelsInThreadAsync(Guid? threadId = null)
        => (await Service().GetChatModelsAsync(new ChatModelsRequest(UserId, ClaudeAgent, null, threadId ?? OwnThread))).Data!;

    [Fact]
    public async Task WithoutATarget_TheListFollowsTheBinding()
    {
        var models = await ModelsInThreadAsync();

        models.Models.Select(m => m.Id).ShouldBe([Opus, Sonnet, Haiku]);
        _targetRequests.ShouldHaveSingleItem().ShouldBe(new ChatModelTargetRequest
        {
            UserId = UserId, AgentId = ClaudeAgent, ThreadId = OwnThread, BindingProviderKey = "claude"
        });
    }

    [Fact]
    public async Task ATargetProvider_ListsThatProvidersModels_WithItsRuntimesReportedEfforts()
    {
        _target = new ChatModelTarget { ProviderKey = "codex", RuntimeId = CodexRuntime };

        var models = await ModelsInThreadAsync();

        models.Models.Select(m => m.Id).ShouldBe([Codex, "gpt-5"]);
        models.Models[0].Efforts.ShouldBe(["low", "medium", "high"], "Codex declares none: its runtime's handshake");
        models.Models[0].DefaultEffort.ShouldBeNull("the binding's ThinkingLevel belongs to another CLI");
        models.Models[1].Efforts.ShouldBe(["low", "medium", "high"]);
        models.Models[1].DefaultEffort.ShouldBe("medium");
    }

    [Fact]
    public async Task AFixedModelOutsideTheList_IsTheOnlyChoice_WithTheRuntimesEfforts()
    {
        _target = new ChatModelTarget { ProviderKey = "kimi", Model = "kimi-k2", ModelDisplayName = "Kimi K2", RuntimeId = KimiRuntime };

        var models = await ModelsInThreadAsync();

        var only = models.Models.ShouldHaveSingleItem();
        only.Id.ShouldBe("kimi-k2");
        only.DisplayName.ShouldBe("Kimi K2");
        only.Efforts.ShouldBe(["off", "on"]);
        models.DefaultModel.ShouldBe("kimi-k2");

        var turn = await Service().ResolveChatTurnAsync(new ChatModelsRequest(UserId, ClaudeAgent, null, OwnThread), null, "ON");
        turn.Data.ShouldBe(new ChatTurnModel("kimi-k2", "on"));
        (await Service().ResolveChatTurnAsync(new ChatModelsRequest(UserId, ClaudeAgent, null, OwnThread), null, "max")).ErrorCode
            .ShouldBe(ErrorCodes.EffortNotAllowed, "Claude's max means nothing on Kimi");
    }

    [Fact]
    public async Task AThreadOfSomeoneElse_IsNotPassedToTheResolver()
    {
        await ModelsInThreadAsync(Guid.NewGuid());

        _targetRequests.ShouldHaveSingleItem().ThreadId.ShouldBeNull();
    }

    [Fact]
    public async Task TheEffortsOfAPlacedProviderAndModel_FollowPolicyThenListThenHandshake()
    {
        (await Service().GetModelEffortsAsync(UserId, "codex", "gpt-5", CodexRuntime)).ShouldBe(["low", "medium", "high"]);
        (await Service().GetModelEffortsAsync(UserId, "kimi", "kimi-k2", KimiRuntime)).ShouldBe(["off", "on"], "not in the list: the handshake");
        (await Service().GetModelEffortsAsync(UserId, "kimi", "kimi-k2", null)).ShouldBeEmpty("nothing declares any");

        await Service().SaveDefaultsAsync(Policy(new() { ["gpt-5"] = Efforts(null, "high") }));
        (await Service().GetModelEffortsAsync(UserId, "codex", "gpt-5", CodexRuntime)).ShouldBe(["high"]);
    }

    // ── 还没有线程时，用户在输入框旁的运行选择（每轮元数据）────────────────────

    [Fact]
    public async Task TheSelectionInTheMetadata_ReachesTheResolver_WithoutAThread()
    {
        _target = null;
        var metadata = new Dictionary<string, JsonElement> { ["cliProfile"] = JsonSerializer.SerializeToElement("kimi") };

        await Service().GetChatModelsAsync(new ChatModelsRequest(UserId, ClaudeAgent) { Metadata = metadata });

        var seen = _targetRequests.ShouldHaveSingleItem();
        seen.ThreadId.ShouldBeNull();
        seen.Metadata["cliProfile"].GetString().ShouldBe("kimi");
    }

    [Fact]
    public async Task ThePreviewAndTheTurnCheck_AnswerFromTheSameSelection()
    {
        var kimi = new Dictionary<string, JsonElement> { ["cliProfile"] = JsonSerializer.SerializeToElement("kimi") };
        var resolver = new Func<ChatModelTargetRequest, ChatModelTarget?>(r =>
            r.Metadata.TryGetValue("cliProfile", out var p) && p.GetString() == "kimi"
                ? new ChatModelTarget { ProviderKey = "kimi", Model = "kimi-k2", RuntimeId = KimiRuntime }
                : null);
        _targetFor = resolver;

        var preview = (await Service().GetChatModelsAsync(new ChatModelsRequest(UserId, ClaudeAgent) { Metadata = kimi })).Data!;
        preview.Models.ShouldHaveSingleItem().Efforts.ShouldBe(["off", "on"]);

        (await Service().ResolveChatTurnAsync(new ChatModelsRequest(UserId, ClaudeAgent) { Metadata = kimi }, null, "on")).Data
            .ShouldBe(new ChatTurnModel("kimi-k2", "on"));
        (await Service().ResolveChatTurnAsync(new ChatModelsRequest(UserId, ClaudeAgent), null, "on")).ErrorCode
            .ShouldBe(ErrorCodes.EffortNotAllowed, "without the selection the default (Claude) answers, and Claude has no 'on'");
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("""{"cliProfile":"kimi","n":2}""", 2)]
    public void MetadataFromAQuery_IsReadAsAnObject(string? json, int keys)
    {
        var read = ChatTurnMetadata.ValidateJson(json, new ChatMetadataOptions());

        read.Succeeded.ShouldBeTrue(read.Message);
        read.Data!.Count.ShouldBe(keys);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("""{"1bad":"x"}""")]
    public void MetadataFromAQuery_ThatIsNotAValidObject_IsRefused(string json)
        => ChatTurnMetadata.ValidateJson(json, new ChatMetadataOptions()).ErrorCode.ShouldBe(ErrorCodes.InvalidTurnMetadata);
}
