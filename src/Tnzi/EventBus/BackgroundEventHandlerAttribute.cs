namespace Tnzi.EventBus;

/// <summary>
/// 标记事件处理器为后台执行
/// 后台处理器不会阻塞事件发布者，在独立 Scope 中异步执行
/// 适用于辅助性操作（日志、统计、通知等）
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class BackgroundEventHandlerAttribute : Attribute { }
