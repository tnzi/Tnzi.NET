namespace Tnzi.AI.Services;

/// <summary>
/// Default implementation - delegates the actual scanning to
/// <see cref="IWorkspaceAgentProvider"/> and projects the parsed
/// <see cref="WorkspaceAgentDefinition"/> shapes to admin DTOs.
/// </summary>
public class WorkspaceAgentAdminService : IWorkspaceAgentAdminService
{
    private readonly IWorkspaceAgentProvider _provider;
    private readonly IOptionsMonitor<AIOptions> _options;
    private readonly ILogger<WorkspaceAgentAdminService> _logger;
    private readonly IHostEnvironment? _hostEnvironment;

    public WorkspaceAgentAdminService(
        IWorkspaceAgentProvider provider,
        IOptionsMonitor<AIOptions> options,
        ILogger<WorkspaceAgentAdminService> logger,
        IHostEnvironment? hostEnvironment = null)
    {
        _provider = Check.NotNull(provider);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
        _hostEnvironment = hostEnvironment;
    }

    /// <summary>
    /// 与运行时回退（AgentResolver）读同一份配置 <c>AI:Workspace</c>。此前注入的 <c>IOptions&lt;WorkspaceOptions&gt;</c>
    /// 从未绑定任何配置节，列表恒用默认值：配置了 GlobalPath 时，管理端列的与运行时加载的是两个不同目录，
    /// 关掉 Enabled 也照样列出。
    /// </summary>
    private WorkspaceOptions Options => _options.CurrentValue.Workspace;

    public async Task<Result<List<WorkspaceAgentDto>>> ListAgentsAsync(CancellationToken cancellationToken = default)
    {
        var options = Options;
        if (!options.Enabled)
        {
            return Result<List<WorkspaceAgentDto>>.Success([]);
        }

        var results = new List<WorkspaceAgentDto>();

        // Global path: AI:Workspace:GlobalPath (default ~/.tnzi/agents/)
        await DiscoverScopeAsync(options.GlobalPath, "Global", results, cancellationToken);

        // Project path: .agents/ relative to ContentRootPath (host-controlled)
        if (options.ScanProjectPath && _hostEnvironment != null)
        {
            var projectPath = Path.Combine(_hostEnvironment.ContentRootPath, ".agents");
            await DiscoverScopeAsync(projectPath, "Project", results, cancellationToken);
        }

        return Result<List<WorkspaceAgentDto>>.Success(results);
    }

    public async Task<Result<List<WorkspacePersonaDto>>> ListPersonasAsync(CancellationToken cancellationToken = default)
    {
        if (!Options.Enabled)
        {
            return Result<List<WorkspacePersonaDto>>.Success([]);
        }
        // Personas live next to AGENT.md files - discover via the same path
        // walk and surface the `PersonaContent` field when present.
        var agents = await ListAgentsAsync(cancellationToken);
        if (!agents.Succeeded || agents.Data == null)
        {
            return Result<List<WorkspacePersonaDto>>.Success([]);
        }

        var personas = new List<WorkspacePersonaDto>();
        foreach (var agentDto in agents.Data)
        {
            if (!agentDto.HasPersona) continue;
            var folder = Path.GetDirectoryName(agentDto.FilePath);
            if (folder == null) continue;
            var personaPath = Path.Combine(folder, "PERSONA.md");
            if (!File.Exists(personaPath)) continue;
            personas.Add(new WorkspacePersonaDto
            {
                Name = agentDto.Name + " · Persona",
                AgentId = agentDto.AgentId,
                FilePath = personaPath,
                WorkspaceScope = agentDto.WorkspaceScope,
            });
        }
        return Result<List<WorkspacePersonaDto>>.Success(personas);
    }

    private async Task DiscoverScopeAsync(string path, string scope, List<WorkspaceAgentDto> sink, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }
        try
        {
            var definitions = await _provider.DiscoverAsync(path, ct);
            foreach (var def in definitions)
            {
                sink.Add(Project(def, scope));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Discover failures (permissions, malformed files) shouldn't block
            // the admin endpoint - workspace agents are an optional surface - but they must be visible.
            _logger.LogWarning(ex, "Workspace agent discovery failed for the {Scope} path {Path}", scope, path);
        }
    }

    private static WorkspaceAgentDto Project(WorkspaceAgentDefinition def, string scope)
    {
        return new WorkspaceAgentDto
        {
            AgentId = def.AgentId,
            Name = def.Name,
            Description = def.Description,
            Provider = def.Provider,
            Model = def.Model,
            ToolGroups = def.ToolGroups,
            ExecutionMode = def.ExecutionMode,
            Domains = def.Domains,
            Roles = def.Roles,
            QualityTier = def.QualityTier,
            FilePath = def.FilePath,
            WorkspaceScope = scope,
            HasPersona = !string.IsNullOrEmpty(def.PersonaContent),
            Instructions = def.Instructions,
            PersonaContent = def.PersonaContent,
        };
    }
}
