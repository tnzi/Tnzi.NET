namespace Tnzi.AI.Services;

/// <summary>
/// 持久化工具权限规则管理服务实现。
/// </summary>
public class ToolPermissionRuleService : ApplicationService, IToolPermissionRuleService
{
    private readonly IRepository<ToolPermissionRuleEntity, Guid> _repository;
    private readonly IToolPermissionRuleStore _ruleStore;
    private readonly IToolPermissionEvaluator _evaluator;

    public ToolPermissionRuleService(
        IServiceProvider serviceProvider,
        IRepository<ToolPermissionRuleEntity, Guid> repository,
        IToolPermissionRuleStore ruleStore,
        IToolPermissionEvaluator evaluator)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _ruleStore = Check.NotNull(ruleStore);
        _evaluator = Check.NotNull(evaluator);
    }

    /// <summary>
    /// 把本次写入刷进当前工作单元，再用<b>同一个作用域</b>的 store 重读全量规则并推给评估器。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 不能让评估器自己刷新：它会另开作用域、另开 DbContext（另一条连接）重查。宿主开着
    /// <c>EnableGlobalUnitOfWork</c> 时本次写入还停在变更跟踪器里，flush 之后也只是本连接上的未提交状态 ——
    /// 另一条连接一律看不见。于是刚建的 Deny 规则在下一次 CRUD 或进程重启前完全不生效，
    /// 而接口返回的是 200 成功：管理端显示的规则集与运行时执行的规则集是两份东西。
    /// </para>
    /// <para>
    /// flush 走工作单元而不是绕开事务直接落库：后者会让一次回滚掉的请求留下一条撤不回来的权限规则。
    /// </para>
    /// </remarks>
    private async Task RefreshEvaluatorAsync(CancellationToken cancellationToken)
    {
        await _repository.SaveChangesAsync(cancellationToken);
        var rules = await _ruleStore.GetRulesAsync(cancellationToken);
        await _evaluator.RefreshRulesAsync(rules);
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
        await RefreshEvaluatorAsync(cancellationToken);

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
        await RefreshEvaluatorAsync(cancellationToken);

        return Ok(entity.MapTo<PersistedPermissionRuleDto>());
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetAsync(id, cancellationToken);
        if (entity == null)
            return Fail("Permission rule not found.", 404, ErrorCodes.PermissionRuleNotFound);

        await _repository.DeleteAsync(entity, cancellationToken);
        await RefreshEvaluatorAsync(cancellationToken);

        return Ok();
    }
}
