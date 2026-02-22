namespace Tnzi.Identity.Services;

/// <summary>
/// 组织架构服务接口
/// </summary>
public interface IOrganizationService
{
    /// <summary>
    /// 获取组织树
    /// </summary>
    /// <returns>组织树</returns>
    Task<Result<IEnumerable<OrganizationDto>>> GetTreeAsync();

    /// <summary>
    /// 根据ID获取组织
    /// </summary>
    /// <param name="id">组织ID</param>
    /// <returns>组织信息</returns>
    Task<Result<OrganizationDto>> GetByIdAsync(Guid id);

    /// <summary>
    /// 创建组织
    /// </summary>
    /// <param name="input">组织信息</param>
    /// <returns>创建的组织</returns>
    Task<Result<OrganizationDto>> CreateAsync(CreateOrganizationDto input);

    /// <summary>
    /// 更新组织
    /// </summary>
    /// <param name="id">组织ID</param>
    /// <param name="input">组织信息</param>
    /// <returns>更新后的组织</returns>
    Task<Result<OrganizationDto>> UpdateAsync(Guid id, UpdateOrganizationDto input);

    /// <summary>
    /// 删除组织
    /// </summary>
    /// <param name="id">组织ID</param>
    Task<Result> DeleteAsync(Guid id);

    /// <summary>
    /// 移动组织到新的父组织
    /// </summary>
    /// <param name="id">组织ID</param>
    /// <param name="newParentId">新父组织ID</param>
    Task<Result> MoveAsync(Guid id, Guid? newParentId);

    /// <summary>
    /// 获取组织的所有子组织（包括子子组织）
    /// </summary>
    /// <param name="id">组织ID</param>
    /// <returns>所有子组织</returns>
    Task<Result<IEnumerable<OrganizationDto>>> GetAllChildrenAsync(Guid id);

    /// <summary>
    /// 获取组织的所有父组织（包括父父组织）
    /// </summary>
    /// <param name="id">组织ID</param>
    /// <returns>所有父组织</returns>
    Task<Result<IEnumerable<OrganizationDto>>> GetAllParentsAsync(Guid id);

    /// <summary>
    /// 批量创建组织
    /// </summary>
    /// <param name="inputs">组织信息列表</param>
    /// <returns>创建的组织列表</returns>
    Task<Result<IEnumerable<OrganizationDto>>> CreateManyAsync(IEnumerable<CreateOrganizationDto> inputs);

    /// <summary>
    /// 批量更新组织
    /// </summary>
    /// <param name="inputs">组织更新信息列表（ID和DTO）</param>
    /// <returns>更新后的组织列表</returns>
    Task<Result<IEnumerable<OrganizationDto>>> UpdateManyAsync(IEnumerable<(Guid Id, UpdateOrganizationDto Dto)> inputs);

    /// <summary>
    /// 批量删除组织
    /// </summary>
    /// <param name="ids">组织ID列表</param>
    Task<Result> DeleteManyAsync(IEnumerable<Guid> ids);

    /// <summary>
    /// 获取组织的人员统计
    /// </summary>
    /// <param name="id">组织ID</param>
    /// <returns>人员统计信息（总人数、直接下属人数等）</returns>
    Task<Result<OrganizationStatisticsDto>> GetStatisticsAsync(Guid id);

    /// <summary>
    /// 分配用户到组织
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="organizationId">组织ID</param>
    Task<Result> AssignUserToOrganizationAsync(Guid userId, Guid organizationId);

    /// <summary>
    /// 从组织移除用户
    /// </summary>
    /// <param name="userId">用户ID</param>
    Task<Result> RemoveUserFromOrganizationAsync(Guid userId);
}

