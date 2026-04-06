namespace Tnzi.AI.Tests.Integration;

/// <summary>
/// Agent 编排策略集成测试 — 验证各执行策略通过 AgentRuntime 端到端运行
/// </summary>
public class AgentOrchestrationIntegrationTests
{
    #region 1. SingleAgent Strategy

    /// <summary>
    /// 单 Agent 策略 — 验证基本请求→响应流程，确认使用 SingleAgentStrategy
    /// </summary>
    public class SingleAgentStrategyTests : AiIntegrationTestBase
    {
        // 在构造函数期间无法访问 DbContext，所以用 agent 工厂方法解耦
        private Agent? _agent;

        private async Task<Agent> EnsureAgentAsync()
        {
            if (_agent != null) return _agent;
            _agent = await CreateAgentAsync("SingleAgent", "You are a single agent.");
            return _agent;
        }

        [Fact]
        public async Task Should_UseSingleStrategy_And_ReturnResponse()
        {
            // Arrange
            var agent = await EnsureAgentAsync();
            MockProvider.EnqueueResponse("Hello from SingleAgent");

            var request = CreateRequest("Hi", agentId: agent.Id);

            // Act
            var result = await Runtime.RunAsync(request);

            // Assert
            result.ShouldNotBeNull();
            result.Response.ShouldContain("Hello from SingleAgent");
            result.FinishReason.ShouldBe("stop");
            // SingleAgent 策略不产生 HandoffPath（直通模式）
            result.HandoffPath.ShouldBeNull();
            result.FinalAgentName.ShouldBeNull();
        }

        [Fact]
        public async Task Should_UseSingleStrategy_InStreaming()
        {
            // Arrange
            var agent = await EnsureAgentAsync();
            MockProvider.EnqueueResponse("streaming ok");

            var request = CreateRequest("Hi", agentId: agent.Id);

            // Act
            var chunks = new List<AgentStreamChunk>();
            await foreach (var chunk in Runtime.RunStreamingAsync(request))
            {
                chunks.Add(chunk);
            }

            // Assert
            chunks.ShouldNotBeEmpty();
            var textChunks = chunks.Where(c => c.Text != null).ToList();
            textChunks.ShouldNotBeEmpty();
            string.Concat(textChunks.Select(c => c.Text)).ShouldContain("streaming ok");
        }
    }

    #endregion

    #region 2. Router Strategy

    /// <summary>
    /// Router 策略 — Router Agent 选择目标 Agent 并委托执行
    /// </summary>
    public class RouterStrategyTests : OrchestrationTestBase
    {
        private Guid _mathAgentId;
        private Guid _codeAgentId;

        protected override AgentExecutionMode ExecutionMode => AgentExecutionMode.Router;

        protected override string BuildConfiguration(Dictionary<string, Guid> agentIds)
        {
            var config = new AgentExecutionConfigDto
            {
                Router = new RouterExecutionConfigDto
                {
                    Targets = agentIds,
                    AllowDirectResponse = false
                }
            };
            return AgentExecutionConfigDto.Serialize(config)!;
        }

        private async Task SetupAsync()
        {
            var mathAgent = await CreateTargetAgentAsync("MathAgent", "You are a math expert.");
            var codeAgent = await CreateTargetAgentAsync("CodeAgent", "You are a code expert.");
            _mathAgentId = mathAgent.Id;
            _codeAgentId = codeAgent.Id;

            await CreateOrchestratorAgentAsync("RouterAgent",
                new Dictionary<string, Guid>
                {
                    ["MathAgent"] = _mathAgentId,
                    ["CodeAgent"] = _codeAgentId
                },
                "You are a router.");
        }

        [Fact]
        public async Task Should_RouteToCorrectAgent_BasedOnToolCall()
        {
            // Arrange
            await SetupAsync();

            // Router 调用 route_to_agent → MathAgent
            MockProvider.EnqueueToolCall("route_to_agent",
                new { targetAgentName = "MathAgent", reason = "Math question" });
            // AgentExecutor 工具循环：tool result 后再调一次 LLM
            MockProvider.EnqueueResponse("Routing to MathAgent.");
            // MathAgent 响应
            MockProvider.EnqueueResponse("The answer is 42.");

            var request = CreateRequest("What is 6 * 7?", agentId: OrchestratorAgentId);

            // Act
            var result = await Runtime.RunAsync(request);

            // Assert
            result.ShouldNotBeNull();
            result.Response.ShouldContain("The answer is 42");
            result.HandoffPath.ShouldNotBeNull();
            result.HandoffPath.ShouldContain("RouterAgent");
            result.HandoffPath.ShouldContain("MathAgent");
            result.FinalAgentName.ShouldBe("MathAgent");
        }

