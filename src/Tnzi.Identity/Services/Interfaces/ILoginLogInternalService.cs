namespace Tnzi.Identity.Services;

/// <summary>
/// 登录日志内部服务接口
/// 提供内部方法（返回原始实体/原始类型），供 EventHandler 和其他内部服务使用
/// </summary>
public interface ILoginLogInternalService
{
    /// <summary>
    /// 记录登录日志
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="userName">用户名</param>
    /// <param name="ipAddress">IP地址</param>
    /// <param name="userAgent">用户代理</param>
    /// <param name="status">登录状态</param>
    /// <param name="failureReason">失败原因</param>
    /// <returns>登录日志ID</returns>
    Task<Guid> LogAsync(Guid? userId, string? userName, string? ipAddress, string? userAgent, LoginStatus status, string? failureReason = null);

    /// <summary>
    /// 获取用户的登录日志（返回原始实体）
    /// </summary>
    Task<IPagedList<LoginLog>> GetPagedLogsAsync(Guid userId, int pageIndex = 1, int pageSize = 20);

    /// <summary>
    /// 获取最近的登录日志（支持用户特定或全局）
    /// </summary>
    Task<IEnumerable<LoginLog>> GetRecentLogsAsync(Guid? userId = null, int count = 10);

    /// <summary>
    /// 按IP地址查询登录日志
    /// </summary>
    Task<IPagedList<LoginLog>> GetLogsByIpAsync(string ipAddress, int pageIndex = 1, int pageSize = 20);

    /// <summary>
    /// 按时间范围查询登录日志
    /// </summary>
    Task<IPagedList<LoginLog>> GetLogsByDateRangeAsync(DateTime startDate, DateTime endDate, int pageIndex = 1, int pageSize = 20);

    /// <summary>
    /// 查询登录日志（支持多条件）
    /// </summary>
    Task<IPagedList<LoginLog>> QueryLogsAsync(LoginLogQueryDto query);

    /// <summary>
    /// 获取登录统计
    /// </summary>
    Task<LoginStatisticsDto> GetLoginStatisticsAsync(Guid? userId = null, DateTime? startDate = null, DateTime? endDate = null);
}
