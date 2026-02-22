namespace Tnzi.System.Services;

/// <summary>
/// 访问日志发送者
/// </summary>
public interface IAccessLogSender
{
    /// <summary>
    /// 发送访问日志到后台队列
    /// </summary>
    /// <param name="log">访问日志</param>
    Task SendAsync(AccessLogDto log);
}