        [Fact]
        public async Task Should_FallbackToFirstTarget_WhenDirectResponseDisabled()
        {
            // Arrange
            await SetupAsync();

            // Router 直接回复（不调用工具），AllowDirectResponse=false → fallback
            MockProvider.EnqueueResponse("I'm just responding directly.");
            // MathAgent（fallback 目标）响应
            MockProvider.EnqueueResponse("Fallback response from MathAgent.");

            var request = CreateRequest("Some question", agentId: OrchestratorAgentId);

            // Act
            var result = await Runtime.RunAsync(request);

            // Assert
            result.ShouldNotBeNull();
            result.HandoffPath.ShouldNotBeNull();
            result.HandoffPath.ShouldContain("MathAgent");
        }
    }

    #endregion

    #region 3. AgentAsTools Strategy

    /// <summary>
    /// AgentAsTools 策略 — 子 Agent 作为工具注入父 Agent
    /// </summary>
    public class AgentAsToolsStrategyTests : OrchestrationTestBase
    {
        protected override AgentExecutionMode ExecutionMode => AgentExecutionMode.AgentAsTools;

        protected override string BuildConfiguration(Dictionary<string, Guid> agentIds)
        {
            var config = new AgentExecutionConfigDto
            {
                AgentAsTools = new AgentAsToolsExecutionConfigDto
                {
                    Agents = agentIds,
                    MaxConcurrentSubAgents = 2,
                    SubAgentTimeoutSeconds = 30
                }
            };
            return AgentExecutionConfigDto.Serialize(config)!;
        }

        private async Task SetupAsync()
        {
            var searchAgent = await CreateTargetAgentAsync("SearchAgent", "You are a search specialist.");

            await CreateOrchestratorAgentAsync("OrchestratorAgent",
                new Dictionary<string, Guid>
                {
                    ["SearchAgent"] = searchAgent.Id
                },
                "You orchestrate sub-agents.");
        }

        [Fact]
        public async Task Should_InvokeSubAgent_AsToolAndIntegrateResult()
        {
            // Arrange
            await SetupAsync();

            // 父 Agent 调用 call_searchagent 工具
            MockProvider.EnqueueToolCall("call_searchagent",
                new { task = "Find weather in Tokyo" });
            // SearchAgent 响应（AgentFactory 从 MockProvider 创建的 ChatClient 读取队列）
            MockProvider.EnqueueResponse("Weather in Tokyo: 22C sunny.");
            // 父 Agent 整合子 Agent 结果后的最终响应（tool result 返回后 LLM 再调一次）
            MockProvider.EnqueueResponse("Based on my search, the weather in Tokyo is 22C and sunny.");

            var request = CreateRequest("What's the weather in Tokyo?", agentId: OrchestratorAgentId);

            // Act
            var result = await Runtime.RunAsync(request);

            // Assert
            result.ShouldNotBeNull();
            result.Response.ShouldContain("Tokyo");
            result.HandoffPath.ShouldNotBeNull();
            result.HandoffPath.ShouldContain("OrchestratorAgent");
            result.HandoffPath.ShouldContain("SearchAgent");
            result.FinalAgentName.ShouldBe("OrchestratorAgent");
        }
    }

    #endregion

    #region 4. Handoff Strategy

    /// <summary>
    /// Handoff 策略 — Agent A 转接给 Agent B，验证 handoff 路径
    /// </summary>
    public class HandoffStrategyTests : OrchestrationTestBase
    {
        private Guid _agentBId;

        protected override AgentExecutionMode ExecutionMode => AgentExecutionMode.Handoff;

        protected override string BuildConfiguration(Dictionary<string, Guid> agentIds)
        {
            var config = new AgentExecutionConfigDto
            {
                Handoff = new HandoffExecutionConfigDto
                {
                    Targets = agentIds,
                    MaxHandoffs = 5,
                    AllowReturnToSource = true
                }
            };
            return AgentExecutionConfigDto.Serialize(config)!;
        }

        private async Task SetupAsync()
        {
            var agentB = await CreateTargetAgentAsync("AgentB", "You are Agent B, a specialist.");
            _agentBId = agentB.Id;

            await CreateOrchestratorAgentAsync("AgentA",
                new Dictionary<string, Guid>
                {
                    ["AgentB"] = _agentBId
                },
                "You are Agent A. Hand off specialist questions to AgentB.");
        }

