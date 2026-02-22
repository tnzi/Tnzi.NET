namespace Tnzi.Audit.Services;

/// <summary>
/// 审计存储接口
/// </summary>
public interface IAuditStore
{
    /// <summary>
    /// 保存审计操作
    /// </summary>
    /// <param name="operation">审计操作</param>
    Task SaveOperationAsync(AuditOperation operation);

    /// <summary>
    /// 批量保存审计操作
    /// </summary>
    /// <param name="operations">审计操作列表</param>
    Task SaveOperationBatchAsync(IEnumerable<AuditOperation> operations);

    /// <summary>
    /// 保存实体变更记录
    /// </summary>
    /// <param name="entries">实体变更记录列表</param>
    Task SaveEntityEntriesAsync(IEnumerable<AuditEntityEntry> entries);

    /// <summary>
    /// 删除过期审计数据
    /// </summary>
    /// <param name="days">保留天数</param>
    /// <returns>删除的记录数</returns>
    Task<int> DeleteExpiredAsync(int days);
}
