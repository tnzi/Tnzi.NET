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
public abstract class EntityBase<TKey> : IEntity<TKey>, IHasDomainEvents
{
    public virtual TKey Id { get; set; } = default!;

    // 领域事件集合（惰性分配，不影响不使用事件的实体）
    private List<IEvent>? _domainEvents;

    /// <inheritdoc />
    public IReadOnlyCollection<IEvent> DomainEvents =>
        _domainEvents?.AsReadOnly() ?? (IReadOnlyCollection<IEvent>)Array.Empty<IEvent>();

    /// <inheritdoc />
    public void AddDomainEvent(IEvent @event) => (_domainEvents ??= new()).Add(Check.NotNull(@event));

    /// <inheritdoc />
    public void ClearDomainEvents() => _domainEvents?.Clear();

    public override string ToString()
    {
        return $"[{GetType().Name} {Id}]";
    }

    public object[] GetKeys()
    {
        return new object[] { Id! };
    }
}

