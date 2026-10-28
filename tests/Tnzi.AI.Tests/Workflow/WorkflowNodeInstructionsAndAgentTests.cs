namespace Tnzi.AI.Tests.Workflow;

/// <summary>
/// 节点如何使用步骤的 <see cref="WorkflowStepDto.Instructions"/> 与 <see cref="WorkflowStepDto.AgentId"/>。
/// </summary>
/// <remarks>
/// <para>
/// 文档、Builder 示例与内置模板都在 Instructions 里写 <c>{{stepId}}</c> / <c>{{input}}</c>，而 agent / parallel / router /
/// synthesize（系统提示词）节点把它原样交给模型，debate 节点则整个忽略；模板反而被套在了初始输入与上游输出这些数据上。
/// </para>
/// <para>
/// Builder 的 <c>AddReviewStep(stepId, agentId)</c> 一类方法、内置模板与编辑器的 Agent 选择器都把 Agent 写在
/// <see cref="WorkflowStepDto.AgentId"/> 上，review / router / synthesize 节点却只读各自的配置键，选的 Agent 从不生效。
/// </para>
/// </remarks>
public class WorkflowNodeInstructionsAndAgentTests
{
    private static readonly Guid ChosenAgentId = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff");

    // ── Instructions 是模板，输入是数据 ──────────────────────────────────

    [Fact]
    public async Task AgentNode_InstructionsTemplates_AreResolvedBeforeTheAgentIsCreated()
    {
        var fake = new FakeAgents("summary");
        var node = new AgentNode(fake.Context);
        var state = StateWith(("extract", "the facts"), ("audience", "executives"));

        await node.ExecuteAsync(Context(new WorkflowStepDto
        {
            StepId = "summarize",
            DependsOn = ["extract"],
            Instructions = "Summarize for {{audience}}: {{extract}} (request: {{input}})"
        }, state));

        fake.Created.Single().Instructions.ShouldBe("Summarize for executives: the facts (request: write a brief)");
    }

    [Fact]
    public async Task AgentNode_InputData_IsNotTreatedAsATemplate()
    {
        // 上游生成的是一段 Jinja 模板：数据里的 {{input}} 不该被换成工作流的初始输入。
        var fake = new FakeAgents("ok");
        var node = new AgentNode(fake.Context);
        var state = StateWith(("generate", "<p>{{input}}</p>"));

        await node.ExecuteAsync(Context(new WorkflowStepDto { StepId = "check", DependsOn = ["generate"] }, state));

        fake.UserMessages.Single().ShouldBe("<p>{{input}}</p>");
    }

    [Fact]
    public async Task ParallelNode_WorkersReceiveTheResolvedInstructions()
    {
        var fake = new FakeAgents("done");
        var node = new ParallelNode(fake.Context, Mock.Of<ILogger<ParallelNode>>());
        var state = StateWith(("brief", "the brief"));

        await node.ExecuteAsync(Context(new WorkflowStepDto
        {
            StepId = "fan-out",
            Instructions = "Work from {{brief}}",
            Configuration = new Dictionary<string, string>
            {
                ["workers"] = JsonSerializer.Serialize(new[] { new { stepId = "w1" }, new { stepId = "w2" } })
            }
        }, state));

        fake.Created.Count.ShouldBe(2);
        fake.Created.ShouldAllBe(a => a.Instructions == "Work from the brief");
    }

    [Fact]
    public async Task RouterNode_PromptCarriesTheResolvedInstructions()
    {
        var fake = new FakeAgents("billing");
        var node = new RouterNode(fake.Context, Mock.Of<ILogger<RouterNode>>());
        var state = StateWith(("policy", "refunds go to billing"));

        await node.ExecuteAsync(Context(new WorkflowStepDto
        {
            StepId = "classify",
            Instructions = "Policy: {{policy}}",
            Configuration = RoutesConfig()
        }, state));

        var prompt = fake.UserMessages.Single();
        prompt.ShouldContain("Policy: refunds go to billing");
        prompt.ShouldNotContain("{{policy}}");
    }

