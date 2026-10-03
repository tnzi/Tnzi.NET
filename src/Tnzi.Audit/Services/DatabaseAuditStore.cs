namespace Tnzi.Audit.Services;

/// <summary>
/// 数据库审计存储实现
/// </summary>
public class DatabaseAuditStore : IAuditStore
{
    private readonly IRepository<AuditOperation, Guid> _operationRepository;
    private readonly IRepository<AuditEntityEntry, Guid> _entityEntryRepository;
    private readonly IOptionsMonitor<AuditOptions> _options;

    /// <summary>
    /// 初始化一个<see cref="DatabaseAuditStore"/>类型的新实例
    /// </summary>
    public DatabaseAuditStore(
        IRepository<AuditOperation, Guid> operationRepository,
        IRepository<AuditEntityEntry, Guid> entityEntryRepository,
        IOptionsMonitor<AuditOptions> options)
    {
        _operationRepository = Check.NotNull(operationRepository);
        _entityEntryRepository = Check.NotNull(entityEntryRepository);
        _options = Check.NotNull(options);
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
    /// 删除过期审计数据：按主键分批删，每批 <see cref="AuditOptions.BatchSize"/> 条操作（实体 / 属性条目由外键级联）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>分批而不是一条 DELETE。</b>保留期清理最常见的首次执行形态是「积累了一年、第一次打开」：
    /// 一条谓词 DELETE 连同级联要在一个语句里删掉几百万行，撞上命令超时整条回滚 —— 每天失败一次，表永远不变小，
    /// 而日志里只是一条每天重复的超时。每批是独立的短语句，中途失败或被取消也只丢那一批，已删的不会回滚。
    /// </para>
    /// <para>
    /// ★ <b>按 <see cref="AuditOperation.StartTime"/> 判过期，不是 <c>CreationTime</c>。</b>后者没有索引，
    /// 每一批都会退化成全表扫描；<c>StartTime</c> 有索引，且两者只差采集队列的几秒延迟 —— 对以天计的保留期没有区别。
    /// </para>
    /// </remarks>
    public async Task<int> DeleteExpiredAsync(int days, CancellationToken cancellationToken = default)
    {
        var expireDate = DateTime.UtcNow.AddDays(-days);
        var batchSize = Math.Max(1, _options.CurrentValue.BatchSize);
        var total = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ids = await _operationRepository.AsQueryable()
                .Where(o => o.StartTime < expireDate)
                .OrderBy(o => o.StartTime)
                .Select(o => o.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            if (ids.Count == 0)
            {
                break;
            }

            // 级联删除会自动清理关联的 EntityEntry 和 PropertyEntry
            await _operationRepository.DeleteAsync(o => ids.Contains(o.Id), cancellationToken);
            total += ids.Count;

            if (ids.Count < batchSize)
            {
                break;
            }
        }

        return total;
    }
}
