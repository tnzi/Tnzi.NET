namespace Tnzi.AI.Agents.Definitions;

/// <summary>
/// Agent 定义同步后台服务 — 启动时从 YAML 文件同步到数据库
/// </summary>
/// <remarks>
/// 仅同步 Source="yaml" 的 Agent。Source="database"（通过 API 创建）的 Agent 不受影响。
/// 使用 DefinitionHash 避免不必要的数据库更新。
/// </remarks>
public class AgentDefinitionSyncService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IAgentDefinitionProvider _definitionProvider;
    private readonly IOptions<AIOptions> _options;
    private readonly ILogger<AgentDefinitionSyncService> _logger;

    public AgentDefinitionSyncService(
        IServiceProvider serviceProvider,
        IAgentDefinitionProvider definitionProvider,
        IOptions<AIOptions> options,
        ILogger<AgentDefinitionSyncService> logger)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        _definitionProvider = Check.NotNull(definitionProvider);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var config = _options.Value.AgentDefinitions;
        if (!config.Enabled || !config.SyncOnStartup)
        {
            _logger.LogDebug("Agent definition sync is disabled");
            return;
        }

        try
        {
            var definitions = await _definitionProvider.LoadDefinitionsAsync(cancellationToken);
            if (definitions.Count == 0)
            {
                _logger.LogDebug("No agent definitions found to sync");
                return;
            }

            await using var scope = _serviceProvider.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<Agent, Guid>>();

            // 批量加载所有已有 Agent，避免循环内 N+1 查询
            var existingAgents = await repository.AsQueryable()
                .Where(a => a.Source == AgentSources.Yaml || definitions.Select(d => d.Name).Contains(a.Name))
                .ToListAsync(cancellationToken);
            var agentsByName = existingAgents.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);

            var synced = 0;
            var skipped = 0;

            foreach (var definition in definitions)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var result = await SyncDefinitionAsync(repository, definition, agentsByName, cancellationToken);
                    if (result)
                        synced++;
                    else
                        skipped++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to sync agent definition '{Name}'", definition.Name);
                }
            }

            _logger.LogInformation(
                "Agent definition sync completed: {Synced} synced, {Skipped} skipped (unchanged or database-managed)",
                synced, skipped);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent definition sync failed during startup");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task<bool> SyncDefinitionAsync(IRepository<Agent, Guid> repository, AgentDefinitionDto definition, Dictionary<string, Agent> agentsByName, CancellationToken ct)
    {
        var hash = definition.DefinitionHash;

        // 从预加载的字典中查找已有 Agent
        agentsByName.TryGetValue(definition.Name, out var existing);

        if (existing != null)
        {
            // 跳过非 YAML 来源的 Agent（通过 API 创建的不覆盖）
            if (existing.Source != AgentSources.Yaml)
            {
                _logger.LogDebug("Skipping agent '{Name}': managed by database (Source={Source})", definition.Name, existing.Source);
                return false;
            }

            // 哈希未变则跳过
            if (hash != null && existing.DefinitionHash == hash)
            {
                _logger.LogDebug("Skipping agent '{Name}': definition unchanged", definition.Name);
                return false;
            }

            // 更新已有 Agent
            ApplyDefinition(existing, definition, hash);
            await repository.UpdateAsync(existing);
            _logger.LogDebug("Updated agent '{Name}' from YAML definition", definition.Name);
            return true;
        }

        // 创建新 Agent
        var agent = new Agent { Source = AgentSources.Yaml };
        ApplyDefinition(agent, definition, hash);
        await repository.InsertAsync(agent);
        _logger.LogDebug("Created agent '{Name}' from YAML definition", definition.Name);
        return true;
    }

    /// <summary>
    /// 将定义应用到 Agent 实体
    /// </summary>
    private static void ApplyDefinition(Agent agent, AgentDefinitionDto definition, string? hash)
    {
        agent.Name = definition.Name;
        agent.Description = definition.Description;
        agent.Instructions = definition.Instructions;
        agent.Provider = definition.Provider ?? string.Empty;
        agent.Model = definition.Model;
        agent.ToolGroups = definition.ToolGroups is { Count: > 0 } ? definition.ToolGroups : null;
        agent.Temperature = definition.Temperature;
        agent.MaxTokens = definition.MaxTokens;
        agent.TimeoutSeconds = definition.TimeoutSeconds;
        agent.ExecutionMode = (AgentExecutionMode)definition.ExecutionMode;
        agent.Configuration = definition.Configuration;
        agent.Domains = definition.Domains is { Count: > 0 } ? definition.Domains : null;
        agent.Roles = definition.Roles is { Count: > 0 } ? definition.Roles : null;
        agent.QualityTier = definition.QualityTier;
        agent.LatencyTier = definition.LatencyTier;
        agent.CostTier = definition.CostTier;
        agent.IsEnabled = definition.IsEnabled;
        agent.DefinitionHash = hash;
    }
}
