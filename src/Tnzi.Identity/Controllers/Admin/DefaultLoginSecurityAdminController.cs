namespace Tnzi.Identity.Controllers.Admin;

/// <summary>
/// 登录安全管理控制器
/// 提供安全概览、异常登录检测、用户安全状态查询等管理端点
/// </summary>
[DefaultController]
[Route("admin/login-security")]
public class DefaultLoginSecurityAdminController : ApiAdminControllerBase
{
    protected readonly ILoginSecurityService LoginSecurityService;

    /// <summary>
    /// 初始化登录安全管理控制器
    /// </summary>
    /// <param name="loginSecurityService">登录安全服务</param>
    public DefaultLoginSecurityAdminController(ILoginSecurityService loginSecurityService)
    {
        LoginSecurityService = Check.NotNull(loginSecurityService);
    }

    /// <summary>
    /// 获取登录安全概览（仪表盘数据）
    /// </summary>
    /// <param name="hours">统计时间范围（小时，默认24小时）</param>
    /// <returns>安全概览数据</returns>
    [HttpGet("overview")]
    public virtual async Task<ApiResult<SecurityOverviewDto>> GetOverview([FromQuery] int hours = 24)
    {
        var result = await LoginSecurityService.GetSecurityOverviewAsync(hours);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取频繁登录失败的用户列表
    /// </summary>
    /// <param name="hours">统计时间范围（小时，默认24小时）</param>
    /// <param name="minFailures">最小失败次数阈值（默认3次）</param>
    /// <returns>频繁登录失败的用户列表</returns>
    [HttpGet("frequent-failures")]
    public virtual async Task<ApiResult<IEnumerable<UserFailedLoginSummaryDto>>> GetFrequentFailures(
        [FromQuery] int hours = 24,
        [FromQuery] int minFailures = 3)
    {
        var result = await LoginSecurityService.GetUsersWithFrequentFailuresAsync(hours, minFailures);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取用户的最近登录记录
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="count">记录数量（默认10条）</param>
    /// <returns>登录记录列表</returns>
    [HttpGet("user/{userId}/recent-logins")]
    public virtual async Task<ApiResult<IEnumerable<LoginLogDto>>> GetUserRecentLogins(Guid userId, [FromQuery] int count = 10)
    {
        var logins = await LoginSecurityService.GetRecentLoginsAsync(userId, count);
        return ApiResult<IEnumerable<LoginLogDto>>.Ok(logins);
    }

    /// <summary>
    /// 获取用户的常用IP地址
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>常用IP列表</returns>
    [HttpGet("user/{userId}/frequent-ips")]
    public virtual async Task<ApiResult<IEnumerable<string>>> GetUserFrequentIps(Guid userId)
    {
        var ips = await LoginSecurityService.GetFrequentIpAddressesAsync(userId);
        return ApiResult<IEnumerable<string>>.Ok(ips);
    }

    /// <summary>
    /// 手动检测用户的异常登录风险
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="ipAddress">IP地址（可选）</param>
    /// <param name="userAgent">User-Agent（可选）</param>
    /// <returns>异常登录检测结果</returns>
    [HttpPost("user/{userId}/detect-abnormal")]
    public virtual async Task<ApiResult<AbnormalLoginResult>> DetectAbnormalLogin(
        Guid userId,
        [FromQuery] string? ipAddress = null,
        [FromQuery] string? userAgent = null)
    {
        var result = await LoginSecurityService.DetectAbnormalLoginAsync(userId, ipAddress, userAgent);
        return ApiResult<AbnormalLoginResult>.Ok(result);
    }
}
