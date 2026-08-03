namespace Tnzi.AI.Services;

/// <summary>
/// 持久化工具权限规则管理服务实现。
/// </summary>
public class ToolPermissionRuleService : ApplicationService, IToolPermissionRuleService
{
    private readonly IRepository<ToolPermissionRuleEntity, Guid> _repository;
    private readonly IToolPermissionEvaluator _evaluator;

    public ToolPermissionRuleService(
        IServiceProvider serviceProvider,
        IRepository<ToolPermissionRuleEntity, Guid> repository,
        IToolPermissionEvaluator evaluator)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _evaluator = Check.NotNull(evaluator);
    }

    /// <inheritdoc />
    public async Task<Result<List<PersistedPermissionRuleDto>>> GetListAsync(CancellationToken cancellationToken = default)
    {
        var rules = await _repository.AsQueryable()
            .AsNoTracking()
            .OrderByDescending(e => e.Priority)
            .ThenBy(e => e.Scope)
            .ProjectTo<ToolPermissionRuleEntity, PersistedPermissionRuleDto>()
            .ToListAsync(cancellationToken);

        return Ok(rules);
    }

    /// <inheritdoc />
    public async Task<Result<PersistedPermissionRuleDto>> CreateAsync(
        CreatePersistedPermissionRuleDto input,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var entity = input.MapTo<ToolPermissionRuleEntity>();

        await _repository.InsertAsync(entity, cancellationToken);
        await _evaluator.RefreshRulesAsync();

        return Ok(entity.MapTo<PersistedPermissionRuleDto>());
    }

    /// <inheritdoc />
    public async Task<Result<PersistedPermissionRuleDto>> UpdateAsync(
        Guid id,
        CreatePersistedPermissionRuleDto input,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var entity = await _repository.GetAsync(id, cancellationToken);
        if (entity == null)
            return Fail<PersistedPermissionRuleDto>("Permission rule not found.", 404, ErrorCodes.PermissionRuleNotFound);

        // 逐字段就地赋值 —— 不要重建实体，那会丢掉 Id / 审计列 / TenantId。
        // Behavior 与 Scope 以 int 持久化。
        entity.ToolPattern = input.ToolPattern;
        entity.ToolGroup = input.ToolGroup;
        entity.CommandPrefix = input.CommandPrefix;
        entity.ServerName = input.ServerName;
        entity.PathPrefix = input.PathPrefix;
        entity.Behavior = (int)input.Behavior;
        entity.Scope = (int)input.Scope;
        entity.Priority = input.Priority;
        entity.IsDestructiveOnly = input.IsDestructiveOnly;
        entity.IsSubAgentOnly = input.IsSubAgentOnly;
        entity.Reason = input.Reason;
        entity.UserId = input.UserId;
        entity.IsEnabled = input.IsEnabled;

        await _repository.UpdateAsync(entity, cancellationToken);
        await _evaluator.RefreshRulesAsync();

        return Ok(entity.MapTo<PersistedPermissionRuleDto>());
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetAsync(id, cancellationToken);
        if (entity == null)
            return Fail("Permission rule not found.", 404, ErrorCodes.PermissionRuleNotFound);

        await _repository.DeleteAsync(entity, cancellationToken);
        await _evaluator.RefreshRulesAsync();

        return Ok();
    }
}
