namespace Tnzi.Exceptions;

/// <summary>
/// 实体未找到异常
/// </summary>
public class EntityNotFoundException : ResourceNotFoundException
{
    /// <summary>
    /// 实体类型名称
    /// </summary>
    public string EntityTypeName { get; }

    /// <summary>
    /// 实体ID
    /// </summary>
    public object? EntityId { get; }

    /// <summary>
    /// 初始化一个<see cref="EntityNotFoundException"/>类型的新实例
    /// </summary>
    /// <param name="entityTypeName">实体类型名称</param>
    /// <param name="entityId">实体ID</param>
    public EntityNotFoundException(string entityTypeName, object? entityId = null)
        : base($"Entity '{entityTypeName}'", entityId)
    {
        EntityTypeName = entityTypeName;
        EntityId = entityId;
    }

    /// <summary>
    /// 初始化一个<see cref="EntityNotFoundException"/>类型的新实例
    /// </summary>
    /// <typeparam name="TEntity">实体类型</typeparam>
    /// <param name="entityId">实体ID</param>
    public EntityNotFoundException<TEntity> ForEntity<TEntity>(object? entityId = null)
    {
        return new EntityNotFoundException<TEntity>(entityId);
    }
}

/// <summary>
/// 实体未找到异常（泛型版本）
/// </summary>
/// <typeparam name="TEntity">实体类型</typeparam>
public class EntityNotFoundException<TEntity> : EntityNotFoundException
{
    /// <summary>
    /// 初始化一个<see cref="EntityNotFoundException{TEntity}"/>类型的新实例
    /// </summary>
    /// <param name="entityId">实体ID</param>
    public EntityNotFoundException(object? entityId = null)
        : base(typeof(TEntity).Name, entityId)
    {
    }
}

