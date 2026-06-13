namespace Tnzi.AI.Engine;

/// <summary>
/// 工作流 Fluent Builder 接口 — 以代码方式定义 DAG 工作流
/// </summary>
/// <remarks>
/// 用法示例:
/// <code>
/// var graph = builder
///     .AddStep("extract", agentId: extractAgentId)
///         .WithInstructions("Extract key facts from: {{input}}")
///     .AddStep("summarize", agentId: summarizeAgentId)
///         .DependsOn("extract")
///         .WithRetry(maxRetries: 2, delaySeconds: 3)
///         .WithTimeout(120)
///     .AddReviewStep("review", agentId: reviewAgentId)
///         .DependsOn("summarize")
///         .RequiresApproval()
///     .AddConditionalEdge("review", new Dictionary&lt;string, string&gt;
///     {
///         ["approved"] = "publish",
///         ["rejected"] = "revise"
///     })
///     .BuildGraph();
/// </code>
/// </remarks>
public interface IWorkflowBuilder
{
    /// <summary>
    /// 添加一个工作流步骤
    /// </summary>
    /// <param name="stepId">步骤唯一 ID</param>
    /// <param name="agentId">Agent ID（可选）</param>
    IWorkflowStepBuilder AddStep(string stepId, Guid? agentId = null);

    /// <summary>添加 Review 步骤（nodeType = "review"）</summary>
    IWorkflowStepBuilder AddReviewStep(string stepId, Guid? agentId = null);

    /// <summary>添加 Router 步骤（nodeType = "router"）</summary>
    IWorkflowStepBuilder AddRouterStep(string stepId, Guid? agentId = null);

    /// <summary>添加 Parallel 步骤（nodeType = "parallel"）</summary>
    IWorkflowStepBuilder AddParallelStep(string stepId, Guid? agentId = null);

    /// <summary>添加 Synthesize 步骤（nodeType = "synthesize"）</summary>
    IWorkflowStepBuilder AddSynthesizeStep(string stepId, Guid? agentId = null);

    /// <summary>添加 Debate 步骤（nodeType = "debate"）</summary>
    IWorkflowStepBuilder AddDebateStep(string stepId, Guid? agentId = null);

    /// <summary>添加 Transform 步骤（nodeType = "transform"）</summary>
    IWorkflowStepBuilder AddTransformStep(string stepId, Guid? agentId = null);

    /// <summary>添加条件边</summary>
    IWorkflowBuilder AddConditionalEdge(string fromNodeId, Dictionary<string, string> routes, string? defaultTarget = null, EdgeConditionType conditionType = EdgeConditionType.OutputContains);

    /// <summary>添加循环</summary>
    IWorkflowBuilder AddLoop(string loopId, int maxIterations, Action<IWorkflowBuilder> loopBody);

    /// <summary>
    /// 构建 DAG 步骤列表（向后兼容）
    /// </summary>
    IReadOnlyList<WorkflowStepDto> Build();

    /// <summary>
    /// 构建 WorkflowGraph（新 API，包含条件边和循环）
    /// </summary>
    WorkflowGraph BuildGraph();
}

/// <summary>
/// 工作流步骤 Fluent Builder 接口
/// </summary>
public interface IWorkflowStepBuilder : IWorkflowBuilder
{
    /// <summary>添加前置依赖步骤</summary>
    IWorkflowStepBuilder DependsOn(params string[] stepIds);

    /// <summary>设置步骤执行条件</summary>
    IWorkflowStepBuilder WithCondition(string condition);

    /// <summary>设置步骤自定义指令</summary>
    IWorkflowStepBuilder WithInstructions(string instructions);

    /// <summary>覆盖 Provider</summary>
    IWorkflowStepBuilder WithProvider(string provider);

    /// <summary>覆盖 Model</summary>
    IWorkflowStepBuilder WithModel(string model);

    /// <summary>设置重试策略</summary>
    IWorkflowStepBuilder WithRetry(int maxRetries, int delaySeconds = 2);

    /// <summary>设置步骤超时</summary>
    IWorkflowStepBuilder WithTimeout(int timeoutSeconds);

    /// <summary>标记为需要人工审批</summary>
    IWorkflowStepBuilder RequiresApproval();

    /// <summary>设置自定义配置</summary>
    IWorkflowStepBuilder WithConfiguration(string key, string value);
}

/// <summary>
/// 工作流 Fluent Builder 实现
/// </summary>
public class WorkflowBuilder : IWorkflowBuilder
{
    private readonly List<StepBuilderEntry> _steps = [];
    private readonly List<ConditionalEdge> _conditionalEdges = [];
    private readonly Dictionary<string, LoopDefinition> _loops = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public IWorkflowStepBuilder AddStep(string stepId, Guid? agentId = null)
    {
        Check.NotNullOrWhiteSpace(stepId);
        var entry = new StepBuilderEntry(this, stepId, agentId);
        _steps.Add(entry);
        return entry;
    }

