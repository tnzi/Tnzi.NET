namespace Tnzi.AI.Services;

/// <summary>
/// Agent 验证服务 — 检查 Agent 配置的完整性和有效性
/// </summary>
public class AgentValidationService : ApplicationService, IAgentValidationService
{
    private readonly IRepository<Agent, Guid> _agentRepository;
    private readonly IChatClientFactory _chatClientFactory;
    private readonly IToolRegistry _toolRegistry;
    private readonly ISkillRegistry? _skillRegistry;

    public AgentValidationService(
        IRepository<Agent, Guid> agentRepository,
        IChatClientFactory chatClientFactory,
        IToolRegistry toolRegistry,
        IServiceProvider serviceProvider,
        ISkillRegistry? skillRegistry = null)
        : base(serviceProvider)
    {
        _agentRepository = Check.NotNull(agentRepository);
        _chatClientFactory = Check.NotNull(chatClientFactory);
        _toolRegistry = Check.NotNull(toolRegistry);
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
        foreach (var agent in enabledAgents)
        {
            var validationResult = await ValidateCoreAsync(agent);
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

    private async Task<AgentValidationResultDto> ValidateCoreAsync(Agent agent)
    {
        var checks = new List<ValidationCheckDto>();

        // Check 1: Provider availability
        checks.Add(ValidateProvider(agent));

        // Check 2: Model availability
        checks.Add(ValidateModel(agent));

        // Check 3: Tool groups registered
        checks.Add(ValidateToolGroups(agent));

        // Check 4: Handoff targets exist (for Handoff/AgentAsTools modes)
        if (agent.ExecutionMode is AgentExecutionMode.Handoff or AgentExecutionMode.AgentAsTools)
        {
            checks.Add(await ValidateHandoffTargetsAsync(agent));
        }

        // Check 5: Skill references valid
        checks.Add(await ValidateSkillReferencesAsync(agent));

        return new AgentValidationResultDto
        {
            AgentId = agent.Id,
            AgentName = agent.Name,
            IsValid = checks.All(c => c.Passed),
            Checks = checks
        };
    }

    private ValidationCheckDto ValidateProvider(Agent agent)
    {
        var providers = _chatClientFactory.GetAvailableProviders();
        var isValid = providers.Any(p => string.Equals(p, agent.Provider, StringComparison.OrdinalIgnoreCase));

        return new ValidationCheckDto
        {
            Name = "provider",
            Passed = isValid,
            Details = isValid
                ? $"Provider '{agent.Provider}' is available"
                : $"Provider '{agent.Provider}' is not registered. Available: {string.Join(", ", providers)}"
        };
    }

    private ValidationCheckDto ValidateModel(Agent agent)
    {
        if (string.IsNullOrWhiteSpace(agent.Model))
        {
            // No model specified — will use default, which is fine
            var defaultModel = _chatClientFactory.GetDefaultModel(agent.Provider);
            return new ValidationCheckDto
            {
                Name = "model",
                Passed = !string.IsNullOrWhiteSpace(defaultModel),
                Details = !string.IsNullOrWhiteSpace(defaultModel)
                    ? $"No model specified, will use default: {defaultModel}"
                    : $"No model specified and no default model configured for provider '{agent.Provider}'"
            };
        }

        // Model is specified — we can't fully validate it without calling the API,
        // but we can check it's not empty
        return new ValidationCheckDto
        {
            Name = "model",
            Passed = true,
            Details = $"Model '{agent.Model}' specified"
        };
    }

    private ValidationCheckDto ValidateToolGroups(Agent agent)
    {
        if (agent.ToolGroups == null || agent.ToolGroups.Count == 0)
        {
            return new ValidationCheckDto
            {
                Name = "tool_groups",
                Passed = true,
                Details = "No tool groups configured"
            };
        }

        var registeredGroups = _toolRegistry.GetAllGroupNames().ToList();
        var missingGroups = agent.ToolGroups.Where(g => !registeredGroups.Contains(g, StringComparer.OrdinalIgnoreCase)).ToList();

        return new ValidationCheckDto
        {
            Name = "tool_groups",
            Passed = missingGroups.Count == 0,
            Details = missingGroups.Count == 0
                ? $"All {agent.ToolGroups.Count} tool groups are registered"
                : $"Missing tool groups: {string.Join(", ", missingGroups)}"
        };
    }

    private async Task<ValidationCheckDto> ValidateHandoffTargetsAsync(Agent agent)
    {
        if (string.IsNullOrWhiteSpace(agent.Configuration))
        {
            return new ValidationCheckDto
            {
                Name = "handoff_targets",
                Passed = true,
                Details = "No handoff configuration"
            };
        }

        try
        {
            using var doc = JsonDocument.Parse(agent.Configuration);
            if (!doc.RootElement.TryGetProperty("HandoffTargets", out var targetsElement) &&
                !doc.RootElement.TryGetProperty("handoffTargets", out targetsElement))
            {
                return new ValidationCheckDto
                {
                    Name = "handoff_targets",
                    Passed = true,
                    Details = "No handoff targets configured"
                };
            }

            var targetIds = new List<Guid>();
            foreach (var target in targetsElement.EnumerateArray())
            {
                if (target.TryGetProperty("AgentId", out var agentIdProp) || target.TryGetProperty("agentId", out agentIdProp))
                {
                    if (Guid.TryParse(agentIdProp.GetString(), out var targetId))
                        targetIds.Add(targetId);
                }
            }

            if (targetIds.Count == 0)
            {
                return new ValidationCheckDto
                {
                    Name = "handoff_targets",
                    Passed = true,
                    Details = "No handoff target agent IDs found"
                };
            }

            var existingIds = await _agentRepository
                .Where(a => targetIds.Contains(a.Id))
                .Select(a => a.Id)
                .ToListAsync();

            var missingIds = targetIds.Except(existingIds).ToList();

            return new ValidationCheckDto
            {
                Name = "handoff_targets",
                Passed = missingIds.Count == 0,
                Details = missingIds.Count == 0
                    ? $"All {targetIds.Count} handoff target agents exist"
                    : $"Missing handoff target agents: {string.Join(", ", missingIds)}"
            };
        }
        catch (JsonException)
        {
            return new ValidationCheckDto
            {
                Name = "handoff_targets",
                Passed = false,
                Details = "Configuration JSON is malformed"
            };
        }
    }

    private Task<ValidationCheckDto> ValidateSkillReferencesAsync(Agent agent)
    {
        // Check [RequiresSkill] references can't be validated per-agent from configuration alone
        // since they're attribute-based. Just verify skill registry is available.
        var result = new ValidationCheckDto
        {
            Name = "skill_registry",
            Passed = _skillRegistry != null,
            Details = _skillRegistry != null
                ? "Skill registry is available"
                : "Skill registry is not registered (SkillService may not be loaded)"
        };

        return Task.FromResult(result);
    }
}