        [Fact]
        public async Task Should_HandoffToAgentB_And_RecordPath()
        {
            // Arrange
            await SetupAsync();

            // Agent A 调用 handoff_to_agent → AgentB
            MockProvider.EnqueueToolCall("handoff_to_agent",
                new { targetAgentName = "AgentB", reason = "Specialist question" });
            // AgentExecutor 工具循环：tool result 后再调一次 LLM
            MockProvider.EnqueueResponse("Handing off to AgentB.");
            // Agent B 响应
            MockProvider.EnqueueResponse("I am Agent B and this is my expert answer.");

            var request = CreateRequest("A specialist question", agentId: OrchestratorAgentId);

            // Act
            var result = await Runtime.RunAsync(request);

            // Assert
            result.ShouldNotBeNull();
            result.Response.ShouldContain("Agent B");
            result.HandoffPath.ShouldNotBeNull();
            result.HandoffPath.Count.ShouldBe(2);
            result.HandoffPath[0].ShouldBe("AgentA");
            result.HandoffPath[1].ShouldBe("AgentB");
            result.FinalAgentName.ShouldBe("AgentB");
        }

        [Fact]
        public async Task Should_StayAtAgentA_WhenNoHandoffTriggered()
        {
            // Arrange
            await SetupAsync();

            // Agent A 直接回复（不调用 handoff_to_agent）
            MockProvider.EnqueueResponse("I can handle this myself.");

            var request = CreateRequest("Simple question", agentId: OrchestratorAgentId);

            // Act
            var result = await Runtime.RunAsync(request);

            // Assert
            result.ShouldNotBeNull();
            result.Response.ShouldContain("handle this myself");
            result.HandoffPath.ShouldNotBeNull();
            result.HandoffPath.Count.ShouldBe(1);
            result.HandoffPath[0].ShouldBe("AgentA");
            result.FinalAgentName.ShouldBe("AgentA");
        }
    }

    #endregion

    #region 5. Strategy Resolution

    /// <summary>
    /// ExecutionStrategyResolver — 验证枚举到策略类的正确映射
    /// </summary>
    public class StrategyResolutionTests
    {
        [Fact]
        public void Should_ResolveSingleStrategy()
        {
            var strategy = ExecutionStrategyResolver.Resolve(AgentExecutionMode.Single, null);
            strategy.ShouldBeOfType<SingleAgentStrategy>();
            // 应该是全局单例
            strategy.ShouldBeSameAs(SingleAgentStrategy.Instance);
        }

        [Fact]
        public void Should_ResolveHandoffStrategy()
        {
            var config = new AgentExecutionConfigDto
            {
                Handoff = new HandoffExecutionConfigDto
                {
                    Targets = new Dictionary<string, Guid>
                    {
                        ["TargetA"] = Guid.NewGuid()
                    },
                    MaxHandoffs = 3
                }
            };

            var strategy = ExecutionStrategyResolver.Resolve(
                AgentExecutionMode.Handoff, AgentExecutionConfigDto.Serialize(config));
            strategy.ShouldBeOfType<HandoffExecutionStrategy>();
        }

        [Fact]
        public void Should_ResolveRouterStrategy()
        {
            var config = new AgentExecutionConfigDto
            {
                Router = new RouterExecutionConfigDto
                {
                    Targets = new Dictionary<string, Guid>
                    {
                        ["Expert"] = Guid.NewGuid()
                    },
                    AllowDirectResponse = false
                }
            };

            var strategy = ExecutionStrategyResolver.Resolve(
                AgentExecutionMode.Router, AgentExecutionConfigDto.Serialize(config));
            strategy.ShouldBeOfType<RouterExecutionStrategy>();
        }

        [Fact]
        public void Should_ResolveAgentAsToolsStrategy()
        {
            var config = new AgentExecutionConfigDto
            {
                AgentAsTools = new AgentAsToolsExecutionConfigDto
                {
                    Agents = new Dictionary<string, Guid>
                    {
                        ["Worker"] = Guid.NewGuid()
                    }
                }
            };

            var strategy = ExecutionStrategyResolver.Resolve(
                AgentExecutionMode.AgentAsTools, AgentExecutionConfigDto.Serialize(config));
            strategy.ShouldBeOfType<AgentAsToolsExecutionStrategy>();
        }

