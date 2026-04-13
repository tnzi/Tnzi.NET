namespace Tnzi.AI.Security;

/// <summary>
/// 基于数据库的工具权限规则存储实现
/// </summary>
public class DatabaseToolPermissionRuleStore : IToolPermissionRuleStore
{
    private readonly IRepository<ToolPermissionRuleEntity, Guid> _repository;

    public DatabaseToolPermissionRuleStore(IRepository<ToolPermissionRuleEntity, Guid> repository)
    {
        _repository = Check.NotNull(repository);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ToolPermissionRule>> GetRulesAsync(CancellationToken cancellationToken = default)
    {
        var entities = await _repository.AsQueryable()
            .Where(e => e.IsEnabled)
            .OrderByDescending(e => e.Priority)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToRule).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ToolPermissionRule>> GetRulesByScopeAsync(ToolPermissionScope scope, CancellationToken cancellationToken = default)
    {
        var scopeInt = (int)scope;

        var entities = await _repository.AsQueryable()
            .Where(e => e.IsEnabled && e.Scope == scopeInt)
            .OrderByDescending(e => e.Priority)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToRule).ToList();
    }

    private static ToolPermissionRule MapToRule(ToolPermissionRuleEntity entity)
    {
        return new ToolPermissionRule
        {
            ToolPattern = entity.ToolPattern ?? "*",
            ToolGroup = entity.ToolGroup,
            CommandPrefix = entity.CommandPrefix,
            ServerName = entity.ServerName,
            PathPrefix = entity.PathPrefix,
            Behavior = (PermissionBehavior)entity.Behavior,
            Scope = (ToolPermissionScope)entity.Scope,
            Priority = entity.Priority,
            IsDestructiveOnly = entity.IsDestructiveOnly,
            IsSubAgentOnly = entity.IsSubAgentOnly,
            Reason = entity.Reason
        };
    }
}
