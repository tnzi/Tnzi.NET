namespace Tnzi.AI.Services;

/// <summary>
/// 持久化工具权限规则的管理端服务（CRUD）。
/// </summary>
/// <remarks>
/// 与 <see cref="Tnzi.AI.Security.IToolPermissionRuleStore"/> 的分工同 Audit 模块的
/// <c>IAuditStore</c> / <c>IAuditOperationService</c>：Store 是**运行时判定**要用的只读取数口
/// （只返回已启用的规则），本服务是**管理面**的读写口（含未启用规则、返回 <c>Result&lt;T&gt;</c>）。
///
/// 每次写入后都会调 <c>IToolPermissionEvaluator.RefreshRulesAsync()</c> —— 评估器持有规则快照，
/// 不刷新的话管理端改完要等下次进程重启才生效。
/// </remarks>
public interface IToolPermissionRuleService
{
    /// <summary>获取全部持久化规则（按优先级降序、Scope 升序）</summary>
    Task<Result<List<PersistedPermissionRuleDto>>> GetListAsync(CancellationToken cancellationToken = default);

    /// <summary>创建规则</summary>
    Task<Result<PersistedPermissionRuleDto>> CreateAsync(CreatePersistedPermissionRuleDto input, CancellationToken cancellationToken = default);

    /// <summary>更新规则</summary>
    Task<Result<PersistedPermissionRuleDto>> UpdateAsync(Guid id, CreatePersistedPermissionRuleDto input, CancellationToken cancellationToken = default);

    /// <summary>删除规则</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
