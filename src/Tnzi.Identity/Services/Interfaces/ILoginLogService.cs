namespace Tnzi.Identity.Services;

/// <summary>
/// 登录日志服务接口
/// 提供面向 Controller 的公开 API 方法（返回 Result&lt;T&gt;）
/// </summary>
public interface ILoginLogService
{
    /// <summary>
    /// 根据ID获取登录日志
    /// </summary>
    /// <param name="id">日志ID</param>
    /// <returns>登录日志DTO</returns>
    Task<Result<LoginLogDto>> GetByIdAsync(Guid id);

    /// <summary>
    /// 获取登录日志列表（分页）
    /// </summary>
    /// <param name="query">查询条件</param>
    /// <returns>分页的登录日志列表</returns>
    Task<Result<IPagedList<LoginLogDto>>> GetPagedListAsync(LoginLogQueryDto query);

    /// <summary>
    /// 获取用户的登录日志
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="startDate">开始日期（可选）</param>
    /// <param name="endDate">结束日期（可选）</param>
    /// <param name="isSuccess">是否成功（可选）</param>
    /// <returns>登录日志列表</returns>
    Task<Result<IEnumerable<LoginLogDto>>> GetUserLoginLogsAsync(Guid userId, DateTime? startDate = null, DateTime? endDate = null, bool? isSuccess = null);

    /// <summary>
    /// 获取登录统计信息
    /// </summary>
    /// <param name="startDate">开始日期（可选）</param>
    /// <param name="endDate">结束日期（可选）</param>
    /// <returns>登录统计信息</returns>
    Task<Result<LoginStatisticsDto>> GetStatisticsAsync(DateTime? startDate = null, DateTime? endDate = null);

    /// <summary>
    /// 获取用户的登录统计
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="startDate">开始日期（可选）</param>
    /// <param name="endDate">结束日期（可选）</param>
    /// <returns>用户登录统计信息</returns>
    Task<Result<UserLoginStatisticsDto>> GetUserStatisticsAsync(Guid userId, DateTime? startDate = null, DateTime? endDate = null);

    /// <summary>
    /// 获取失败的登录尝试
    /// </summary>
    /// <param name="startDate">开始日期（可选）</param>
    /// <param name="endDate">结束日期（可选）</param>
    /// <param name="top">返回数量（默认100）</param>
    /// <returns>失败的登录尝试列表</returns>
    Task<Result<IEnumerable<LoginLogDto>>> GetFailedAttemptsAsync(DateTime? startDate = null, DateTime? endDate = null, int top = 100);

    /// <summary>
    /// 删除过期日志
    /// </summary>
    /// <param name="days">保留天数</param>
    /// <returns>删除的记录数</returns>
    Task<Result<int>> DeleteExpiredLogsAsync(int days = 90);

    /// <summary>
    /// Get daily login trend data for the specified date range.
    /// Provides day-by-day breakdown of login activity for charts and dashboards.
    /// </summary>
    /// <param name="startDate">Start date (inclusive)</param>
    /// <param name="endDate">End date (inclusive)</param>
    /// <param name="userId">Optional: filter by specific user</param>
    /// <returns>List of daily login trend data points</returns>
    Task<Result<IEnumerable<LoginTrendItem>>> GetLoginTrendAsync(DateTime startDate, DateTime endDate, Guid? userId = null);
}
