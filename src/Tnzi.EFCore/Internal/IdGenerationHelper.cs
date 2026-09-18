
namespace Tnzi.EFCore.Internal;

/// <summary>
/// ID 自动生成辅助类
/// 负责实体 ID 的自动生成（Sequential GUID / Snowflake）
/// </summary>
internal static class IdGenerationHelper
{
    private static readonly ConcurrentDictionary<Type, System.Reflection.PropertyInfo?> _idPropertyCache = new();
    private static readonly ConcurrentDictionary<Type, string?> _dbProviderCache = new();
    private static readonly ConcurrentDictionary<string, SequentialGuid.SequentialGuidType> _guidTypeCache = new();

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

    private static SequentialGuid.SequentialGuidType GetSequentialGuidTypeForDatabase(DbContext dbContext)
    {
        string? providerName = GetCachedDatabaseProvider(dbContext);
        providerName ??= "Default";

        return _guidTypeCache.GetOrAdd(providerName, name =>
            name switch
            {
                "Microsoft.EntityFrameworkCore.SqlServer" => SequentialGuid.SequentialGuidType.SequentialAtEnd,
                "Npgsql.EntityFrameworkCore.PostgreSQL" => SequentialGuid.SequentialGuidType.SequentialAsBinary,
                "Pomelo.EntityFrameworkCore.MySql" or "MySql.EntityFrameworkCore" or "Microsoft.EntityFrameworkCore.Sqlite" => SequentialGuid.SequentialGuidType.SequentialAsString,
                _ => SequentialGuid.SequentialGuidType.SequentialAtEnd
            });
    }

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
