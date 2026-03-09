namespace Tnzi.AI.Controllers.Admin;

/// <summary>
/// AI Provider 管理控制器
/// 提供提供商和模型信息的查询端点
/// </summary>
[DefaultController]
[Route("admin/ai/providers")]
public class DefaultProviderAdminController : ApiAdminControllerBase
{
    protected readonly IChatClientFactory ClientFactory;

    /// <summary>
    /// 初始化 Provider 管理控制器
    /// </summary>
    public DefaultProviderAdminController(IChatClientFactory clientFactory)
    {
        ClientFactory = Check.NotNull(clientFactory);
    }

    /// <summary>
    /// 获取所有已启用的提供商列表
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
}

/// <summary>
/// Provider 默认模型信息 DTO
/// </summary>
public class ProviderDefaultModelDto
{
    /// <summary>Provider name</summary>
    public string ProviderName { get; set; } = string.Empty;
    /// <summary>Default model name</summary>
    public string? DefaultModel { get; set; }
}
