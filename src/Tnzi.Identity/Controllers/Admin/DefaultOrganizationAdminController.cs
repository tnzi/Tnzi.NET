namespace Tnzi.Identity.Controllers.Admin;

/// <summary>
/// 组织架构管理控制器
/// 提供组织CRUD、树形查询、移动组织等API端点，所有方法支持重写
/// </summary>
[DefaultController]
[Route("admin/organizations")]
[ApiAuthorize(PermissionName = "organization.view")]
public class DefaultOrganizationAdminController : ApiAdminControllerBase
{
    protected readonly IOrganizationService OrganizationService;

    /// <summary>
    /// 初始化组织架构管理控制器
    /// </summary>
    /// <param name="organizationService">组织服务</param>
    public DefaultOrganizationAdminController(IOrganizationService organizationService)
    {
        OrganizationService = Check.NotNull(organizationService);
    }

    /// <summary>
    /// 获取组织树
    /// </summary>
    /// <returns>组织树</returns>
    [HttpGet("tree")]
    public virtual async Task<ApiResult<IEnumerable<OrganizationTreeNodeDto>>> GetTree()
    {
        var result = await OrganizationService.GetTreeAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// 根据ID获取组织
    /// </summary>
    /// <param name="id">组织ID</param>
    /// <returns>组织信息</returns>
    [HttpGet("{id}")]
    public virtual async Task<ApiResult<OrganizationDto>> GetById(Guid id)
    {
        var result = await OrganizationService.GetByIdAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建组织
    /// </summary>
    /// <param name="input">组织信息</param>
    /// <returns>创建的组织</returns>
    [HttpPost]
    public virtual async Task<ApiResult<OrganizationDto>> Create([FromBody] CreateOrganizationDto input)
    {
        var result = await OrganizationService.CreateAsync(input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新组织
    /// </summary>
    /// <param name="id">组织ID</param>
    /// <param name="input">组织信息</param>
    /// <returns>更新后的组织</returns>
    [HttpPut("{id}")]
    public virtual async Task<ApiResult<OrganizationDto>> Update(Guid id, [FromBody] UpdateOrganizationDto input)
    {
        var result = await OrganizationService.UpdateAsync(id, input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除组织
    /// </summary>
    /// <param name="id">组织ID</param>
    /// <returns>操作结果</returns>
    [HttpDelete("{id}")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await OrganizationService.DeleteAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 移动组织到新的父组织
    /// </summary>
    /// <param name="id">组织ID</param>
    /// <param name="input">新父组织信息</param>
    /// <returns>操作结果</returns>
    [HttpPost("{id}/move")]
    public virtual async Task<ApiResult> Move(Guid id, [FromBody] MoveOrganizationDto input)
    {
        var result = await OrganizationService.MoveAsync(id, input.NewParentId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取组织的所有子组织（包括子子组织）
    /// </summary>
    /// <param name="id">组织ID</param>
    /// <returns>所有子组织</returns>
    [HttpGet("{id}/children")]
    public virtual async Task<ApiResult<IEnumerable<OrganizationDto>>> GetAllChildren(Guid id)
    {
        var result = await OrganizationService.GetAllChildrenAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取组织的所有父组织（包括父父组织）
    /// </summary>
    /// <param name="id">组织ID</param>
    /// <returns>所有父组织</returns>
    [HttpGet("{id}/parents")]
    public virtual async Task<ApiResult<IEnumerable<OrganizationDto>>> GetAllParents(Guid id)
    {
        var result = await OrganizationService.GetAllParentsAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量创建组织
    /// </summary>
    /// <param name="inputs">组织信息列表</param>
    /// <returns>创建的组织列表</returns>
    [HttpPost("batch/create")]
    public virtual async Task<ApiResult<IEnumerable<OrganizationDto>>> CreateMany([FromBody] IEnumerable<CreateOrganizationDto> inputs)
    {
        var result = await OrganizationService.CreateManyAsync(inputs);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量更新组织
    /// </summary>
    /// <param name="inputs">组织更新信息列表</param>
    /// <returns>更新后的组织列表</returns>
    [HttpPut("batch/update")]
    public virtual async Task<ApiResult<IEnumerable<OrganizationDto>>> UpdateMany([FromBody] IEnumerable<UpdateOrganizationBatchDto> inputs)
    {
        var updateList = inputs.Select(x => (x.Id, x.Dto));
        var result = await OrganizationService.UpdateManyAsync(updateList);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量删除组织
    /// </summary>
    /// <param name="ids">组织ID列表</param>
    /// <returns>操作结果</returns>
    [HttpDelete("batch/delete")]
    public virtual async Task<ApiResult> DeleteMany([FromBody] IEnumerable<Guid> ids)
    {
        var result = await OrganizationService.DeleteManyAsync(ids);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取组织的人员统计
    /// </summary>
    /// <param name="id">组织ID</param>
    /// <returns>人员统计信息</returns>
    [HttpGet("{id}/statistics")]
    public virtual async Task<ApiResult<OrganizationStatisticsDto>> GetStatistics(Guid id)
    {
        var result = await OrganizationService.GetStatisticsAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 根据名称或代码模糊搜索组织
    /// </summary>
    /// <param name="keyword">搜索关键词</param>
    /// <param name="maxResults">最大返回数量（默认20）</param>
    /// <returns>匹配的组织列表</returns>
    [HttpGet("search")]
    public virtual async Task<ApiResult<IEnumerable<OrganizationDto>>> Search([FromQuery] string keyword, [FromQuery] int maxResults = 20)
    {
        var result = await OrganizationService.SearchAsync(keyword, maxResults);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新组织排序
    /// </summary>
    /// <param name="id">组织ID</param>
    /// <param name="input">排序值</param>
    /// <returns>操作结果</returns>
    [HttpPut("{id}/sort-order")]
    public virtual async Task<ApiResult> UpdateSortOrder(Guid id, [FromBody] UpdateSortOrderDto input)
    {
        var result = await OrganizationService.UpdateSortOrderAsync(id, input.SortOrder);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量更新组织排序（适用于前端拖拽排序场景）
    /// </summary>
    /// <param name="inputs">排序更新列表</param>
    /// <returns>操作结果</returns>
    [HttpPut("batch/sort-order")]
    public virtual async Task<ApiResult> BatchUpdateSortOrder([FromBody] IEnumerable<BatchSortOrderItemDto> inputs)
    {
        var updates = inputs.Select(x => (x.Id, x.SortOrder));
        var result = await OrganizationService.BatchUpdateSortOrderAsync(updates);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取组织下的用户分页列表
    /// </summary>
    /// <param name="id">组织ID</param>
    /// <param name="query">分页参数</param>
    /// <param name="includeChildren">是否包含子组织的用户</param>
    /// <returns>用户分页列表</returns>
    [HttpGet("{id}/users")]
    public virtual async Task<ApiResult<IPagedList<UserListItemDto>>> GetUsers(Guid id, [FromQuery] PagedQueryDto query, [FromQuery] bool includeChildren = false)
    {
        var result = await OrganizationService.GetUsersAsync(id, query, includeChildren);
        return result.ToApiResult();
    }

}
