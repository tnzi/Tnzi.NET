namespace Tnzi.Domain.Entities;

/// <summary>
/// 支持领域事件收集的实体接口
/// 实体可在业务方法中添加领域事件，SaveChanges 后自动发布
/// </summary>
[ExperimentalApi(Reason = "Domain event collection mechanism - API may change")]
public interface IHasDomainEvents
{
    /// <summary>
    /// 已收集的领域事件（只读）
    /// </summary>
    IReadOnlyCollection<IEvent> DomainEvents { get; }

    /// <summary>
    /// 添加领域事件
    /// </summary>
    void AddDomainEvent(IEvent @event);

    /// <summary>
    /// 清除所有领域事件
    /// </summary>
    void ClearDomainEvents();
}
