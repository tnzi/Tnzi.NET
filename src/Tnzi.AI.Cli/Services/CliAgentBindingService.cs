namespace Tnzi.AI.Cli.Services;

/// <summary>
/// Agent → 外部运行时绑定的真实实现。
/// </summary>
public class CliAgentBindingService : ApplicationService, ICliAgentBindingService
{
    private readonly IRepository<CliAgentBinding, Guid> _repository;
    private readonly IRepository<Entities.CliRuntime, Guid> _runtimeRepository;
    private readonly IRepository<Agent, Guid> _agentRepository;

    /// <summary>初始化绑定服务。</summary>
    public CliAgentBindingService(
        IRepository<CliAgentBinding, Guid> repository,
        IRepository<Entities.CliRuntime, Guid> runtimeRepository,
        IRepository<Agent, Guid> agentRepository,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _runtimeRepository = Check.NotNull(runtimeRepository);
        _agentRepository = Check.NotNull(agentRepository);
    }

    /// <inheritdoc />
    public async Task<CliAgentBindingDto?> GetByAgentIdAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        var binding = await _repository.FirstOrDefaultAsync(b => b.AgentId == agentId, cancellationToken);
        if (binding is null)
        {
            return null;
        }

        return await ProjectAsync(binding, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<CliAgentBindingDto>> UpsertAsync(
        Guid agentId, UpsertCliAgentBindingDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var agentExists = await _agentRepository.AnyAsync(a => a.Id == agentId, cancellationToken);
        if (!agentExists)
        {
            return Fail<CliAgentBindingDto>("Agent not found.", 404, ErrorCodes.AgentNotFound);
        }

        var runtime = await _runtimeRepository.GetAsync(input.CliRuntimeId, cancellationToken);
        if (runtime is null)
        {
            return Fail<CliAgentBindingDto>("External CLI runtime not found.", 404, ErrorCodes.CliRuntimeNotFound);
        }

        if (input.WorkDirectoryMode == CliWorkDirectoryMode.UserProvided
            && string.IsNullOrWhiteSpace(input.UserWorkDirectory))
        {
            return Fail<CliAgentBindingDto>(
                "UserWorkDirectory is required when WorkDirectoryMode is UserProvided.", 400, ErrorCodes.InternalError);
        }

        var binding = await _repository.AsQueryable(withTracking: true)
            .FirstOrDefaultAsync(b => b.AgentId == agentId, cancellationToken);

        var isNew = binding is null;
        binding ??= new CliAgentBinding { AgentId = agentId };

        binding.CliRuntimeId = input.CliRuntimeId;
        binding.Model = input.Model;
        binding.ThinkingLevel = input.ThinkingLevel;
        binding.CustomArgsJson = input.CustomArgs is { Count: > 0 }
            ? JsonSerializer.Serialize(input.CustomArgs, TnziJsonDefaults.Options)
            : null;
        binding.McpConfigJson = input.McpConfigJson;
        binding.WorkDirectoryMode = input.WorkDirectoryMode;
        binding.UserWorkDirectory = input.UserWorkDirectory;
        binding.InjectAgentInstructions = input.InjectAgentInstructions;
        binding.MaterializeSkills = input.MaterializeSkills;
        binding.IdleWatchdog = input.IdleWatchdog;

        if (isNew)
        {
            await _repository.InsertAsync(binding, cancellationToken);
        }
        else
        {
            await _repository.UpdateAsync(binding, cancellationToken);
        }

        await _repository.SaveChangesAsync(cancellationToken);

        Logger.LogInformation(
            "Agent {AgentId} is now bound to external CLI runtime {RuntimeId} ({ProviderKey})",
            agentId, runtime.Id, runtime.ProviderKey);

        return Ok(await ProjectAsync(binding, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        var binding = await _repository.FirstOrDefaultAsync(b => b.AgentId == agentId, cancellationToken);
        if (binding is null)
        {
            return Fail("The agent has no external CLI binding.", 404, ErrorCodes.CliBindingNotFound);
        }

        await _repository.DeleteAsync(binding, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        Logger.LogInformation("Agent {AgentId} unbound from external CLI execution", agentId);
        return Ok();
    }

    private async Task<CliAgentBindingDto> ProjectAsync(CliAgentBinding binding, CancellationToken cancellationToken)
    {
        var dto = binding.MapTo<CliAgentBindingDto>();
        dto.CustomArgs = string.IsNullOrWhiteSpace(binding.CustomArgsJson)
            ? null
            : JsonSerializer.Deserialize<List<string>>(binding.CustomArgsJson, TnziJsonDefaults.Options);

        var runtime = await _runtimeRepository.AsQueryable()
            .Where(r => r.Id == binding.CliRuntimeId)
            .Select(r => new { r.Name, r.ProviderKey })
            .FirstOrDefaultAsync(cancellationToken);

        dto.CliRuntimeName = runtime?.Name;
        dto.ProviderKey = runtime?.ProviderKey;
        return dto;
    }
}