    [Fact]
    public async Task ReviewNode_ResolvesInstructionsButNotTheContentUnderReview()
    {
        var fake = new FakeAgents("""{"verdict":"accept","feedback":"fine"}""");
        var node = new ReviewNode(fake.Context, Mock.Of<ILogger<ReviewNode>>());
        var state = StateWith(("rules", "no secrets"), ("draft", "Hello {{rules}}"));

        await node.ExecuteAsync(Context(new WorkflowStepDto
        {
            StepId = "review",
            DependsOn = ["draft"],
            Instructions = "Check against: {{rules}}",
            Configuration = new Dictionary<string, string> { ["nodeType"] = "review" }
        }, state, dependencies: ["draft"]));

        var prompt = fake.UserMessages.Single();
        prompt.ShouldContain("Check against: no secrets");
        prompt.ShouldContain("Hello {{rules}}", Case.Sensitive, "the content under review is data, not a template");
    }

    [Fact]
    public async Task SynthesizeNode_SystemPromptIsTheResolvedInstructions()
    {
        var fake = new FakeAgents("merged");
        var node = new SynthesizeNode(fake.Context);
        var state = StateWith(("goal", "a one-page report"));

        await node.ExecuteAsync(Context(new WorkflowStepDto
        {
            StepId = "synthesize",
            Instructions = "Produce {{goal}}"
        }, state));

        fake.Created.Single().Instructions.ShouldBe("Produce a one-page report");
    }

    [Fact]
    public async Task DebateNode_StepInstructionsFrameTheDiscussion()
    {
        var fake = new FakeAgents("I agree");
        var node = new DebateNode(fake.Context, Mock.Of<ILogger<DebateNode>>());
        var state = StateWith(("constraints", "budget is fixed"));

        await node.ExecuteAsync(Context(new WorkflowStepDto
        {
            StepId = "debate",
            Instructions = "Decide within: {{constraints}}",
            Configuration = new Dictionary<string, string>
            {
                ["debateAgentIds"] = JsonSerializer.Serialize(new[] { Guid.NewGuid().ToString(), Guid.NewGuid().ToString() }),
                ["maxRounds"] = "1"
            }
        }, state));

        fake.UserMessages.ShouldContain(m => m.Contains("Decide within: budget is fixed"));
    }

    // ── step.AgentId 是 review / router / synthesize 节点的 Agent ──────────

    [Fact]
    public async Task ReviewNode_UsesTheStepAgent_WhenNoReviewerKeyIsConfigured()
    {
        var fake = new FakeAgents("""{"verdict":"accept","feedback":"ok"}""", ChosenAgent());
        var node = new ReviewNode(fake.Context, Mock.Of<ILogger<ReviewNode>>());

        await node.ExecuteAsync(Context(new WorkflowStepDto
        {
            StepId = "review",
            AgentId = ChosenAgentId,
            Configuration = new Dictionary<string, string> { ["nodeType"] = "review" }
        }, new WorkflowState("draft")));

        var created = fake.Created.Single();
        created.AgentId.ShouldBe(ChosenAgentId);
        created.Provider.ShouldBe("agent-provider");
        created.Model.ShouldBe("agent-model");
        created.Instructions.ShouldBe("You are a senior reviewer.", "the chosen agent's own system prompt is used");
    }

    [Fact]
    public async Task ReviewNode_ReviewerKey_TakesPrecedenceOverTheStepAgent()
    {
        var reviewerKeyAgent = Guid.NewGuid();
        var fake = new FakeAgents("""{"verdict":"accept","feedback":"ok"}""");
        var node = new ReviewNode(fake.Context, Mock.Of<ILogger<ReviewNode>>());

        await node.ExecuteAsync(Context(new WorkflowStepDto
        {
            StepId = "review",
            AgentId = ChosenAgentId,
            Configuration = new Dictionary<string, string>
            {
                ["nodeType"] = "review",
                ["reviewerAgentId"] = reviewerKeyAgent.ToString()
            }
        }, new WorkflowState("draft")));

        fake.Created.Single().AgentId.ShouldBe(reviewerKeyAgent);
    }

