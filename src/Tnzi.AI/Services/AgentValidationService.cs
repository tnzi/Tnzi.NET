namespace Tnzi.AI.Services;

/// <summary>
/// Agent 验证服务 - 检查 Agent 配置的完整性和有效性
/// </summary>
public class AgentValidationService : ApplicationService, IAgentValidationService
{
    private readonly IRepository<Agent, Guid> _agentRepository;
    private readonly IChatClientFactory _chatClientFactory;
    private readonly IToolRegistry _toolRegistry;
    private readonly IAgentGrantService _grantService;
    private readonly ISkillRegistry? _skillRegistry;

    public AgentValidationService(
        IRepository<Agent, Guid> agentRepository,
        IChatClientFactory chatClientFactory,
        IToolRegistry toolRegistry,
        IAgentGrantService grantService,
        IServiceProvider serviceProvider,
        ISkillRegistry? skillRegistry = null)
        : base(serviceProvider)
    {
        _agentRepository = Check.NotNull(agentRepository);
        _chatClientFactory = Check.NotNull(chatClientFactory);
        _toolRegistry = Check.NotNull(toolRegistry);
        _grantService = Check.NotNull(grantService);
        _skillRegistry = skillRegistry;
    }

    public async Task<Result<AgentValidationResultDto>> ValidateAsync(Guid agentId)
    {
        var agent = await _agentRepository.GetAsync(agentId);
        if (agent == null)
            return Fail<AgentValidationResultDto>("Agent not found", 404, ErrorCodes.AgentNotFound);

        return Ok(await ValidateCoreAsync(agent));
    }

    public async Task<Result<AgentHealthSummaryDto>> GetHealthSummaryAsync()
    {
        var agents = await _agentRepository.AsQueryable().ToListAsync();

        var summary = new AgentHealthSummaryDto
        {
            TotalAgents = agents.Count,
            DisabledAgents = agents.Count(a => !a.IsEnabled)
        };

        var enabledAgents = agents.Where(a => a.IsEnabled).ToList();

        // 批量取本次要校验的全部 Agent 授权（三表各一次 IN 查询），
        // 避免逐 Agent 调 GetGrantsAsync 的 N+1。
        var grantsByAgent = enabledAgents.Count > 0
            ? await _grantService.GetGrantsAsync(enabledAgents.Select(a => a.Id).ToList())
            : null;

        foreach (var agent in enabledAgents)
        {
            var validationResult = await ValidateCoreAsync(
                agent,
                grantsByAgent != null && grantsByAgent.TryGetValue(agent.Id, out var g) ? g : null);
            if (validationResult.IsValid)
            {
                summary.HealthyAgents++;
            }
            else
            {
                summary.UnhealthyAgents++;
                summary.UnhealthyDetails.Add(validationResult);
            }
        }

        return Ok(summary);
    }

    // prefetchedGrants: 已批量取好的授权投影（批量校验路径传入以避免 N+1）；为 null 时按需单查。
    private async Task<AgentValidationResultDto> ValidateCoreAsync(Agent agent, AgentGrantsProjection? prefetchedGrants = null)
    {
        var checks = new List<ValidationCheckDto>();

        // Check 1: Provider availability
        checks.Add(ValidateProvider(agent));

        // Check 2: Model availability
        checks.Add(ValidateModel(agent));

        // Check 3: Tool groups registered (sourced from the authoritative grant junction)
        var grants = prefetchedGrants ?? await _grantService.GetGrantsAsync(agent.Id);
        checks.Add(ValidateToolGroups(grants.ToolGroups));

        // Check 4: Target agents exist (multi-agent modes)
        if (agent.ExecutionMode is AgentExecutionMode.Handoff or AgentExecutionMode.AgentAsTools or AgentExecutionMode.Router)
        {
            checks.Add(await ValidateDelegationTargetsAsync(agent));
        }

        // Check 5: Skill registry present when the agent has skill grants
        checks.Add(ValidateSkillReferences(grants.SkillSlugs));

        return new AgentValidationResultDto
        {
            AgentId = agent.Id,
            AgentName = agent.Name,
            IsValid = checks.All(c => c.Passed),
            Checks = checks
        };
    }

    /// <remarks>
    /// 按名问工厂（与对话时解析提供商同一口径），而不是拿配置名单比对：管理端登记在库里的提供商不在配置名单里，
    /// 比对会把一个能正常对话的提供商报成「未注册」。
    /// </remarks>
    private ValidationCheckDto ValidateProvider(Agent agent)
    {
        // 空白 Provider（省略 provider 的 YAML 定义）在运行时解析为默认提供商，这里同一口径
        var isValid = _chatClientFactory.IsProviderAvailable(agent.Provider);
        var shown = DescribeProvider(agent);

        return new ValidationCheckDto
        {
            Name = "provider",
            Passed = isValid,
            Details = isValid
                ? $"Provider {shown} is available"
                : $"Provider {shown} is not registered or is disabled. Configured: {string.Join(", ", _chatClientFactory.GetAvailableProviders())}"
        };
    }

    private static string DescribeProvider(Agent agent)
        => string.IsNullOrWhiteSpace(agent.Provider) ? "(default provider)" : $"'{agent.Provider}'";

