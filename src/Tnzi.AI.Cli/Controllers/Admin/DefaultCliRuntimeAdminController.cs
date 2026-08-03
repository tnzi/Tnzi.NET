// R3：二级目录（Controllers/Admin/）不产生子命名空间。
namespace Tnzi.AI.Cli.Controllers;

/// <summary>
/// 外部 CLI 运行时注册表管理。
/// </summary>
[DefaultController]
[Route("admin/ai/cli-runtimes")]
[ApiAuthorize(PermissionName = "ai.cliRuntime.view")]
public class DefaultCliRuntimeAdminController : ApiAdminControllerBase
{
    /// <summary>运行时注册表服务。</summary>
    protected readonly ICliRuntimeService RuntimeService;

    /// <summary>初始化控制器。</summary>
    public DefaultCliRuntimeAdminController(ICliRuntimeService runtimeService)
    {
        RuntimeService = Check.NotNull(runtimeService);
    }

    /// <summary>列出已注册的外部运行时。</summary>
    [HttpGet]
    public virtual async Task<ApiResult<List<CliRuntimeDto>>> GetList(CancellationToken ct = default)
        => (await RuntimeService.GetListAsync(ct)).ToApiResult();

    /// <summary>取单个运行时。</summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<CliRuntimeDto>> Get(Guid id, CancellationToken ct = default)
        => (await RuntimeService.GetAsync(id, ct)).ToApiResult();

    /// <summary>列出本部署可用的 provider（含「存在但本版本未实现」的诚实标记）。</summary>
    [HttpGet("providers")]
    public virtual async Task<ApiResult<List<CliProviderOptionDto>>> GetProviders(CancellationToken ct = default)
        => (await RuntimeService.GetProviderOptionsAsync(ct)).ToApiResult();

    /// <summary>立即探测本宿主 PATH 上的 CLI 并注册/更新运行时。</summary>
    [HttpPost("probe")]
    [ApiAuthorize(PermissionName = "ai.cliRuntime.execute")]
    public virtual async Task<ApiResult<CliRuntimeProbeResultDto>> Probe(CancellationToken ct = default)
        => (await RuntimeService.ProbeAsync(ct)).ToApiResult();

    /// <summary>更新运行时的可改字段。</summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "ai.cliRuntime.update")]
    public virtual async Task<ApiResult<CliRuntimeDto>> Update(
        Guid id, [FromBody] UpdateCliRuntimeDto input, CancellationToken ct = default)
        => (await RuntimeService.UpdateAsync(id, input, ct)).ToApiResult();

    /// <summary>删除运行时注册。</summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "ai.cliRuntime.delete")]
    public virtual async Task<ApiResult> Delete(Guid id, CancellationToken ct = default)
        => (await RuntimeService.DeleteAsync(id, ct)).ToApiResult();
}