    [Fact]
    public async Task ReviewNode_WithoutAnyAgent_KeepsTheBuiltInReviewerPrompt()
    {
        var fake = new FakeAgents("""{"verdict":"accept","feedback":"ok"}""");
        var node = new ReviewNode(fake.Context, Mock.Of<ILogger<ReviewNode>>());

        await node.ExecuteAsync(Context(new WorkflowStepDto
        {
            StepId = "review",
            Configuration = new Dictionary<string, string> { ["nodeType"] = "review" }
        }, new WorkflowState("draft")));

        var created = fake.Created.Single();
        created.AgentId.ShouldBeNull();
        created.Instructions.ShouldNotBeNull();
        created.Instructions.ShouldContain("You are a reviewer");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ReviewNode_ChosenAgentWithoutAPrompt_KeepsTheBuiltInReviewerPrompt(string? agentInstructions)
    {
        var agent = ChosenAgent();
        agent.Instructions = agentInstructions;
        var fake = new FakeAgents("""{"verdict":"accept","feedback":"ok"}""", agent);
        var node = new ReviewNode(fake.Context, Mock.Of<ILogger<ReviewNode>>());

        await node.ExecuteAsync(Context(new WorkflowStepDto
        {
            StepId = "review",
            AgentId = ChosenAgentId,
            Configuration = new Dictionary<string, string> { ["nodeType"] = "review" }
        }, new WorkflowState("draft")));

        var created = fake.Created.Single();
        created.Model.ShouldBe("agent-model");
        created.Instructions.ShouldNotBeNull();
        created.Instructions.ShouldContain("You are a reviewer");
    }

    [Fact]
    public async Task RouterNode_UsesTheStepAgent_WhenNoRouterKeyIsConfigured()
    {
        var fake = new FakeAgents("billing", ChosenAgent());
        var node = new RouterNode(fake.Context, Mock.Of<ILogger<RouterNode>>());

        await node.ExecuteAsync(Context(new WorkflowStepDto
        {
            StepId = "classify",
            AgentId = ChosenAgentId,
            Configuration = RoutesConfig()
        }, new WorkflowState("refund please")));

        var created = fake.Created.Single();
        created.AgentId.ShouldBe(ChosenAgentId);
        created.Model.ShouldBe("agent-model");
    }

    [Fact]
    public async Task SynthesizeNode_UsesTheStepAgent_WhenNoSynthesizerKeyIsConfigured()
    {
        var fake = new FakeAgents("merged", ChosenAgent());
        var node = new SynthesizeNode(fake.Context);

        await node.ExecuteAsync(Context(new WorkflowStepDto
        {
            StepId = "synthesize",
            AgentId = ChosenAgentId
        }, new WorkflowState("inputs")));

        var created = fake.Created.Single();
        created.AgentId.ShouldBe(ChosenAgentId);
        created.Instructions.ShouldBe("You are a senior reviewer.", "without step instructions the chosen agent's prompt applies");
    }

    [Fact]
    public async Task BuilderReviewStep_ReviewerAgent_ReachesTheReviewNode()
    {
        // Builder 的 AddReviewStep(stepId, agentId) 把审查者写在 AgentId 上：它此前在运行时不生效。
        var reviewerId = Guid.NewGuid();
        var steps = WorkflowBuilder.Create()
            .AddStep("worker")
            .AddReviewStep("review", agentId: reviewerId).DependsOn("worker")
            .BuildGraph().Nodes;
        var reviewStep = steps.Single(s => s.StepId == "review");

        var fake = new FakeAgents("""{"verdict":"accept","feedback":"ok"}""");
        var node = new ReviewNode(fake.Context, Mock.Of<ILogger<ReviewNode>>());
        await node.ExecuteAsync(Context(reviewStep, StateWith(("worker", "the draft"))));

        fake.Created.Single().AgentId.ShouldBe(reviewerId);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static Agent ChosenAgent() => new()
    {
        Id = ChosenAgentId,
        Name = "Senior reviewer",
        Provider = "agent-provider",
        Model = "agent-model",
        Instructions = "You are a senior reviewer."
    };

    private static Dictionary<string, string> RoutesConfig() => new()
    {
        ["nodeType"] = "router",
        ["routes"] = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["billing"] = "Billing questions",
            ["technical"] = "Technical questions"
        })
    };