    private ValidationCheckDto ValidateModel(Agent agent)
    {
        if (string.IsNullOrWhiteSpace(agent.Model))
        {
            // No model specified - will use default, which is fine
            var defaultModel = _chatClientFactory.GetDefaultModel(agent.Provider);
            return new ValidationCheckDto
            {
                Name = "model",
                Passed = !string.IsNullOrWhiteSpace(defaultModel),
                Details = !string.IsNullOrWhiteSpace(defaultModel)
                    ? $"No model specified, will use default: {defaultModel}"
                    : $"No model specified and no default model configured for provider {DescribeProvider(agent)}"
            };
        }

        // Model is specified - we can't fully validate it without calling the API,
        // but we can check it's not empty
        return new ValidationCheckDto
        {
            Name = "model",
            Passed = true,
            Details = $"Model '{agent.Model}' specified"
        };
    }

    private ValidationCheckDto ValidateToolGroups(IReadOnlyList<string> toolGroups)
    {
        if (toolGroups.Count == 0)
        {
            return new ValidationCheckDto
            {
                Name = "tool_groups",
                Passed = true,
                Details = "No tool groups configured"
            };
        }

        var registeredGroups = _toolRegistry.GetAllGroupNames().ToList();
        var missingGroups = toolGroups.Where(g => !registeredGroups.Contains(g, StringComparer.OrdinalIgnoreCase)).ToList();

        return new ValidationCheckDto
        {
            Name = "tool_groups",
            Passed = missingGroups.Count == 0,
            Details = missingGroups.Count == 0
                ? $"All {toolGroups.Count} tool groups are registered"
                : $"Missing tool groups: {string.Join(", ", missingGroups)}"
        };
    }

    /// <summary>
    /// 多 Agent 模式（Handoff / AgentAsTools / Router）配置的目标 Agent 是否都还在、都启用、对调用者可见。
    /// </summary>
    /// <remarks>
    /// 按运行时同一口径读配置（<see cref="AgentExecutionConfigDto.Deserialize"/>，即 <c>ExecutionStrategyResolver</c> 读的那份）：
    /// 目标在 <c>handoff.targets</c> / <c>agentAsTools.agents</c> / <c>router.targets</c> 里，是「名称 → AgentId」的映射。
    /// 存在性也按运行时同一口径判（<c>ExecutionStrategyAgentLoader</c>）：租户调用者看得见本租户的与宿主共享的定义，
    /// 禁用的目标运行时同样解析不出来，一并算缺失。运行时找不到目标只记一条 Warning、静默返回源 Agent 的回复，
    /// 这项检查是唯一看得见的信号。
    /// </remarks>
    private async Task<ValidationCheckDto> ValidateDelegationTargetsAsync(Agent agent)
    {
        var config = AgentExecutionConfigDto.Deserialize(agent.Configuration);
        var targets = agent.ExecutionMode switch
        {
            AgentExecutionMode.Handoff => config?.Handoff?.Targets,
            AgentExecutionMode.AgentAsTools => config?.AgentAsTools?.Agents,
            AgentExecutionMode.Router => config?.Router?.Targets,
            _ => null
        };

        if (targets is not { Count: > 0 })
        {
            return new ValidationCheckDto
            {
                Name = "handoff_targets",
                Passed = false,
                Details = $"No target agents are configured for {agent.ExecutionMode} mode"
            };
        }

        var targetIds = targets.Values.Distinct().ToList();
        var visible = await SharedAgentScope.Resolve(ServiceProvider)
            .Apply(_agentRepository.AsQueryable())
            .Where(a => targetIds.Contains(a.Id))
            .Select(a => new { a.Id, a.IsEnabled })
            .ToListAsync();

        var enabledIds = visible.Where(a => a.IsEnabled).Select(a => a.Id).ToHashSet();
        var disabledIds = visible.Where(a => !a.IsEnabled).Select(a => a.Id).ToHashSet();
        var problems = targets
            .Where(t => !enabledIds.Contains(t.Value))
            .Select(t => disabledIds.Contains(t.Value)
                ? $"'{t.Key}' ({t.Value}) is disabled"
                : $"'{t.Key}' ({t.Value}) does not exist")
            .ToList();

        return new ValidationCheckDto
        {
            Name = "handoff_targets",
            Passed = problems.Count == 0,
            Details = problems.Count == 0
                ? $"All {targets.Count} target agents exist and are enabled"
                : $"Unavailable target agents: {string.Join("; ", problems)}"
        };
    }

    /// <summary>
    /// 授予了技能的 Agent 需要技能注册表（<c>Tnzi.AI.Skills</c>）；没有技能授权的 Agent 与它无关。
    /// </summary>
    /// <remarks>
    /// 技能注册表没有核心回退，不加载技能子模块时为 null。此前对每个 Agent 都要求它存在，
    /// 不加载技能子模块的部署里每个启用的 Agent 都被报成不健康，健康统计失去意义。
    /// </remarks>
    private ValidationCheckDto ValidateSkillReferences(IReadOnlyList<string> skillSlugs)
    {
        if (skillSlugs.Count == 0)
        {
            return new ValidationCheckDto
            {
                Name = "skill_registry",
                Passed = true,
                Details = "No skills granted"
            };
        }

        return new ValidationCheckDto
        {
            Name = "skill_registry",
            Passed = _skillRegistry != null,
            Details = _skillRegistry != null
                ? $"Skill registry is available for {skillSlugs.Count} granted skills"
                : $"{skillSlugs.Count} skills are granted but the skill registry is not registered (the skills module is not loaded)"
        };
    }
}
