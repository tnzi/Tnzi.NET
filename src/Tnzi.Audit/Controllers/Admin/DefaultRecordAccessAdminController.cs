namespace Tnzi.Audit.Controllers.Admin;

/// <summary>
/// 记录级读取审计管理控制器：查「谁读了哪一条数据」、验链、看读取量分布。
/// </summary>
/// <remarks>
/// <para>
/// 登记只是手段，能回答「上个月谁看过这位举报人的材料」才是目的——
/// 一张只写不读的表，在合规问询到来时和没有这张表没有区别。
/// </para>
/// <para>
/// <strong>本控制器的端点自身不做记录级登记。</strong>「谁查了这张审计表」由请求级审计
/// （<c>Audit_Operation</c>）回答；在这里再登记一次会让审计表被自己的查询灌满，
/// 而每一次翻页都会往被查记录的访问历史里插进一条无关的行。
/// </para>
/// </remarks>
[DefaultController]
[Route("admin/record-access")]
[ApiAuthorize(PermissionName = "audit.recordAccess.view")]
public class DefaultRecordAccessAdminController : ApiAdminControllerBase
{
    /// <summary>记录级读取审计服务。</summary>
    protected readonly IRecordAccessAuditor RecordAccessAuditor;

    /// <summary>
    /// 初始化记录级读取审计管理控制器。
    /// </summary>
    /// <param name="recordAccessAuditor">记录级读取审计服务。</param>
    public DefaultRecordAccessAdminController(IRecordAccessAuditor recordAccessAuditor)
    {
        RecordAccessAuditor = Check.NotNull(recordAccessAuditor);
    }

    /// <summary>
    /// 分页查询读取记录。
    /// </summary>
    /// <param name="query">查询条件（按记录查「都被谁看过」，按用户查「看过哪些记录」）。</param>
    /// <returns>分页的读取记录，按时间倒序。</returns>
    [HttpPost("query")]
    public virtual async Task<ApiResult<IPagedList<RecordAccessDto>>> GetList(
        [FromBody] RecordAccessQueryDto query)
    {
        var result = await RecordAccessAuditor.GetAccessesAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 按读取者汇总读取量，用于发现异常访问。
    /// </summary>
    /// <param name="startTime">统计起始时间（可选）。</param>
    /// <param name="endTime">统计结束时间（可选）。</param>
    /// <param name="topN">返回读取量最高的前 N 位，默认 20。</param>
    /// <returns>按读取次数降序的用户统计。</returns>
    [HttpGet("user-statistics")]
    public virtual async Task<ApiResult<List<RecordAccessUserStatDto>>> GetUserStatistics(
        [FromQuery] DateTime? startTime = null,
        [FromQuery] DateTime? endTime = null,
        [FromQuery] int topN = 20)
    {
        var result = await RecordAccessAuditor.GetUserStatisticsAsync(startTime, endTime, topN);
        return result.ToApiResult();
    }

    /// <summary>
    /// 校验某个用户的读取审计链是否完整未被篡改。
    /// </summary>
    /// <param name="userId">用户 ID；省略表示校验匿名访问形成的那条链。</param>
    /// <returns>链完整时成功；断链时返回 409 并指出第一个出问题的序号。</returns>
    [HttpGet("verify")]
    public virtual async Task<ApiResult> Verify([FromQuery] Guid? userId = null)
    {
        var result = await RecordAccessAuditor.VerifyChainAsync(userId);
        return result.ToApiResult();
    }
}
