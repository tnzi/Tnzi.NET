namespace Tnzi.Identity.Services;

/// <summary>
/// 登录日志发送者
/// </summary>
public interface ILoginLogSender
{
    /// <summary>
    /// 发送登录日志到后台队列
    /// </summary>
    Task SendAsync(LoginLog log);
}
