namespace Tnzi.Domain.Entities;

/// <summary>
/// 实体接口
/// </summary>
[StableApi(Since = "0.1.0")]
public interface IEntity
{
    object[] GetKeys();
}

/// <summary>
/// 泛型实体接口
/// </summary>
[StableApi(Since = "0.1.0")]
public interface IEntity<TKey> : IEntity
{
    TKey Id { get; set; }
}

/// <summary>
/// 实体基类
/// </summary>
[StableApi(Since = "0.1.0")]
public abstract class EntityBase<TKey> : IEntity<TKey>
{
    public virtual TKey Id { get; set; } = default!;

    public override string ToString()
    {
        return $"[{GetType().Name} {Id}]";
    }

    public object[] GetKeys()
    {
        return new object[] { Id! };
    }
}