        [Fact]
        public void Should_ResolveHandoffStrategy_WithNullConfig()
        {
            // Handoff 策略即使没有配置也应该返回实例（使用默认配置）
            var strategy = ExecutionStrategyResolver.Resolve(AgentExecutionMode.Handoff, null);
            strategy.ShouldBeOfType<HandoffExecutionStrategy>();
        }

        [Fact]
        public void Should_ThrowForUnsupportedMode()
        {
            Should.Throw<InvalidOperationException>(() =>
                ExecutionStrategyResolver.Resolve((AgentExecutionMode)99, null));
        }
    }

    #endregion

    #region Base: 编排测试基类

    /// <summary>
    /// 多 Agent 编排测试基类 — 提供可配置的 AgentResolver，
    /// 支持在测试方法中设置编排 Agent 的 ExecutionMode 和 Configuration
    /// </summary>
    public abstract class OrchestrationTestBase : AiIntegrationTestBase
    {
        private Agent? _orchestratorAgent;
        private string? _orchestratorConfig;

        /// <summary>编排 Agent 的执行模式</summary>
        protected abstract AgentExecutionMode ExecutionMode { get; }

        /// <summary>编排 Agent 的 ID（SetupAsync 后可用）</summary>
        protected Guid OrchestratorAgentId => _orchestratorAgent?.Id ?? throw new InvalidOperationException("Call CreateOrchestratorAgentAsync first.");

        /// <summary>
        /// 由子类实现 — 根据目标 Agent ID 构建 Configuration JSON
        /// </summary>
        protected abstract string BuildConfiguration(Dictionary<string, Guid> agentIds);

        /// <summary>创建目标 Agent（写入数据库）</summary>
        protected async Task<Agent> CreateTargetAgentAsync(string name, string? instructions = null)
        {
            return await CreateAgentAsync(name, instructions);
        }

        /// <summary>创建编排 Agent（写入数据库 + 设置配置）</summary>
        protected async Task CreateOrchestratorAgentAsync(string name, Dictionary<string, Guid> targetAgents, string? instructions = null)
        {
            _orchestratorConfig = BuildConfiguration(targetAgents);

            var agent = new Agent
            {
                Name = name,
                Instructions = instructions ?? $"You are {name}.",
                Provider = "MockProvider",
                Model = "mock-model",
                IsEnabled = true,
                ExecutionMode = ExecutionMode,
                Configuration = _orchestratorConfig
            };

            DbContext.Set<Agent>().Add(agent);
            await DbContext.SaveChangesAsync();
            _orchestratorAgent = agent;
        }

        protected override void ConfigureAdditionalServices(IServiceCollection services)
        {
            // 覆盖 IAgentResolver — 根据 agentId 返回正确的 ExecutionMode + Configuration
            // 注意: 在构造期间注册的 mock，实际调用时读取运行时字段
            services.AddScoped<IAgentResolver>(sp =>
            {
                var resolverMock = new Mock<IAgentResolver>();
                resolverMock.Setup(r => r.ResolveAgentAsync(
                        It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                        It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((Guid? agentId, string? provider, string? model,
                        List<string>? toolGroups, CancellationToken ct) =>
                    {
                        var client = MockProvider.CreateChatClient(
                            new ProviderOptions { Enabled = true }, model ?? "mock-model");

                        // 如果请求的是编排 Agent，使用其 ExecutionMode + Configuration
                        var isOrchestrator = _orchestratorAgent != null && agentId == _orchestratorAgent.Id;
                        var agentName = isOrchestrator ? _orchestratorAgent!.Name : "TestAgent";
                        var agentInstructions = isOrchestrator ? _orchestratorAgent!.Instructions : null;
                        var executionMode = isOrchestrator ? ExecutionMode : AgentExecutionMode.Single;
                        var configuration = isOrchestrator ? _orchestratorConfig : null;

                        var executor = new AgentExecutor(client, new AgentExecutorOptions
                        {
                            Name = agentName,
                            Instructions = agentInstructions
                        });
                        return AgentResolution.Success(
                            executor, provider ?? "MockProvider", model ?? "mock-model",
                            agentId, configuration, executionMode);
                    });
                resolverMock.Setup(r => r.BuildChatMessageAsync(
                        It.IsAny<string?>(), It.IsAny<List<ContentPartDto>?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((string? msg, List<ContentPartDto>? parts, CancellationToken ct) =>
                        new ChatMessage(ChatRole.User, msg ?? string.Empty));

                return resolverMock.Object;
            });
        }
    }

    #endregion
}
