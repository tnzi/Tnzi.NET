namespace Tnzi.AI.Services;

/// <summary>
/// Agent 版本路由器实现 - 支持 A/B 测试流量分配
/// </summary>
public class AgentVersionRouter : IAgentVersionRouter
{
    private readonly IRepository<AgentVersion, Guid> _versionRepository;
    private readonly ICurrentUser? _currentUser;
    private readonly ILogger<AgentVersionRouter> _logger;

    public AgentVersionRouter(
        IRepository<AgentVersion, Guid> versionRepository,
        ILogger<AgentVersionRouter> logger,
        ICurrentUser? currentUser = null)
    {
        _versionRepository = Check.NotNull(versionRepository);
        _logger = Check.NotNull(logger);
        _currentUser = currentUser;
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

        // 流量分配维度：优先按当前用户（实现真正的用户级 A/B 分流 —— 不同用户按比例分到 A/B，
        // 同一用户始终落同一变体）；headless（无 ICurrentUser）时退化为按 Agent 稳定分配。
        // 使用稳定哈希（SHA256）而非 HashCode.Combine —— 后者每进程随机化哈希种子，会导致
        // 同一用户在服务重启后切换变体，破坏 A/B 实验的一致性。
        var routingKey = _currentUser?.Id?.ToString() ?? agent.Id.ToString();
        var roll = StableRoll(routingKey, agent.Id, abTestConfig.VersionA, abTestConfig.VersionB);
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
            SelectedVariant = selectedVariant,
            // The variant's resources must come from the SNAPSHOT, not the live junction - the
            // resolver consumes this projection directly (resource columns were dropped from Agent,
            // so they can't ride on routedAgent). Null snapshot lists map to empty to honor the
            // projection's non-null contract (legacy/pre-grant snapshots → no resources, correct).
            SnapshotGrants = BuildSnapshotGrants(snapshot)
        };
    }

    /// <summary>
    /// 把版本快照的资源列折叠成 <see cref="AgentGrantsProjection"/>（null 列 → 空列表）。
    /// Projects a version snapshot's resource lists into an AgentGrantsProjection (null → empty list).
    /// </summary>
    private static AgentGrantsProjection BuildSnapshotGrants(AgentConfigSnapshot snapshot) =>
        new()
        {
            ToolGroups = snapshot.ToolGroups ?? new List<string>(),
            ToolNames = snapshot.ToolNames ?? new List<string>(),
            SkillSlugs = snapshot.SkillSlugs ?? new List<string>(),
            KnowledgeBaseIds = snapshot.KnowledgeBaseIds ?? new List<Guid>()
        };

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
            // Falls back to original.Persona for snapshots taken before the
            // inline Persona field existed (legacy AgentConfigSnapshot rows).
            Persona = snapshot.Persona ?? original.Persona
        };
    }

    /// <summary>
    /// 稳定的 0-99 流量分配值。基于 SHA256 而非 <see cref="HashCode.Combine(object?,object?,object?)"/> ——
    /// 后者使用每进程随机化的哈希种子，跨进程/重启不稳定，会让同一路由键在重启后切换 A/B 变体。
    /// </summary>
    private static int StableRoll(string routingKey, Guid agentId, int versionA, int versionB)
    {
        var input = $"{routingKey}|{agentId}|{versionA}|{versionB}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var value = BitConverter.ToUInt32(hash, 0);
        return (int)(value % 100);
    }
}
