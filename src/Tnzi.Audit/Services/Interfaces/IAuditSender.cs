namespace Tnzi.Audit.Services;

/// <summary>
/// 审计日志发送者接口，用于异步处理审计日志
/// </summary>
public interface IAuditSender
{
    /// <summary>
    /// 发送审计操作到后台队列
    /// </summary>
    /// <param name="operation">审计操作</param>
    Task SendAsync(AuditOperation operation);
}
