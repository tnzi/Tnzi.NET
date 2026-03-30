namespace Tnzi.AI.Controllers.Admin;

/// <summary>
/// 评估运行管理控制器 — 提供评估历史的只读查询和删除
/// </summary>
[DefaultController]
[Route("admin/ai/evaluations")]
public class DefaultEvaluationAdminController : ApiAdminControllerBase
{
    protected readonly IEvaluationService EvaluationService;

    public DefaultEvaluationAdminController(IEvaluationService evaluationService)
    {
        EvaluationService = Check.NotNull(evaluationService);
    }

    /// <summary>
    /// 获取评估运行详情
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<EvaluationRunDetailDto>> GetById(Guid id)
    {
        var result = await EvaluationService.GetByIdAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 分页查询评估运行列表
    /// </summary>
    [HttpPost("query")]
    public virtual async Task<ApiResult<IPagedList<EvaluationRunDto>>> GetList([FromBody] EvaluationRunQueryDto query)
    {
        var result = await EvaluationService.GetListAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除评估运行记录
    /// </summary>
    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await EvaluationService.DeleteAsync(id);
        return result.ToApiResult();
    }
}
