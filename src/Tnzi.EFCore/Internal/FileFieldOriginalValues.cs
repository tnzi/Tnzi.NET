
namespace Tnzi.EFCore.Internal;

/// <summary>
/// 脱离跟踪的实体被附加回来写库时，把 <c>[FileField]</c> 属性的原值换成库里的值。
/// </summary>
/// <remarks>
/// <para>
/// 仓储读方法默认 <c>AsNoTracking</c>，「读出来 → 改 → <c>UpdateAsync</c>」写回的是脱离跟踪的实例；
/// <c>Attach</c> 以它<b>当前</b>的值作原值快照，于是原值即新值。文件引用追踪
/// （<see cref="FileTracking.FileReferenceChangeTracker"/>）按原值与现值求差，差集为空：
/// 新文件不建引用（开了孤儿清理就会被物理删除），旧文件的引用永不释放（凭残留引用仍能读旧文件）。
/// </para>
/// <para>
/// ★ 只回填文件字段的原值，不整体 <c>OriginalValues.SetValues</c>：那会把并发令牌的原值换成库里最新的那枚，
/// 过期的脱离跟踪写入就绕过了乐观并发检测。
/// </para>
/// <para>
/// 代价是一次按主键的查询，只发生在「实体类型带文件字段、且实例是脱离跟踪附加进来」时；
/// 其余写入零额外往返。库里查不到这一行（已删、或被过滤器隐藏）时保持附加时的快照不动。
/// </para>
/// </remarks>
internal static class FileFieldOriginalValues
{
    private static readonly ConcurrentDictionary<Type, string[]> FileFieldNames = new();

    /// <summary>
    /// 对处于 <see cref="EntityState.Modified"/> 的条目回读文件字段原值；其它状态与无文件字段的类型直接返回。
    /// </summary>
    public static async Task RestoreAsync(EntityEntry entry, CancellationToken cancellationToken)
    {
        if (entry.State != EntityState.Modified)
        {
            return;
        }

        var names = FileFieldNames.GetOrAdd(entry.Metadata.ClrType, type => type.GetProperties()
            .Where(p => p.GetCustomAttribute<FileFieldAttribute>() != null)
            .Select(p => p.Name)
            .ToArray());
        if (names.Length == 0)
        {
            return;
        }

        var databaseValues = await entry.GetDatabaseValuesAsync(cancellationToken);
        if (databaseValues == null)
        {
            return;
        }

        foreach (var name in names)
        {
            var property = entry.Metadata.FindProperty(name);
            if (property != null)
            {
                entry.Property(name).OriginalValue = databaseValues[property];
            }
        }
    }
}
