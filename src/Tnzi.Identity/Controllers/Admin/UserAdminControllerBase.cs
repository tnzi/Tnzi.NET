namespace Tnzi.Identity.Controllers.Admin;

/// <summary>
/// 用户管理控制器基类
/// 提供用户CRUD、状态管理、批量操作等API端点，所有方法支持重写
/// </summary>
public abstract class UserAdminControllerBase : ApiAdminControllerBase
{
    protected readonly IUserService UserService;
    protected readonly IPasswordService PasswordService;
    protected readonly IOrganizationService? OrganizationService;

    /// <summary>
    /// 初始化用户管理控制器基类
    /// </summary>
    /// <param name="userService">用户服务</param>
    /// <param name="passwordService">密码服务</param>
    /// <param name="organizationService">组织服务（可选）</param>
    protected UserAdminControllerBase(IUserService userService, IPasswordService passwordService, IOrganizationService? organizationService = null)
    {
        UserService = Check.NotNull(userService);
        PasswordService = Check.NotNull(passwordService);
        OrganizationService = organizationService;
    }

    /// <summary>
    /// 创建用户
    /// </summary>
    /// <param name="input">用户信息</param>
    /// <returns>创建的用户</returns>
    [HttpPost]
    public virtual async Task<ApiResult<UserDto>> Create([FromBody] CreateUserDto input)
    {
        var result = await UserService.CreateAsync(input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新用户
    /// </summary>
    /// <param name="id">用户ID</param>
    /// <param name="input">用户信息</param>
    /// <returns>更新后的用户</returns>
    [HttpPut("{id}")]
    public virtual async Task<ApiResult<UserDto>> Update(Guid id, [FromBody] UpdateUserDto input)
    {
        var result = await UserService.UpdateAsync(id, input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除用户
    /// </summary>
    /// <param name="id">用户ID</param>
    /// <returns>操作结果</returns>
    [HttpDelete("{id}")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await UserService.DeleteAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 根据ID获取用户
    /// </summary>
    /// <param name="id">用户ID</param>
    /// <returns>用户信息</returns>
    [HttpGet("{id}")]
    public virtual async Task<ApiResult<UserDto>> GetById(Guid id)
    {
        var result = await UserService.GetByIdAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取用户列表
    /// </summary>
    /// <param name="query">查询条件</param>
    /// <returns>用户列表</returns>
    [HttpPost("list")]
    public virtual async Task<ApiResult<IPagedList<UserListItemDto>>> GetList([FromBody] UserListQueryDto query)
    {
        var result = await UserService.GetListAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 启用用户
    /// </summary>
    /// <param name="id">用户ID</param>
    /// <returns>操作结果</returns>
    [HttpPost("{id}/enable")]
    public virtual async Task<ApiResult> Enable(Guid id)
    {
        var result = await UserService.EnableAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 禁用用户
    /// </summary>
    /// <param name="id">用户ID</param>
    /// <param name="reason">禁用原因（可选）</param>
    /// <returns>操作结果</returns>
    [HttpPost("{id}/disable")]
    public virtual async Task<ApiResult> Disable(Guid id, [FromBody] string? reason = null)
    {
        var result = await UserService.DisableAsync(id, reason);
        return result.ToApiResult();
    }

    /// <summary>
    /// 锁定用户
    /// </summary>
    /// <param name="id">用户ID</param>
    /// <param name="input">锁定信息</param>
    /// <returns>操作结果</returns>
    [HttpPost("{id}/lock")]
    public virtual async Task<ApiResult> Lock(Guid id, [FromBody] LockUserDto? input = null)
    {
        var result = await UserService.LockAsync(id, input?.LockoutEnd, input?.Reason);
        return result.ToApiResult();
    }

    /// <summary>
    /// 解锁用户
    /// </summary>
    /// <param name="id">用户ID</param>
    /// <returns>操作结果</returns>
    [HttpPost("{id}/unlock")]
    public virtual async Task<ApiResult> Unlock(Guid id)
    {
        var result = await UserService.UnlockAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 修改密码
    /// </summary>
    /// <param name="id">用户ID</param>
    /// <param name="input">密码信息</param>
    /// <returns>操作结果</returns>
    [HttpPost("{id}/change-password")]
    public virtual async Task<ApiResult> ChangePassword(Guid id, [FromBody] ChangePasswordDto input)
    {
        var result = await PasswordService.ChangePasswordAsync(id, input.CurrentPassword, input.NewPassword);
        return result.ToApiResult();
    }

    /// <summary>
    /// 重置密码（管理员操作）
    /// </summary>
    /// <param name="id">用户ID</param>
    /// <param name="input">新密码</param>
    /// <returns>操作结果</returns>
    [HttpPost("{id}/reset-password")]
    public virtual async Task<ApiResult> ResetPassword(Guid id, [FromBody] ResetPasswordByAdminDto input)
    {
        var result = await PasswordService.ResetPasswordByAdminAsync(id, input.NewPassword);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量创建用户
    /// </summary>
    /// <param name="inputs">用户信息列表</param>
    /// <returns>创建的用户列表</returns>
    [HttpPost("batch/create")]
    public virtual async Task<ApiResult<IEnumerable<UserListItemDto>>> CreateMany([FromBody] IEnumerable<CreateUserDto> inputs)
    {
        var result = await UserService.CreateManyAsync(inputs);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量更新用户
    /// </summary>
    /// <param name="inputs">用户更新信息列表</param>
    /// <returns>更新后的用户列表</returns>
    [HttpPut("batch/update")]
    public virtual async Task<ApiResult<IEnumerable<UserListItemDto>>> UpdateMany([FromBody] IEnumerable<UpdateUserBatchDto> inputs)
    {
        var updateList = inputs.Select(x => (x.Id, x.Dto));
        var result = await UserService.UpdateManyAsync(updateList);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量删除用户
    /// </summary>
    /// <param name="ids">用户ID列表</param>
    /// <returns>操作结果</returns>
    [HttpDelete("batch/delete")]
    public virtual async Task<ApiResult> DeleteMany([FromBody] IEnumerable<Guid> ids)
    {
        var result = await UserService.DeleteManyAsync(ids);
        return result.ToApiResult();
    }

    /// <summary>
    /// 分配用户到组织
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="input">组织信息</param>
    /// <returns>操作结果</returns>
    [HttpPost("{userId}/assign-organization")]
    public virtual async Task<ApiResult> AssignToOrganization(Guid userId, [FromBody] AssignOrganizationDto input)
    {
        if (OrganizationService == null)
        {
            return Error("Organization service is not available", 503);
        }
        await OrganizationService.AssignUserToOrganizationAsync(userId, input.OrganizationId);
        return Ok("User assigned to organization successfully");
    }

    /// <summary>
    /// 从组织移除用户
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>操作结果</returns>
    [HttpPost("{userId}/remove-organization")]
    public virtual async Task<ApiResult> RemoveFromOrganization(Guid userId)
    {
        if (OrganizationService == null)
        {
            return Error("Organization service is not available", 503);
        }
        await OrganizationService.RemoveUserFromOrganizationAsync(userId);
        return Ok("User removed from organization successfully");
    }

    /// <summary>
    /// 分配角色
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="input">角色ID列表</param>
    /// <returns>操作结果</returns>
    [HttpPost("{userId}/assign-roles")]
    public virtual async Task<ApiResult> AssignRoles(Guid userId, [FromBody] AssignRolesDto input)
    {
        var result = await UserService.AssignRolesAsync(userId, input.RoleIds);
        return result.ToApiResult();
    }

    /// <summary>
    /// 移除角色
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="input">角色ID列表</param>
    /// <returns>操作结果</returns>
    [HttpPost("{userId}/remove-roles")]
    public virtual async Task<ApiResult> RemoveRoles(Guid userId, [FromBody] RemoveRolesDto input)
    {
        var result = await UserService.RemoveRolesAsync(userId, input.RoleIds);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取用户统计
    /// </summary>
    /// <param name="organizationId">组织ID（可选）</param>
    /// <param name="roleId">角色ID（可选）</param>
    /// <returns>用户统计信息</returns>
    [HttpGet("statistics")]
    public virtual async Task<ApiResult<UserStatisticsDto>> GetStatistics([FromQuery] Guid? organizationId = null, [FromQuery] Guid? roleId = null)
    {
        var result = await UserService.GetStatisticsAsync(organizationId, roleId);
        return result.ToApiResult();
    }

}