    /// <inheritdoc />
    public IWorkflowStepBuilder AddReviewStep(string stepId, Guid? agentId = null)
    {
        return AddTypedStep(stepId, WorkflowNodeTypes.Review, agentId);
    }

    /// <inheritdoc />
    public IWorkflowStepBuilder AddRouterStep(string stepId, Guid? agentId = null)
    {
        return AddTypedStep(stepId, WorkflowNodeTypes.Router, agentId);
    }

    /// <inheritdoc />
    public IWorkflowStepBuilder AddParallelStep(string stepId, Guid? agentId = null)
    {
        return AddTypedStep(stepId, WorkflowNodeTypes.Parallel, agentId);
    }

    /// <inheritdoc />
    public IWorkflowStepBuilder AddSynthesizeStep(string stepId, Guid? agentId = null)
    {
        return AddTypedStep(stepId, WorkflowNodeTypes.Synthesize, agentId);
    }

    /// <inheritdoc />
    public IWorkflowStepBuilder AddDebateStep(string stepId, Guid? agentId = null)
    {
        return AddTypedStep(stepId, WorkflowNodeTypes.Debate, agentId);
    }

    /// <inheritdoc />
    public IWorkflowStepBuilder AddTransformStep(string stepId, Guid? agentId = null)
    {
        return AddTypedStep(stepId, WorkflowNodeTypes.Transform, agentId);
    }

    /// <inheritdoc />
    public IWorkflowBuilder AddConditionalEdge(string fromNodeId, Dictionary<string, string> routes, string? defaultTarget = null, EdgeConditionType conditionType = EdgeConditionType.OutputContains)
    {
        Check.NotNullOrWhiteSpace(fromNodeId);
        Check.NotNullOrEmpty(routes);

        _conditionalEdges.Add(new ConditionalEdge
        {
            FromNodeId = fromNodeId,
            Routes = routes,
            DefaultTarget = defaultTarget,
            ConditionType = conditionType
        });
        return this;
    }

    /// <inheritdoc />
    public IWorkflowBuilder AddLoop(string loopId, int maxIterations, Action<IWorkflowBuilder> loopBody)
    {
        Check.NotNullOrWhiteSpace(loopId);
        Check.NotNull(loopBody);

        // 记录循环前的步骤数
        var beforeCount = _steps.Count;

        // 让调用者在 builder 上添加循环内的步骤
        loopBody(this);

        // 收集循环内新增的步骤 ID
        var loopNodeIds = _steps.Skip(beforeCount).Select(s => s.StepId).ToList();

        if (loopNodeIds.Count == 0)
        {
            throw new InvalidOperationException($"Loop '{loopId}' must have at least one step.");
        }

        if (_loops.ContainsKey(loopId))
            throw new InvalidOperationException($"Loop with ID '{loopId}' already exists.");

        _loops[loopId] = new LoopDefinition
        {
            NodeIds = loopNodeIds.AsReadOnly(),
            MaxIterations = maxIterations
        };

        return this;
    }

    /// <inheritdoc />
    public IReadOnlyList<WorkflowStepDto> Build()
    {
        if (_steps.Count == 0)
        {
            throw new InvalidOperationException("Workflow must have at least one step.");
        }

        ValidateSteps();

        return _steps.Select((s, i) => s.ToDto(i)).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public WorkflowGraph BuildGraph()
    {
        if (_steps.Count == 0)
        {
            throw new InvalidOperationException("Workflow must have at least one step.");
        }

        ValidateSteps();

        var nodes = _steps.Select((s, i) => s.ToDto(i)).ToList().AsReadOnly();

        return new WorkflowGraph(
            nodes,
            _conditionalEdges.Count > 0 ? _conditionalEdges.AsReadOnly() : null,
            _loops.Count > 0 ? new Dictionary<string, LoopDefinition>(_loops) : null);
    }

    /// <summary>
    /// 创建新的 WorkflowBuilder
    /// </summary>
    public static WorkflowBuilder Create() => new();

    /// <summary>
    /// 添加带 nodeType 的步骤
    /// </summary>
    private IWorkflowStepBuilder AddTypedStep(string stepId, string nodeType, Guid? agentId)
    {
        Check.NotNullOrWhiteSpace(stepId);
        var entry = new StepBuilderEntry(this, stepId, agentId);
        entry.ConfigurationValues["nodeType"] = nodeType;
        _steps.Add(entry);
        return entry;
    }

    /// <summary>
    /// 验证步骤 ID 唯一性和依赖关系
    /// </summary>
    private void ValidateSteps()
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in _steps)
        {
            if (!ids.Add(step.StepId))
            {
                throw new InvalidOperationException($"Duplicate step ID: '{step.StepId}'.");
            }
        }

