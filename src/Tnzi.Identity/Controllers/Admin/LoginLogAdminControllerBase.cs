namespace Tnzi.Identity.Controllers.Admin;

/// <summary>
/// 登录日志控制器基类
/// 提供登录日志查询、统计等API端点，所有方法支持重写
/// </summary>
public abstract class LoginLogAdminControllerBase : ApiAdminControllerBase
{
    protected readonly ILoginLogService LoginLogService;

    /// <summary>
    /// 初始化登录日志控制器基类
    /// </summary>
    /// <param name="loginLogService">登录日志服务</param>
    protected LoginLogAdminControllerBase(ILoginLogService loginLogService)
    {
        LoginLogService = Check.NotNull(loginLogService);
    }

    /// <summary>
    /// 根据ID获取登录日志
    /// </summary>
    /// <param name="id">日志ID</param>
    /// <returns>登录日志信息</returns>
    [HttpGet("{id}")]
    public virtual async Task<ApiResult<LoginLogDto>> GetById(Guid id)
    {
        var result = await LoginLogService.GetByIdAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取登录日志列表（分页）
    /// </summary>
    /// <param name="query">查询条件</param>
    /// <returns>分页的登录日志列表</returns>
    [HttpPost("list")]
    public virtual async Task<ApiResult<IPagedList<LoginLogDto>>> GetList([FromBody] LoginLogQueryDto query)
    {
        var result = await LoginLogService.GetPagedListAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取用户的登录日志
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="startDate">开始日期（可选）</param>
    /// <param name="endDate">结束日期（可选）</param>
    /// <param name="isSuccess">是否成功（可选）</param>
    /// <returns>登录日志列表</returns>
    [HttpGet("user/{userId}")]
    public virtual async Task<ApiResult<IEnumerable<LoginLogDto>>> GetUserLoginLogs(
        Guid userId,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] bool? isSuccess = null)
    {
        var result = await LoginLogService.GetUserLoginLogsAsync(userId, startDate, endDate, isSuccess);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取登录统计信息
    /// </summary>
    /// <param name="startDate">开始日期（可选）</param>
    /// <param name="endDate">结束日期（可选）</param>
    /// <returns>登录统计信息</returns>
    [HttpGet("statistics")]
    public virtual async Task<ApiResult<LoginStatisticsDto>> GetStatistics(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var result = await LoginLogService.GetStatisticsAsync(startDate, endDate);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取用户的登录统计
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="startDate">开始日期（可选）</param>
    /// <param name="endDate">结束日期（可选）</param>
    /// <returns>用户登录统计信息</returns>
    [HttpGet("statistics/user/{userId}")]
    public virtual async Task<ApiResult<UserLoginStatisticsDto>> GetUserStatistics(
        Guid userId,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var result = await LoginLogService.GetUserStatisticsAsync(userId, startDate, endDate);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取失败的登录尝试
    /// </summary>
    /// <param name="startDate">开始日期（可选）</param>
    /// <param name="endDate">结束日期（可选）</param>
    /// <param name="top">返回数量（默认100）</param>
    /// <returns>失败的登录尝试列表</returns>
    [HttpGet("failed-attempts")]
    public virtual async Task<ApiResult<IEnumerable<LoginLogDto>>> GetFailedAttempts(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int top = 100)
    {
        var result = await LoginLogService.GetFailedAttemptsAsync(startDate, endDate, top);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除过期登录日志
    /// </summary>
    /// <param name="days">保留天数（默认90天）</param>
    /// <returns>删除的记录数</returns>
    [HttpDelete("expired")]
    public virtual async Task<ApiResult<int>> DeleteExpired([FromQuery] int days = 90)
    {
        var result = await LoginLogService.DeleteExpiredLogsAsync(days);
        return result.ToApiResult();
    }

}
