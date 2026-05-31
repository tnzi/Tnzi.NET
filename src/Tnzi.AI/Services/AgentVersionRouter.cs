namespace Tnzi.AI.Services;

/// <summary>
/// Agent 版本路由器实现 — 支持 A/B 测试流量分配
/// </summary>
public class AgentVersionRouter : IAgentVersionRouter
{
    private readonly IRepository<AgentVersion, Guid> _versionRepository;
    private readonly ILogger<AgentVersionRouter> _logger;

    public AgentVersionRouter(
        IRepository<AgentVersion, Guid> versionRepository,
        ILogger<AgentVersionRouter> logger)
    {
        _versionRepository = Check.NotNull(versionRepository);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task<AgentVersionRouteResult> RouteAsync(Agent agent, CancellationToken ct)
    {
        Check.NotNull(agent);

        // 尝试从 Configuration JSON 中解析 A/B 测试配置
        var abTestConfig = ExtractAbTestConfig(agent.Configuration);
        if (abTestConfig == null || !abTestConfig.Enabled)
        {
            return AgentVersionRouteResult.Passthrough(agent);
        }

        // 验证版本号不同
        if (abTestConfig.VersionA == abTestConfig.VersionB)
        {
            _logger.LogWarning(
                "A/B test for Agent {AgentId} has identical versions A={VersionA} and B={VersionB}, using passthrough",
                agent.Id, abTestConfig.VersionA, abTestConfig.VersionB);
            return AgentVersionRouteResult.Passthrough(agent);
        }

        // 确定性流量分配：基于 Agent ID + 版本号的确定性哈希，同一 Agent 始终路由到同一 variant
        var hashInput = HashCode.Combine(agent.Id, abTestConfig.VersionA, abTestConfig.VersionB);
        var roll = Math.Abs(hashInput % 100);
        var useVariantB = roll < abTestConfig.TrafficPercentB;
        var selectedVersion = useVariantB ? abTestConfig.VersionB : abTestConfig.VersionA;
        var selectedVariant = useVariantB ? "B" : "A";

        // 加载选中版本的配置快照
        var versionEntity = await _versionRepository
            .Where(v => v.AgentId == agent.Id && v.Version == selectedVersion)
            .FirstOrDefaultAsync(ct);

        if (versionEntity == null)
        {
            _logger.LogWarning(
                "A/B test version {Version} not found for Agent {AgentId}, falling back to current config",
                selectedVersion, agent.Id);
            return AgentVersionRouteResult.Passthrough(agent);
        }

        // 反序列化配置快照并创建覆盖后的 Agent 副本
        var snapshot = DeserializeSnapshot(versionEntity.ConfigSnapshot);
        if (snapshot == null)
        {
            _logger.LogWarning(
                "Failed to deserialize A/B test snapshot for Agent {AgentId} version {Version}, falling back to current config",
                agent.Id, selectedVersion);
            return AgentVersionRouteResult.Passthrough(agent);
        }

        var routedAgent = ApplySnapshot(agent, snapshot);

        _logger.LogDebug(
            "A/B test routed Agent {AgentId} to variant {Variant} (version {Version}), roll={Roll}, thresholdB={ThresholdB}",
            agent.Id, selectedVariant, selectedVersion, roll, abTestConfig.TrafficPercentB);

        return new AgentVersionRouteResult
        {
            Agent = routedAgent,
            IsAbTestRouted = true,
            SelectedVersion = selectedVersion,
            SelectedVariant = selectedVariant
        };
    }

    /// <summary>
    /// 从 Agent.Configuration JSON 中提取 A/B 测试配置
    /// </summary>
    public static AbTestConfig? ExtractAbTestConfig(string? configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration))
            return null;

        try
        {
            var wrapper = JsonSerializer.Deserialize<AbTestConfigWrapper>(configuration);
            return wrapper?.AbTest;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 将 A/B 测试配置合并写入 Agent.Configuration JSON
    /// </summary>
    public static string MergeAbTestConfig(string? existingConfiguration, AbTestConfig? abTestConfig)
    {
        JsonElement existingRoot = default;
        if (!string.IsNullOrWhiteSpace(existingConfiguration))
        {
            try
            {
                existingRoot = JsonSerializer.Deserialize<JsonElement>(existingConfiguration);
            }
            catch (JsonException)
            {
                // 无法解析现有 JSON，从头开始
            }
        }

        var dict = new Dictionary<string, JsonElement>();

        // 保留现有字段（排除 abTest）
        if (existingRoot.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in existingRoot.EnumerateObject())
            {
                if (!string.Equals(property.Name, "abTest", StringComparison.OrdinalIgnoreCase))
                {
                    dict[property.Name] = property.Value.Clone();
                }
            }
        }

        // 添加或移除 abTest 配置
        if (abTestConfig != null)
        {
            var abTestJson = JsonSerializer.SerializeToElement(abTestConfig);
            dict["abTest"] = abTestJson;
        }

        return dict.Count == 0 ? "{}" : JsonSerializer.Serialize(dict);
    }

    private static AgentConfigSnapshot? DeserializeSnapshot(string configSnapshot)
    {
        try
        {
            return JsonSerializer.Deserialize<AgentConfigSnapshot>(configSnapshot);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 将配置快照应用到 Agent 副本（不修改原始对象）
    /// </summary>
    private static Agent ApplySnapshot(Agent original, AgentConfigSnapshot snapshot)
    {
        return new Agent
        {
            Id = original.Id,
            TenantId = original.TenantId,
            Name = snapshot.Name,
            Description = snapshot.Description,
            Instructions = snapshot.Instructions,
            Provider = snapshot.Provider,
            Model = snapshot.Model,
            ToolGroups = snapshot.ToolGroups?.ToList(),
            Temperature = snapshot.Temperature,
            MaxTokens = snapshot.MaxTokens,
            TimeoutSeconds = snapshot.TimeoutSeconds,
            IsEnabled = snapshot.IsEnabled,
            ExecutionMode = snapshot.ExecutionMode,
            Configuration = snapshot.Configuration,
            Domains = snapshot.Domains?.ToList(),
            Roles = snapshot.Roles?.ToList(),
            QualityTier = snapshot.QualityTier,
            LatencyTier = snapshot.LatencyTier,
            CostTier = snapshot.CostTier,
            Source = original.Source,
            DefinitionHash = original.DefinitionHash,
            // Snapshot wins so A/B-routed variants can diverge persona.
            // Falls back to original.PersonaId for snapshots taken before the
            // PersonaId field existed (legacy AgentConfigSnapshot rows).
            PersonaId = snapshot.PersonaId ?? original.PersonaId
        };
    }
}
