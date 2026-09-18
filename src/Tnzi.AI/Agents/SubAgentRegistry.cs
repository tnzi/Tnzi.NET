namespace Tnzi.AI.Agents;

/// <summary>
/// 子 Agent 类型注册表实现 - 内置 3 种标准类型 + 运行时扩展
/// </summary>
/// <remarks>
/// 内置类型：
/// - general-purpose: 通用子 Agent，datetime/text/websearch/sandbox 工具集（排除 task/clarification/artifact），50 轮次
/// - bash: 沙箱专用子 Agent，仅 sandbox 工具，30 轮次
/// - researcher: 研究子 Agent，websearch 工具，30 轮次
/// </remarks>
public class SubAgentRegistry : ISubAgentRegistry
{
    /// <summary>全局类型：内置 + 代码经 Register() 注册的，不随任何租户的重载消失</summary>
    private readonly ConcurrentDictionary<string, SubAgentTypeDefinition> _globalTypes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 数据库来源的类型按租户桶存放。此前是一张单表：任一租户管理员 CRUD 后 Clear() 整表再按自己的
    /// 租户过滤重载，其它租户的类型随之消失、本租户的 Instructions / ToolGroups 对全体可见。
    /// </summary>
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, SubAgentTypeDefinition>> _tenantTypes = new();

    public SubAgentRegistry()
    {
        RegisterBuiltInTypes();
    }

    /// <inheritdoc />
    public IReadOnlyList<SubAgentTypeDefinition> GetAll()
        => GetAllForTenant(SubAgentTenantKey.Default);

    /// <inheritdoc />
    public SubAgentTypeDefinition? Get(string name)
        => GetForTenant(name, SubAgentTenantKey.Default);

    /// <inheritdoc />
    public IReadOnlyList<SubAgentTypeDefinition> GetAllForTenant(string tenantKey)
    {
        Check.NotNullOrWhiteSpace(tenantKey);

        var merged = new Dictionary<string, SubAgentTypeDefinition>(_globalTypes, StringComparer.OrdinalIgnoreCase);
        if (_tenantTypes.TryGetValue(tenantKey, out var bucket))
        {
            foreach (var (name, definition) in bucket)
            {
                merged[name] = definition;
            }
        }

        return merged.Values.ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public SubAgentTypeDefinition? GetForTenant(string name, string tenantKey)
    {
        Check.NotNullOrWhiteSpace(name);
        Check.NotNullOrWhiteSpace(tenantKey);

        if (_tenantTypes.TryGetValue(tenantKey, out var bucket) && bucket.TryGetValue(name, out var tenantDefinition))
        {
            return tenantDefinition;
        }

        return _globalTypes.GetValueOrDefault(name);
    }

    /// <inheritdoc />
    public void Register(SubAgentTypeDefinition definition)
    {
        Check.NotNull(definition);
        Check.NotNullOrWhiteSpace(definition.Name);
        _globalTypes[definition.Name] = definition;
    }

    /// <inheritdoc />
    public bool Unregister(string name)
    {
        Check.NotNullOrWhiteSpace(name);
        return _globalTypes.TryRemove(name, out _);
    }

    /// <inheritdoc />
    public Task LoadFromStoreAsync(IRepository<SubAgentType, Guid> repository, CancellationToken cancellationToken = default)
        => LoadTenantFromStoreAsync(repository, SubAgentTenantKey.Default, cancellationToken);

    /// <inheritdoc />
    public async Task LoadTenantFromStoreAsync(IRepository<SubAgentType, Guid> repository, string tenantKey, CancellationToken cancellationToken = default)
    {
        Check.NotNull(repository);
        Check.NotNullOrWhiteSpace(tenantKey);

        var entities = await repository.AsQueryable()
            .Where(e => e.IsEnabled)
            .ToListAsync(cancellationToken);

        // 整桶替换（确保禁用 / 删除的类型被移除），只动这一个租户的桶
        _tenantTypes[tenantKey] = ToBucket(entities);
    }

    /// <inheritdoc />
    public async Task LoadAllTenantsFromStoreAsync(IRepository<SubAgentType, Guid> repository, CancellationToken cancellationToken = default)
    {
        Check.NotNull(repository);

        var entities = await repository.AsQueryable()
            .Where(e => e.IsEnabled)
            .ToListAsync(cancellationToken);

        var buckets = entities
            .GroupBy(e => SubAgentTenantKey.From(e.TenantId))
            .ToDictionary(g => g.Key, g => ToBucket(g));

        // 启动期整体重建：读到几个租户就装几个桶；没读到的桶（该租户没有启用行）一并清掉
        foreach (var staleKey in _tenantTypes.Keys.Where(k => !buckets.ContainsKey(k)).ToList())
        {
            _tenantTypes.TryRemove(staleKey, out _);
        }

        foreach (var (tenantKey, bucket) in buckets)
        {
            _tenantTypes[tenantKey] = bucket;
        }
    }

    private static ConcurrentDictionary<string, SubAgentTypeDefinition> ToBucket(IEnumerable<SubAgentType> entities)
    {
        var bucket = new ConcurrentDictionary<string, SubAgentTypeDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in entities)
        {
            bucket[entity.Name] = new SubAgentTypeDefinition(
                Name: entity.Name,
                Description: entity.Description,
                ToolGroups: entity.ToolGroups ?? [],
                ExcludedToolGroups: entity.ExcludedToolGroups ?? [],
                MaxTurns: entity.MaxTurns,
                Instructions: entity.Instructions,
                DefaultModel: entity.DefaultModel,
                DefaultApprovalMode: entity.DefaultApprovalMode,
                CapabilityTags: entity.CapabilityTags ?? []);
        }

        return bucket;
    }

