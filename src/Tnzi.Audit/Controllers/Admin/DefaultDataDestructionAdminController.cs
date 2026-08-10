namespace Tnzi.Audit.Controllers.Admin;

/// <summary>
/// 数据销毁证明管理控制器：查证明、验链、手动触发。
/// </summary>
/// <remarks>
/// 一份没人查得到的销毁证明等于不存在——「可证明」要求这些证据能被取出示人，
/// 因此这三个端点是这项能力的组成部分，不是可选的管理糖。
/// </remarks>
[DefaultController]
[Route("admin/data-destruction")]
[ApiAuthorize(PermissionName = "audit.destruction.view")]
public class DefaultDataDestructionAdminController : ApiAdminControllerBase
{
    /// <summary>数据销毁服务。</summary>
    protected readonly IDataDestructionService DataDestructionService;

    /// <summary>
    /// 初始化数据销毁证明管理控制器。
    /// </summary>
    /// <param name="dataDestructionService">数据销毁服务。</param>
    public DefaultDataDestructionAdminController(IDataDestructionService dataDestructionService)
    {
        DataDestructionService = Check.NotNull(dataDestructionService);
    }

    /// <summary>
    /// 分页查询销毁证明。
    /// </summary>
    /// <param name="query">查询条件。</param>
    /// <returns>分页的销毁证明列表。</returns>
    [HttpPost("query")]
    public virtual async Task<ApiResult<IPagedList<DataDestructionDto>>> GetList(
        [FromBody] DataDestructionQueryDto query)
    {
        var result = await DataDestructionService.GetCertificatesAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 校验销毁证明链是否完整未被篡改。
    /// </summary>
    /// <returns>链完整时成功；断链时返回 409 并指出第一个出问题的序号。</returns>
    [HttpGet("verify")]
    public virtual async Task<ApiResult> Verify()
    {
        var result = await DataDestructionService.VerifyChainAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// 手动跑一轮销毁。
    /// </summary>
    /// <returns>每条策略的结果汇总。</returns>
    /// <remarks>
    /// 定时任务已经在跑，这个端点用于首次上线时的空跑演练，
    /// 以及「刚接了一条新策略，想立刻看它会删什么」。
    /// </remarks>
    [HttpPost("run")]
    [ApiAuthorize(PermissionName = "audit.destruction.execute")]
    public virtual async Task<ApiResult<DataDestructionRunDto>> Run()
    {
        var result = await DataDestructionService.RunAsync();
        return result.ToApiResult();
    }
}
