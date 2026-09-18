using Tnzi.AI.Infrastructure.ContextProviders.Contributors;
using Tnzi.AI.Tests.Skills;
using Tnzi.AI.Tools.Models;

namespace Tnzi.AI.Tests.Middleware;

/// <summary>
/// End-to-end gate for skill constraints on the REAL activation chain:
/// ContextInjectionMiddleware(400) → real CompositeContextProviderFactory → real SkillContributor →
/// real SkillContextProvider → the model calls <c>skill_activate</c> inside the run → the scoped
/// <see cref="SkillActivationTracker"/> → SkillConstraintToolMiddleware (same run) and
/// SkillConstraintMiddleware(450) (next run, after the set is restored from the thread).
/// <para>
/// Before this chain existed, Properties["ActiveSkills"] was the only input of the constraint
/// middleware and its only writer read a per-run provider that was still empty when 450 ran, so a
/// skill's tool-blacklist / model override was announced to the model but never applied.
/// </para>
/// </summary>
[Collection("ContextInjectionCache")]
public class SkillConstraintPipelineTests
{
    private static readonly SkillDefinition ReadOnlySkill = new()
    {
        Slug = "ro",
        Name = "Read Only",
        Content = "Never modify anything.",
        Enabled = true,
        DeniedTools = ["bash"],
        RequiredModel = "claude-opus"
    };

    private static readonly Guid ThreadId = Guid.NewGuid();

    public SkillConstraintPipelineTests()
    {
        ContextInjectionMiddleware.ClearAllCachesForTesting();
    }