    /// <summary>
    /// 内置类型的工具组名必须是注册表里真实存在的组名（<c>[AIToolGroup]</c> 声明的那个字符串）：
    /// 未知组名在 <c>IToolRegistry.GetToolsByGroups</c> 里静默解析为零个工具，不报错。
    /// 此前写的是 <c>default</c> / <c>file</c> / <c>code</c> / <c>web-search</c> / <c>present-files</c>
    /// 这些从未存在过的名字，general-purpose 实际只剩 sandbox、researcher 一个工具都没有。
    /// </summary>
    private void RegisterBuiltInTypes()
    {
        Register(new SubAgentTypeDefinition(
            Name: "general-purpose",
            Description: "General-purpose sub-agent with full toolset minus orchestration tools",
            ToolGroups: ["datetime", "text", "websearch", "sandbox"],
            ExcludedToolGroups: ["task", "clarification", "artifact"],
            MaxTurns: 50,
            Instructions: "You are a general-purpose assistant. Complete the delegated task thoroughly.",
            DefaultApprovalMode: ToolApprovalMode.Specific,
            CapabilityTags: ["general", "analysis", "implementation"]));

        Register(new SubAgentTypeDefinition(
            Name: "bash",
            Description: "Sandbox-only sub-agent for command execution and file operations",
            ToolGroups: ["sandbox"],
            ExcludedToolGroups: [],
            MaxTurns: 30,
            Instructions: "You are a command-line assistant. Execute commands in the sandbox to complete the task.",
            DefaultApprovalMode: ToolApprovalMode.Specific,
            CapabilityTags: ["shell", "filesystem", "sandbox"]));

        Register(new SubAgentTypeDefinition(
            Name: "researcher",
            Description: "Research sub-agent with web search",
            ToolGroups: ["websearch"],
            ExcludedToolGroups: [],
            MaxTurns: 30,
            Instructions: "You are a research assistant. Search the web to gather information for the task.",
            DefaultApprovalMode: ToolApprovalMode.NeverRequire,
            CapabilityTags: ["research", "web"]));
    }
}
