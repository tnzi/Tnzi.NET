namespace Tnzi.Finance.Controllers.Admin;

/// <summary>
/// 目录项管理控制器
/// </summary>
[Route("admin/finance/items")]
[DefaultController]
[ApiAuthorize(PermissionName = "finance.item.view")]
public class DefaultFinanceItemAdminController : ApiAdminControllerBase
{
    private readonly IItemService _itemService;

    public DefaultFinanceItemAdminController(IItemService itemService)
    {
        _itemService = Check.NotNull(itemService);
    }

    protected IItemService ItemService => _itemService;

    /// <summary>
    /// 分页查询目录项
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<ItemDto>>> GetPaged([FromQuery] ItemQueryDto query)
    {
        var result = await _itemService.GetPagedAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取目录项
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<ItemDto>> Get(Guid id)
    {
        var result = await _itemService.GetAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建目录项
    /// </summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "finance.item.create")]
    public virtual async Task<ApiResult<ItemDto>> Create([FromBody] CreateItemDto request)
    {
        var result = await _itemService.CreateAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新目录项
    /// </summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.item.update")]
    public virtual async Task<ApiResult<ItemDto>> Update(Guid id, [FromBody] UpdateItemDto request)
    {
        var result = await _itemService.UpdateAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除目录项
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.item.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await _itemService.DeleteAsync(id);
        return result.ToApiResult();
    }
}