        foreach (var step in _steps)
        {
            if (step.DependsOnList == null) continue;
            foreach (var dep in step.DependsOnList)
            {
                if (!ids.Contains(dep))
                {
                    throw new InvalidOperationException($"Step '{step.StepId}' depends on unknown step '{dep}'.");
                }
            }
        }
    }

    /// <summary>
    /// 步骤构建器内部条目
    /// </summary>
    private sealed class StepBuilderEntry : IWorkflowStepBuilder
    {
        private readonly WorkflowBuilder _parent;

        public string StepId { get; }
        public Guid? AgentId { get; }
        public List<string>? DependsOnList { get; private set; }
        public string? Condition { get; private set; }
        public string? Instructions { get; private set; }
        public string? Provider { get; private set; }
        public string? Model { get; private set; }
        public int MaxRetriesValue { get; private set; }
        public int RetryDelaySecondsValue { get; private set; } = 2;
        public int? TimeoutSecondsValue { get; private set; }
        public bool RequiresApprovalValue { get; private set; }
        public Dictionary<string, string> ConfigurationValues { get; } = new(StringComparer.OrdinalIgnoreCase);

        public StepBuilderEntry(WorkflowBuilder parent, string stepId, Guid? agentId)
        {
            _parent = parent;
            StepId = stepId;
            AgentId = agentId;
        }

        public WorkflowStepDto ToDto(int order) => new()
        {
            StepId = StepId,
            AgentId = AgentId,
            Order = order,
            DependsOn = DependsOnList,
            Condition = Condition,
            Provider = Provider,
            Model = Model,
            Instructions = Instructions,
            MaxRetries = MaxRetriesValue,
            RetryDelaySeconds = RetryDelaySecondsValue,
            TimeoutSeconds = TimeoutSecondsValue,
            RequiresApproval = RequiresApprovalValue,
            Configuration = ConfigurationValues.Count > 0 ? new Dictionary<string, string>(ConfigurationValues) : null
        };

        public IWorkflowStepBuilder DependsOn(params string[] stepIds)
        {
            DependsOnList ??= [];
            DependsOnList.AddRange(stepIds);
            return this;
        }

        public IWorkflowStepBuilder WithCondition(string condition)
        {
            Condition = condition;
            return this;
        }

        public IWorkflowStepBuilder WithInstructions(string instructions)
        {
            Instructions = instructions;
            return this;
        }

        public IWorkflowStepBuilder WithProvider(string provider)
        {
            Provider = provider;
            return this;
        }

        public IWorkflowStepBuilder WithModel(string model)
        {
            Model = model;
            return this;
        }

        public IWorkflowStepBuilder WithRetry(int maxRetries, int delaySeconds = 2)
        {
            MaxRetriesValue = maxRetries;
            RetryDelaySecondsValue = delaySeconds;
            return this;
        }

        public IWorkflowStepBuilder WithTimeout(int timeoutSeconds)
        {
            TimeoutSecondsValue = timeoutSeconds;
            return this;
        }

        public IWorkflowStepBuilder WithConfiguration(string key, string value)
        {
            ConfigurationValues[key] = value;
            return this;
        }

        IWorkflowStepBuilder IWorkflowStepBuilder.RequiresApproval()
        {
            RequiresApprovalValue = true;
            return this;
        }

        // Forward IWorkflowBuilder methods to parent
        public IWorkflowStepBuilder AddStep(string stepId, Guid? agentId = null) => _parent.AddStep(stepId, agentId);
        public IWorkflowStepBuilder AddReviewStep(string stepId, Guid? agentId = null) => _parent.AddReviewStep(stepId, agentId);
        public IWorkflowStepBuilder AddRouterStep(string stepId, Guid? agentId = null) => _parent.AddRouterStep(stepId, agentId);
        public IWorkflowStepBuilder AddParallelStep(string stepId, Guid? agentId = null) => _parent.AddParallelStep(stepId, agentId);
        public IWorkflowStepBuilder AddSynthesizeStep(string stepId, Guid? agentId = null) => _parent.AddSynthesizeStep(stepId, agentId);
        public IWorkflowStepBuilder AddDebateStep(string stepId, Guid? agentId = null) => _parent.AddDebateStep(stepId, agentId);
        public IWorkflowStepBuilder AddTransformStep(string stepId, Guid? agentId = null) => _parent.AddTransformStep(stepId, agentId);
        public IWorkflowBuilder AddConditionalEdge(string fromNodeId, Dictionary<string, string> routes, string? defaultTarget = null, EdgeConditionType conditionType = EdgeConditionType.OutputContains) => _parent.AddConditionalEdge(fromNodeId, routes, defaultTarget, conditionType);
        public IWorkflowBuilder AddLoop(string loopId, int maxIterations, Action<IWorkflowBuilder> loopBody) => _parent.AddLoop(loopId, maxIterations, loopBody);
        public IReadOnlyList<WorkflowStepDto> Build() => _parent.Build();
        public WorkflowGraph BuildGraph() => _parent.BuildGraph();
    }
}
