
namespace Tnzi.EFCore.Internal;

/// <summary>
/// ID 自动生成辅助类
/// 负责实体 ID 的自动生成（Sequential GUID / Snowflake）
/// </summary>
internal static class IdGenerationHelper
{
    private static readonly ConcurrentDictionary<Type, System.Reflection.PropertyInfo?> _idPropertyCache = new();
    private static readonly ConcurrentDictionary<Type, string?> _dbProviderCache = new();

    /// <summary>
    /// 为新增实体自动生成 ID
    /// </summary>
    public static void ApplyAutoId(DbContext dbContext, EntityEntry entry)
    {
        if (entry.Entity is not IEntity) return;

        var entityType = entry.Entity.GetType();
        var idProperty = _idPropertyCache.GetOrAdd(entityType, type =>
            type.GetProperty("Id", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance));

        if (idProperty == null || !idProperty.CanWrite) return;

        var currentId = idProperty.GetValue(entry.Entity);
        if (IsDefaultValue(currentId, idProperty.PropertyType))
        {
            var newId = GenerateId(dbContext, entityType, idProperty.PropertyType);
            if (newId != null) idProperty.SetValue(entry.Entity, newId);
        }
    }

    /// <summary>
    /// 按与 SaveChanges 同一套规则生成一个 Id：注册了 <see cref="IEntityIdGenerator"/> 则交给它，
    /// 否则 Guid 走 Sequential GUID（按 provider 选排列）、long 走 Snowflake；其它类型返回 null。
    /// 绕过变更跟踪器的写入路径（Dapper 批量插入）也从这里取 Id，两条路径的 Id 形态一致。
    /// </summary>
    public static object? GenerateId(DbContext dbContext, Type entityType, Type idType)
    {
        var idGenerator = GetEntityIdGenerator(dbContext);
        if (idGenerator != null)
        {
            return idGenerator.GenerateId(entityType, idType);
        }

        var guidType = GetSequentialGuidTypeForDatabase(dbContext);
        return idType switch
        {
            Type t when t == typeof(Guid) || t == typeof(Guid?) => SequentialGuid.NewGuid(guidType),
            Type t when t == typeof(long) => IdHelper.NextId(),
            _ => null
        };
    }

    /// <summary>不经 <see cref="IEntityIdGenerator"/>、按 provider 选排列的顺序 Guid。</summary>
    public static Guid NewSequentialGuid(DbContext dbContext)
        => SequentialGuid.NewGuid(GetSequentialGuidTypeForDatabase(dbContext));

    private static SequentialGuid.SequentialGuidType GetSequentialGuidTypeForDatabase(DbContext dbContext)
        => GetSequentialGuidType(GetCachedDatabaseProvider(dbContext));

    /// <summary>
    /// EF provider → 该库比较 Guid 列的规则对应的顺序 Guid 排列。<see cref="DefaultEntityIdGenerator"/> 也走这里，两张表不再各写一份。
    /// </summary>
    /// <remarks>
    /// PostgreSQL 归文本顺序而不是字节顺序：Npgsql 按 RFC 大端字节序写 <c>uuid</c>、库按同一顺序比较，
    /// 那正是 <see cref="Guid.ToString()"/> 的顺序；<see cref="Guid.ToByteArray()"/> 的前三段是小端，按它排的时间戳在库里是乱的。
    /// MySQL（Pomelo / MySql.EntityFrameworkCore 默认 <c>char(36)</c>）与 SQLite（<c>TEXT</c>）存的就是文本。
    /// 认不出的 provider 用默认排列（SQL Server）。
    /// </remarks>
    internal static SequentialGuid.SequentialGuidType GetSequentialGuidType(string? providerName) => providerName switch
    {
        "Microsoft.EntityFrameworkCore.SqlServer" => SequentialGuid.SequentialGuidType.SequentialAtEnd,
        "Npgsql.EntityFrameworkCore.PostgreSQL"
            or "Pomelo.EntityFrameworkCore.MySql"
            or "MySql.EntityFrameworkCore"
            or "Microsoft.EntityFrameworkCore.Sqlite" => SequentialGuid.SequentialGuidType.SequentialAsString,
        _ => SequentialGuid.SequentialGuidType.SequentialAtEnd
    };

    private static string? GetCachedDatabaseProvider(DbContext dbContext)
    {
        var contextType = dbContext.GetType();
        return _dbProviderCache.GetOrAdd(contextType, type =>
        {
            try
            {
                return dbContext.Database.ProviderName;
            }
            catch
            {
                return null;
            }
        });
    }

    private static IEntityIdGenerator? GetEntityIdGenerator(DbContext dbContext)
    {
        var serviceProvider = DbContextServiceResolver.GetServiceProvider(dbContext);
        if (serviceProvider != null)
        {
            try
            {
                return serviceProvider.GetService<IEntityIdGenerator>();
            }
            catch
            {
                // 服务可能未注册，这是预期情况，继续尝试其他方式
            }
        }

        try
        {
            return dbContext.Database.GetService<IEntityIdGenerator>();
        }
        catch
        {
            // 服务可能未注册，返回 null 表示未找到
        }

        return null;
    }

    public static bool IsDefaultValue(object? value, Type type)
    {
        if (value == null) return true;
        if (!type.IsValueType) return false;
        return Equals(value, Activator.CreateInstance(type));
    }
}
