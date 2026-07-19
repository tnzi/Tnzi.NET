namespace Tnzi.Audit.Services;

/// <summary>
/// 实体级审计采集器默认实现（注册为 Scoped，一个请求一个实例）。
/// 请求内 DbContext 的使用基本是串行的，加锁只为防御极端并发写入场景。
/// </summary>
public class EntityAuditCollector : IEntityAuditCollector
{
    private readonly object _lock = new();
    private List<AuditEntityEntry> _entries = [];

    public bool HasEntries
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count > 0;
            }
        }
    }

    public void AddRange(IEnumerable<AuditEntityEntry> entries)
    {
        Check.NotNull(entries);

        lock (_lock)
        {
            _entries.AddRange(entries);
        }
    }

    public IReadOnlyList<AuditEntityEntry> Drain()
    {
        lock (_lock)
        {
            var drained = _entries;
            _entries = [];
            return drained;
        }
    }
}
