namespace Tnzi.EventBus;

/// <summary>
/// 事件处理器执行顺序特性
/// 数值越小越先执行，默认为0
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class EventHandlerOrderAttribute : Attribute
{
    /// <summary>
    /// 执行顺序（数值越小越先执行）
    /// </summary>
    public int Order { get; }
    
    /// <summary>
    /// 初始化事件处理器顺序
    /// </summary>
    /// <param name="order">执行顺序</param>
    public EventHandlerOrderAttribute(int order) => Order = order;
}

