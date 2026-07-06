namespace Tnzi.AI.Controllers.Admin;

/// <summary>
/// AI Provider 管理控制器
/// </summary>
/// <remarks>
/// 提供两类端点：
/// <list type="bullet">
///   <item><c>GET /</c> 与 <c>GET /{providerName}/default-model</c> — 配置驱动的只读端点
///         （向后兼容；底层走 IChatClientFactory，读取 appsettings 的 ProviderOptions）。</item>
///   <item><c>POST /query</c> 等 <c>entities/*</c> 子路由 — 实体驱动 CRUD（Phase 5 backend prereq），
///         写入数据库并对 API Key 进行加密存储。</item>
/// </list>
/// 两类端点目前并存：未来若 IChatClientFactory 接入 Provider 实体表，配置驱动端点会自动
/// 反映合并结果（数据库 + 配置）。
/// </remarks>
[DefaultController]
[Route("admin/ai/providers")]
[ApiAuthorize(PermissionName = "ai.provider.view")]
public class DefaultProviderAdminController : ApiAdminControllerBase
{
    protected readonly IChatClientFactory ClientFactory;
    protected readonly IProviderService ProviderService;

    /// <summary>
    /// 初始化 Provider 管理控制器
    /// </summary>
    public DefaultProviderAdminController(IChatClientFactory clientFactory, IProviderService providerService)
    {
        ClientFactory = Check.NotNull(clientFactory);
        ProviderService = Check.NotNull(providerService);
    }

    /// <summary>
    /// 获取所有已启用的提供商列表（配置驱动）
    /// </summary>
    [HttpGet]
    public virtual ApiResult<List<string>> GetProviders()
    {
        var providers = ClientFactory.GetAvailableProviders().ToList();
        return ApiResult<List<string>>.Ok(providers);
    }

    /// <summary>
    /// 获取指定提供商的默认模型
    /// </summary>
    [HttpGet("{providerName}/default-model")]
    public virtual ApiResult<ProviderDefaultModelDto> GetDefaultModel(string providerName)
    {
        var model = ClientFactory.GetDefaultModel(providerName);
        return ApiResult<ProviderDefaultModelDto>.Ok(new ProviderDefaultModelDto { ProviderName = providerName, DefaultModel = model });
    }

    /// <summary>
    /// 获取启用的 Provider 下拉选项（供 Agent 配置的 Provider 下拉框，替代自由文本输入）
    /// </summary>
    [HttpGet("options")]
    public virtual async Task<ApiResult<List<ProviderOptionDto>>> GetOptions()
    {
        var result = await ProviderService.GetOptionsAsync(HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// 列出指定 Provider 可用的模型（优先实时 /v1/models，失败按类型静态兜底）
    /// </summary>
    [HttpGet("entities/{id:guid}/models")]
    public virtual async Task<ApiResult<ProviderModelsDto>> ListModels(Guid id)
    {
        var result = await ProviderService.ListModelsAsync(id, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    // ---- Entity-driven CRUD (Phase 5 backend prereq) -------------------------

    /// <summary>
    /// 分页查询 Provider 实体列表
    /// </summary>
    [HttpPost("query")]
    public virtual async Task<ApiResult<IPagedList<ProviderDto>>> GetList([FromBody] ProviderQueryDto query)
    {
        var result = await ProviderService.GetPagedListAsync(query, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// 按 ID 获取 Provider 实体
    /// </summary>
    [HttpGet("entities/{id:guid}")]
    public virtual async Task<ApiResult<ProviderDto>> GetById(Guid id)
    {
        var result = await ProviderService.GetByIdAsync(id, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建 Provider 实体
    /// </summary>
    [HttpPost("entities")]
    public virtual async Task<ApiResult<ProviderDto>> Create([FromBody] CreateProviderDto dto)
    {
        var result = await ProviderService.CreateAsync(dto, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新 Provider 实体
    /// </summary>
    [HttpPut("entities/{id:guid}")]
    public virtual async Task<ApiResult<ProviderDto>> Update(Guid id, [FromBody] UpdateProviderDto dto)
    {
        var result = await ProviderService.UpdateAsync(id, dto, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// 软删除 Provider 实体
    /// </summary>
    [HttpDelete("entities/{id:guid}")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await ProviderService.DeleteAsync(id, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// 测试 Provider 连接（轻量探针：校验 IsEnabled + API Key 解密）
    /// </summary>
    [HttpPost("entities/{id:guid}/test")]
    public virtual async Task<ApiResult<ProviderTestResultDto>> TestConnection(Guid id)
    {
        var result = await ProviderService.TestConnectionAsync(id, HttpContext.RequestAborted);
        return result.ToApiResult();
    }
}
