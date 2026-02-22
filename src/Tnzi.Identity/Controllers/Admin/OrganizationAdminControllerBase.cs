namespace Tnzi.Identity.Controllers.Admin;

/// <summary>
/// 组织架构控制器基类
/// 提供组织CRUD、树形查询、移动组织等API端点，所有方法支持重写
/// </summary>
public abstract class OrganizationAdminControllerBase : ApiAdminControllerBase
{
    protected readonly IOrganizationService OrganizationService;

    /// <summary>
    /// 初始化组织架构控制器基类
    /// </summary>
    /// <param name="organizationService">组织服务</param>
    protected OrganizationAdminControllerBase(IOrganizationService organizationService)
    {
        OrganizationService = Check.NotNull(organizationService);
    }

    /// <summary>
    /// 获取组织树
    /// </summary>
    /// <returns>组织树</returns>
    [HttpGet("tree")]
    public virtual async Task<ApiResult<IEnumerable<OrganizationDto>>> GetTree()
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

}
