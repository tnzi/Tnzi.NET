namespace Tnzi.Audit.Services;

/// <summary>
/// 数据库审计存储实现
/// </summary>
public class DatabaseAuditStore : IAuditStore
{
    private readonly IRepository<AuditOperation, Guid> _operationRepository;
    private readonly IRepository<AuditEntityEntry, Guid> _entityEntryRepository;

    /// <summary>
    /// 初始化一个<see cref="DatabaseAuditStore"/>类型的新实例
    /// </summary>
    public DatabaseAuditStore(
        IRepository<AuditOperation, Guid> operationRepository,
        IRepository<AuditEntityEntry, Guid> entityEntryRepository)
    {
        _operationRepository = Check.NotNull(operationRepository);
        _entityEntryRepository = Check.NotNull(entityEntryRepository);
    }

    /// <summary>
    /// 保存审计操作
    /// </summary>
    public async Task SaveOperationAsync(AuditOperation operation)
    {
        await _operationRepository.InsertAsync(operation);
    }

    /// <summary>
    /// 批量保存审计操作
    /// </summary>
    public async Task SaveOperationBatchAsync(IEnumerable<AuditOperation> operations)
    {
        var list = operations.ToList();
        if (list.Count > 0)
        {
            await _operationRepository.InsertManyAsync(list);
        }
    }

    /// <summary>
    /// 保存实体变更记录
    /// </summary>
    public async Task SaveEntityEntriesAsync(IEnumerable<AuditEntityEntry> entries)
    {
        var list = entries.ToList();
        if (list.Count > 0)
        {
            await _entityEntryRepository.InsertManyAsync(list);
        }
    }

    /// <summary>
    /// 删除过期审计数据
    /// </summary>
    public async Task<int> DeleteExpiredAsync(int days)
    {
        var expireDate = DateTime.UtcNow.AddDays(-days);

        var count = await _operationRepository.CountAsync(o => o.CreationTime < expireDate);
        if (count > 0)
        {
            // 级联删除会自动清理关联的 EntityEntry 和 PropertyEntry
            await _operationRepository.DeleteAsync(o => o.CreationTime < expireDate);
        }

        return count;
    }
}
