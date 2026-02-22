
namespace Tnzi.Data.IdGenerators;

/// <summary>
/// 实体 ID 生成器接口
/// 用于在 DbContext 保存时自动生成实体 ID
/// </summary>
public interface IEntityIdGenerator
{
    /// <summary>
    /// 生成指定类型的 ID
    /// </summary>
    /// <param name="entityType">实体类型</param>
    /// <param name="idType">ID 类型</param>
    /// <returns>生成的 ID</returns>
    object? GenerateId(Type entityType, Type idType);
}

/// <summary>
/// 默认实体 ID 生成器
/// 使用 SequentialGuid 和 Snowflake 算法
/// </summary>
public class DefaultEntityIdGenerator : IEntityIdGenerator
{
    private readonly Microsoft.EntityFrameworkCore.DbContext _dbContext;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SequentialGuid.SequentialGuidType> _guidTypeCache = new();

    public DefaultEntityIdGenerator(Microsoft.EntityFrameworkCore.DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public object? GenerateId(Type entityType, Type idType)
    {
        return idType switch
        {
            Type t when t == typeof(Guid) || t == typeof(Guid?) => SequentialGuid.NewGuid(GetSequentialGuidTypeForDatabase()),
            Type t when t == typeof(long) => IdHelper.NextId(),
            _ => null
        };
    }

    private SequentialGuid.SequentialGuidType GetSequentialGuidTypeForDatabase()
    {
        string? providerName = null;
        try { providerName = _dbContext.Database.ProviderName; }
        catch
        {
            // 获取 ProviderName 可能失败（例如数据库未初始化），使用默认值
        }

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
}