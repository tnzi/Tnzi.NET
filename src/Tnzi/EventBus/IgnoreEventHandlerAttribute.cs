namespace Tnzi.EventBus;

/// <summary>
/// 忽略事件处理器自动注册特性
/// 用于标记不应自动注册的事件处理器类型
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class IgnoreEventHandlerAttribute : Attribute
{
}