    [Fact]
    public async Task Activate_ViaTool_ThenNextRun_DeniedToolIsWithheldAndModelOverridden()
    {
        var threadStore = new SkillContextProviderTests.InMemoryThreadMetadata();

        // ---- Run 1: a fresh scope; the "model" calls skill_activate('ro') inside the run.
        var run1 = new Scope(threadStore);
        var ctx1 = run1.NewContext();
        await run1.Pipeline(ctx1, async (ctx, ct) =>
        {
            // The agent loop: skill_activate is one of the tools ContextInjection injected.
            var activate = (AIFunction)ctx.AdditionalTools.First(t => t.Name == "skill_activate");
            var reply = (await activate.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["slug"] = "ro" }), ct))?.ToString();
            reply.ShouldNotBeNull().ShouldContain("Skill Activated");
            return new AgentRunResult { Response = "ok" };
        });

        // Run 1 started unconstrained (nothing was active when 450 ran) - that is expected.
        ctx1.ExcludedToolNames.ShouldBeEmpty();

        // ---- Run 2: a brand-new scope (new tracker, new provider) on the same thread.
        var run2 = new Scope(threadStore);
        var ctx2 = run2.NewContext();
        await run2.Pipeline(ctx2, (ctx, ct) => Task.FromResult(new AgentRunResult { Response = "ok" }));

        ctx2.ExcludedToolNames.ShouldContain("bash", "the denied tool must be withheld from the model on the next turn");
        ctx2.ExcludedToolNames.ShouldNotContain("read_file");
        ctx2.EffectiveModel.ShouldBe("claude-opus", "the model override applies from the next turn");
    }

    [Fact]
    public async Task Activate_ViaTool_SameRun_DeniedToolCallIsShortCircuited()
    {
        var threadStore = new SkillContextProviderTests.InMemoryThreadMetadata();
        var run = new Scope(threadStore);
        var ctx = run.NewContext();

        await run.Pipeline(ctx, async (c, ct) =>
        {
            // Before activation: bash goes through.
            var before = await run.CallTool("bash");
            before.result.ShouldBe("executed");
            before.context.FailureReason.ShouldBeNull();

            var activate = (AIFunction)c.AdditionalTools.First(t => t.Name == "skill_activate");
            await activate.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["slug"] = "ro" }), ct);

            // After activation, in the SAME run: bash is blocked, read_file still works.
            var blocked = await run.CallTool("bash");
            blocked.executed.ShouldBeFalse("the denied tool must not run once the skill is active");
            blocked.context.FailureReason.ShouldNotBeNull();
            blocked.result!.ToString()!.ShouldContain("blocked");

            var allowed = await run.CallTool("read_file");
            allowed.result.ShouldBe("executed");

            // Skill management tools are never blocked by a skill.
            var deactivate = await run.CallTool("skill_deactivate");
            deactivate.executed.ShouldBeTrue();

            return new AgentRunResult { Response = "ok" };
        });
    }

    [Fact]
    public async Task Deactivate_ViaTool_ClearsPersistedState_NextRunIsUnconstrained()
    {
        var threadStore = new SkillContextProviderTests.InMemoryThreadMetadata();

        var run1 = new Scope(threadStore);
        await run1.Pipeline(run1.NewContext(), async (c, ct) =>
        {
            var activate = (AIFunction)c.AdditionalTools.First(t => t.Name == "skill_activate");
            await activate.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["slug"] = "ro" }), ct);
            var deactivate = (AIFunction)c.AdditionalTools.First(t => t.Name == "skill_deactivate");
            await deactivate.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["slug"] = "ro" }), ct);
            return new AgentRunResult { Response = "ok" };
        });

        var run2 = new Scope(threadStore);
        var ctx2 = run2.NewContext();
        await run2.Pipeline(ctx2, (c, ct) => Task.FromResult(new AgentRunResult { Response = "ok" }));

        ctx2.ExcludedToolNames.ShouldBeEmpty();
        ctx2.EffectiveModel.ShouldBeNull("no active skill means no override is written");
    }

    /// <summary>
    /// Restoring the persisted activation set used to live only inside the Skills context provider,
    /// which the composite skips once earlier providers (Memory / RAG) exhaust
    /// <see cref="ContextProvidersOptions.MaxTokenBudget"/>. A turn that never reached the provider
    /// ran with an empty tracker: no tool withheld, no tool call blocked, only a Debug log.
    /// Restore must happen for the turn regardless of whether the content step ran.
    /// </summary>
    [Fact]
    public async Task PersistedSkill_BudgetExhaustedBeforeSkillsProvider_ConstraintsStillApply()
    {
        var threadStore = await StoreWithActivatedSkillAsync();

        // A "memory" provider ahead of Skills whose injection exactly fills the budget
        // (40 ASCII chars ≈ 10 tokens), so the composite breaks before reaching Skills.
        var run = new Scope(threadStore, maxTokenBudget: 10, extraContributor: new BulkContributor(ContextProviderOrders.Memory, new string('x', 40)));
        var ctx = run.NewContext();

        await run.Pipeline(ctx, async (c, ct) =>
        {
            var blocked = await run.CallTool("bash");
            blocked.executed.ShouldBeFalse("the persisted skill's denied tool must be blocked even when the skills provider was budget-skipped");
            blocked.context.FailureReason.ShouldNotBeNull();
            return new AgentRunResult { Response = "ok" };
        });

        ctx.AdditionalTools.Select(t => t.Name).ShouldNotContain("skill_activate", "precondition: the skills provider really was skipped by the budget");
        ctx.ExcludedToolNames.ShouldContain("bash");
        ctx.EffectiveModel.ShouldBe("claude-opus");
    }

    /// <summary>
    /// Same hole, other trigger: an agent whose configuration disables Memory/RAG/Skills injection
    /// (`disableContextProviders`) never builds the Skills provider at all. Constraints activated on
    /// the thread must still hold.
    /// </summary>
    [Fact]
    public async Task PersistedSkill_ContextProvidersDisabledForAgent_ConstraintsStillApply()
    {
        var threadStore = await StoreWithActivatedSkillAsync();
        var run = new Scope(threadStore);
        var ctx = run.NewContext(agentConfiguration: """{"disableContextProviders":true}""");

        await run.Pipeline(ctx, async (c, ct) =>
        {
            var blocked = await run.CallTool("bash");
            blocked.executed.ShouldBeFalse();
            return new AgentRunResult { Response = "ok" };
        });

        ctx.AdditionalTools.ShouldBeEmpty("precondition: no provider ran for this agent");
        ctx.ExcludedToolNames.ShouldContain("bash");
    }

    /// <summary>
    /// If the activation set cannot be read, the turn must fail rather than run unconstrained:
    /// the failed read is not remembered as "restored", so the 450 middleware retries it and, when
    /// the store is still down, surfaces the failure instead of letting bash through.
    /// </summary>
    [Fact]
    public async Task PersistedSkill_MetadataReadFails_TurnFailsInsteadOfRunningUnconstrained()
    {
        var broken = new Mock<IAgentThreadInternalService>();
        broken.Setup(s => s.GetMetadataValueAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));
        var run = new Scope(broken.Object);
        var ctx = run.NewContext();
        var coreRan = false;

        await Should.ThrowAsync<InvalidOperationException>(() => run.Pipeline(ctx, (c, ct) =>
        {
            coreRan = true;
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        }));

        coreRan.ShouldBeFalse("the agent loop must not start when the activation set is unknown");
        broken.Verify(s => s.GetMetadataValueAsync(ThreadId, SkillActivationTracker.ThreadMetadataKey, It.IsAny<CancellationToken>()),
            Times.AtLeast(2), "the failed read in the provider must be retried by the middleware, not cached as restored");
    }

    private static async Task<SkillContextProviderTests.InMemoryThreadMetadata> StoreWithActivatedSkillAsync()
    {
        var threadStore = new SkillContextProviderTests.InMemoryThreadMetadata();
        await threadStore.SetMetadataValueAsync(ThreadId, SkillActivationTracker.ThreadMetadataKey, """["ro"]""");
        return threadStore;
    }

    /// <summary>A contributor whose provider injects one large system message (stands in for Memory / RAG).</summary>
    private sealed class BulkContributor(int order, string text) : IContextProviderContributor
    {
        public int Order => order;

        public IContextProvider? TryCreate(ContextProviderCreationContext context) => new BulkProvider(text);

        private sealed class BulkProvider(string text) : IContextProvider
        {
            public Task<ContextInjection> GetContextAsync(List<ChatMessage> messages, CancellationToken ct = default)
                => Task.FromResult(new ContextInjection { Messages = [new ChatMessage(ChatRole.System, text)] });

            public Task OnCompletedAsync(List<ChatMessage> messages, CancellationToken ct = default) => Task.CompletedTask;
        }
    }

    /// <summary>
    /// One request scope: the real components wired the way DI wires them, sharing one scoped tracker.
    /// </summary>
    private sealed class Scope
    {
        private readonly SkillActivationTracker _tracker;
        private readonly ContextInjectionMiddleware _contextInjection;
        private readonly SkillConstraintMiddleware _constraint;
        private readonly SkillConstraintToolMiddleware _toolConstraint;

        public Scope(IAgentThreadInternalService threadStore, int? maxTokenBudget = null, IContextProviderContributor? extraContributor = null)
        {
            var registry = new Mock<ISkillRegistry>();
            registry.Setup(r => r.GetAvailableSkillsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<SkillDefinition>)[ReadOnlySkill]);
            registry.Setup(r => r.GetBySlugAsync("ro", It.IsAny<CancellationToken>())).ReturnsAsync(ReadOnlySkill);

            var templateEngine = new Mock<ISkillTemplateEngine>();
            templateEngine.Setup(e => e.Render(It.IsAny<SkillDefinition>(), It.IsAny<Dictionary<string, string>?>()))
                .Returns(new SkillRenderResult { Success = true, RenderedContent = "Never modify anything." });

            _tracker = new SkillActivationTracker(threadService: threadStore, registry: registry.Object);

            var aiOptions = new StaticOptionsMonitor<AIOptions>(new AIOptions
            {
                ContextProviders = new ContextProvidersOptions
                {
                    Enabled = true,
                    MaxTokenBudget = maxTokenBudget ?? new ContextProvidersOptions().MaxTokenBudget,
                    Skills = new SkillsOptions { Enabled = true, InjectionMode = SkillInjectionMode.Both }
                }
            });

            var contributor = new SkillContributor(
                aiOptions, NullLoggerFactory.Instance, registry.Object, templateEngine.Object,
                new SkillConstraintEnforcer(), new SkillLoadTracker(), _tracker);

            var factory = new CompositeContextProviderFactory(
                contributors: extraContributor is null ? [contributor] : [extraContributor, contributor],
                options: aiOptions,
                tokenEstimator: new HeuristicTokenEstimator(),
                loggerFactory: NullLoggerFactory.Instance,
                logger: NullLogger<CompositeContextProviderFactory>.Instance);

            _contextInjection = new ContextInjectionMiddleware(factory, NullLogger<ContextInjectionMiddleware>.Instance);

            var toolRegistry = new Mock<IToolRegistry>();
            toolRegistry.Setup(r => r.GetAllTools()).Returns(
            [
                new ToolDefinition { Name = "bash", GroupName = "shell" },
                new ToolDefinition { Name = "read_file", GroupName = "fs" }
            ]);

            _constraint = new SkillConstraintMiddleware(new SkillConstraintEnforcer(), toolRegistry.Object, _tracker, NullLogger<SkillConstraintMiddleware>.Instance);
            _toolConstraint = new SkillConstraintToolMiddleware(new SkillConstraintEnforcer(), toolRegistry.Object, _tracker, NullLogger<SkillConstraintToolMiddleware>.Instance);
        }

        public AiMiddlewareContext NewContext(string? agentConfiguration = null)
        {
            var agent = new Mock<IAgentExecutor>();
            agent.Setup(a => a.Name).Returns("agent");
            agent.Setup(a => a.Tools).Returns(new[] { "bash", "read_file" }.Select(n =>
            {
                var t = new Mock<AITool>();
                t.Setup(x => x.Name).Returns(n);
                return t.Object;
            }).ToList());

            return new AiMiddlewareContext
            {
                Request = new AgentRunRequest { UserMessage = "hi", ThreadId = ThreadId, Model = "gpt-4o", Provider = "openai" },
                Agent = AgentResolution.Success(agent.Object, provider: "openai", model: "gpt-4o", agentId: null, agentConfiguration: agentConfiguration),
                Messages = [new ChatMessage(ChatRole.User, "hi")],
                ServiceProvider = new ServiceCollection().BuildServiceProvider()
            };
        }

        /// <summary>ContextInjection(400) → SkillConstraint(450) → core.</summary>
        public Task<AgentRunResult> Pipeline(AiMiddlewareContext context, AiMiddlewareDelegate core)
            => _contextInjection.InvokeAsync(context, (c1, ct1) => _constraint.InvokeAsync(c1, core, ct1));

        /// <summary>Runs one tool call through the tool-execution middleware the way AgentExecutor does.</summary>
        public async Task<(object? result, bool executed, ToolExecutionContext context)> CallTool(string name)
        {
            var executed = false;
            var context = new ToolExecutionContext { ToolName = name, CallId = Guid.NewGuid().ToString("N") };
            var result = await _toolConstraint.InvokeAsync(context, () =>
            {
                executed = true;
                return Task.FromResult<object?>("executed");
            });
            return (result, executed, context);
        }
    }
}
