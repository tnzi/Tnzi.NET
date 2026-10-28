namespace Tnzi.Storage.Services;

/// <summary>
/// <c>[FileField]</c> 只能长在主键是单个 <see cref="Guid"/>（属性名 <c>Id</c>）的实体上。
/// </summary>
/// <remarks>
/// <para>
/// 引用表 <see cref="FileReference.EntityId"/> 是 Guid 列。变更追踪器按字符串收集 Guid / long / int / string 主键，
/// 处理器却只存得下能解析成 Guid 的那一种。此前解析不成就记一条 Warning 跳过：没有引用行、计数不加，
/// 实体保存成功；它引用的临时上传计数仍是 0，开着清理时过了保留期被当成孤儿删掉，而那条记录还指着它。
/// </para>
/// <para>
/// 这里把那种声明在启动时就点名拒绝（<see cref="StorageModule"/>），处理器在运行时遇到漏网的同样拒绝保存。
/// 主键不是 Guid 的实体要挂文件，用正式上传（<c>isTemporary = false</c>）：正式记录自带一个持有者，
/// 不依赖引用追踪，由业务代码在不再需要时显式删除。
/// </para>
/// </remarks>
internal static class FileFieldHostKeyGuard
{
    /// <summary>
    /// 声明了 <c>[FileField]</c>、而主键不是 Guid 的实体，形如 <c>Product (key Int64)</c>；没有 <c>Id</c> 属性的记作 <c>(key none)</c>。
    /// </summary>
    public static IReadOnlyList<string> FindUnsupportedHosts(IEnumerable<Type> entityTypes)
    {
        Check.NotNull(entityTypes);

        var unsupported = new List<string>();
        foreach (var type in entityTypes)
        {
            if (!type.GetProperties().Any(p => p.GetCustomAttribute<FileFieldAttribute>() != null))
                continue;

            var keyType = type.GetProperties().FirstOrDefault(p => p.Name == "Id")?.PropertyType;
            if (keyType == typeof(Guid) || keyType == typeof(Guid?))
                continue;

            unsupported.Add($"{type.Name} (key {keyType?.Name ?? "none"})");
        }

        return unsupported;
    }

    /// <summary>启动拒绝时的说明：点名每个实体，并说出两条出路。</summary>
    public static string DescribeUnsupportedHosts(IReadOnlyCollection<string> hosts)
        => $"[FileField] is only tracked on entities whose key is a single Guid Id, because file references store the entity id as a Guid. "
            + $"These entities declare it on another key: {string.Join(", ", hosts)}. "
            + "Their file fields would not be tracked, and a temporary upload they point at would be removed as an orphan while still in use. "
            + "Give these entities a Guid key, or remove [FileField] and upload their files as permanent (isTemporary = false) so the file lifetime does not depend on reference tracking.";

    /// <summary>处理器在运行时遇到一个漏网的这种实体时的说明。</summary>
    public static string DescribeUnsupportedReference(string entityType, string entityId, string fieldName)
        => $"Cannot track [FileField] '{entityType}.{fieldName}': the entity id '{entityId}' is not a Guid, and file references only store Guid entity ids. "
            + "The save is refused so the file is not later removed as an orphan while this record still points at it. "
            + "Give the entity a Guid key, or remove [FileField] and upload the file as permanent (isTemporary = false).";
}
