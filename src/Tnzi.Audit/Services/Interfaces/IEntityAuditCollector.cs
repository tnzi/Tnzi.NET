namespace Tnzi.Audit.Services;

/// <summary>
/// 实体级审计采集器（per-request scoped）。
/// <para>
/// <see cref="Interceptors.EntityAuditSaveChangesInterceptor"/> 在每次 SaveChanges
/// 成功后把采集到的实体变更累积进来；<see cref="Middleware.AuditMiddleware"/> 在请求
/// 收尾时 <see cref="Drain"/> 取走全部条目挂到当前请求的 AuditOperation.EntityEntries，
/// 随操作审计管道级联入库。
/// </para>
/// <para>
/// 非 HTTP 场景（后台任务等）没有 AuditOperation 承载，拦截器侧已跳过采集；
/// 即使有残留条目，也会随 scope 释放一起丢弃。
/// </para>
/// </summary>
public interface IEntityAuditCollector
{
    /// <summary>当前是否已有采集到的实体变更</summary>
    bool HasEntries { get; }

    /// <summary>追加一批实体变更（由 EF SaveChanges 拦截器在保存成功后调用）</summary>
    void AddRange(IEnumerable<AuditEntityEntry> entries);

    /// <summary>取走全部已采集的实体变更并清空（由 AuditMiddleware 在请求收尾时调用）</summary>
    IReadOnlyList<AuditEntityEntry> Drain();
}