    private static WorkflowState StateWith(params (string StepId, string Output)[] outputs)
    {
        var state = new WorkflowState("write a brief");
        foreach (var (stepId, output) in outputs)
            state.SetOutput(stepId, output);
        return state;
    }

    private static WorkflowNodeContext Context(WorkflowStepDto step, WorkflowState state, IReadOnlyList<string>? dependencies = null)
    {
        var dependencyOutputs = new Dictionary<string, WorkflowStepOutput>(StringComparer.OrdinalIgnoreCase);
        foreach (var dep in dependencies ?? step.DependsOn ?? [])
        {
            if (state.GetOutput(dep) is { } output)
                dependencyOutputs[dep] = output;
        }

        return new WorkflowNodeContext
        {
            Step = step,
            State = state,
            DependencyOutputs = dependencyOutputs,
            ServiceProvider = Mock.Of<IServiceProvider>()
        };
    }

    /// <summary>记录节点向 <see cref="IAgentFactory"/> 要了什么样的 Agent、发给模型的用户消息是什么。</summary>
    private sealed class FakeAgents
    {
        public List<(string? Provider, string? Model, string? Instructions, Guid? AgentId)> Created { get; } = [];
        public List<string> UserMessages { get; } = [];
        public IWorkflowNodeServiceContext Context { get; }

        public FakeAgents(string llmResponse, Agent? storedAgent = null)
        {
            var chatClient = new Mock<IChatClient>();
            chatClient
                .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
                .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((messages, _, _) =>
                {
                    lock (UserMessages)
                    {
                        UserMessages.AddRange(messages.Where(m => m.Role == ChatRole.User).Select(m => m.Text).Take(1));
                    }
                })
                .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, llmResponse)));

            var factory = new Mock<IAgentFactory>();
            factory
                .Setup(f => f.CreateAgentAsync(
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(), It.IsAny<int?>(),
                    It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(),
                    It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string? provider, string? model, string? instructions, string? name,
                    IEnumerable<string>? _, double? _, int? _, AgentExecutorOptions? _,
                    IEnumerable<string>? _, IEnumerable<string>? _, Guid? agentId, CancellationToken _) =>
                {
                    lock (Created)
                    {
                        Created.Add((provider, model, instructions, agentId));
                    }

                    return new AgentExecutor(chatClient.Object, new AgentExecutorOptions { Name = name ?? "agent" });
                });

            var repository = new Mock<IRepository<Agent, Guid>>();
            if (storedAgent != null)
            {
                repository.Setup(r => r.GetAsync(storedAgent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(storedAgent);
            }

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(factory.Object);
            services.AddSingleton(repository.Object);
            var provider = services.BuildServiceProvider();

            var context = new Mock<IWorkflowNodeServiceContext>();
            context.Setup(c => c.AgentFactory).Returns(factory.Object);
            context.Setup(c => c.AgentRepository).Returns(repository.Object);
            context.Setup(c => c.CreateScope()).Returns(() => provider.CreateScope());
            Context = context.Object;
        }
    }
}
