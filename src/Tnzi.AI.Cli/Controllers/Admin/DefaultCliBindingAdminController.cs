// R3：二级目录（Controllers/Admin/）不产生子命名空间。
namespace Tnzi.AI.Cli.Controllers;

/// <summary>
/// Agent → 外部运行时绑定管理。
/// </summary>
[DefaultController]
[Route("admin/ai/cli-bindings")]
[ApiAuthorize(PermissionName = "ai.cliBinding.view")]
public class DefaultCliBindingAdminController : ApiAdminControllerBase
{
    /// <summary>绑定服务。</summary>
    protected readonly ICliAgentBindingService BindingService;

    /// <summary>初始化控制器。</summary>
    public DefaultCliBindingAdminController(ICliAgentBindingService bindingService)
    {
        BindingService = Check.NotNull(bindingService);
    }

    /// <summary>
    /// 取某个 Agent 的绑定。<b>无绑定返回 null 数据而不是 404</b> ——
    /// 「这个 Agent 走内建执行」是一个正常答案，前端据此渲染"未绑定"状态。
    /// </summary>
    [HttpGet("{agentId:guid}")]
    public virtual async Task<ApiResult<CliAgentBindingDto?>> Get(Guid agentId, CancellationToken ct = default)
        => ApiResult<CliAgentBindingDto?>.Ok(await BindingService.GetByAgentIdAsync(agentId, ct));

    /// <summary>新建或更新绑定。</summary>
    [HttpPut("{agentId:guid}")]
    [ApiAuthorize(PermissionName = "ai.cliBinding.update")]
    public virtual async Task<ApiResult<CliAgentBindingDto>> Upsert(
        Guid agentId, [FromBody] UpsertCliAgentBindingDto input, CancellationToken ct = default)
        => (await BindingService.UpsertAsync(agentId, input, ct)).ToApiResult();

    /// <summary>解除绑定 —— 该 Agent 回到内建执行。</summary>
    [HttpDelete("{agentId:guid}")]
    [ApiAuthorize(PermissionName = "ai.cliBinding.delete")]
    public virtual async Task<ApiResult> Delete(Guid agentId, CancellationToken ct = default)
        => (await BindingService.DeleteAsync(agentId, ct)).ToApiResult();
}
