namespace Tnzi.Identity.Controllers.Admin;

/// <summary>
/// 用户管理控制器
/// 提供用户CRUD、状态管理、批量操作等API端点，所有方法支持重写
/// </summary>
[DefaultController]
[Route("admin/users")]
[ApiAuthorize(PermissionName = "user.view")]
public class DefaultUserAdminController : ApiAdminControllerBase
{
    protected readonly IUserService UserService;
    protected readonly IPasswordService PasswordService;

    /// <summary>
    /// 组织架构服务。实现住在可选包 <c>Tnzi.Identity.Organization</c> 里，
    /// 未加载时为 null，两个组织端点答 <see cref="OrganizationModuleMissing"/> + 501。
    /// </summary>
    protected readonly IOrganizationService? OrganizationService;

    /// <summary>
    /// 组织包缺席时的统一回答：<b>501 + 指名要加载什么</b>。
    /// </summary>
    /// <remarks>
    /// 用 501 而不是 503：503 是「暂时不可用」，会让监控与客户端的重试逻辑照着它一直重试
    /// 一件永远不会变好的事（这台宿主根本没装组织架构）。也不用 404：路由确实存在，
    /// 404 会让调用方以为自己拼错了 URL，从而去查一个不存在的问题。501 恰好就是
    /// 「这台服务器不提供这项功能」，也是框架内既有的同类回答（Storage 的工作区包、
    /// Finance 的 ICheckDocumentRenderer / IReceiptExtractor 缺席时同样是 501）。
    /// 消息里点名包名，是因为「少加载一个可选包」在日志里唯一能自证的方式就是它自己说出来。
    ///
    /// ★ 这两个端点**留在本控制器上**而不是搬去组织包：<c>[Route("admin/users")]</c> 是核心
    /// 已经拥有的模板，在同一模板上新铸一个 <c>[DefaultController]</c>，会让继承本默认控制器的
    /// 消费方（<c>[DefaultController]</c> 是 <c>Inherited=false</c>、<c>[Route]</c> 是
    /// <c>Inherited=true</c>）把组织包的端点一并继承走；换个前缀又会为一次内部重构
    /// 打断所有既有调用方的 URL。
    /// </remarks>
    protected const string OrganizationModuleMissing =
        "Organization assignment requires the Tnzi.Identity.Organization module, which this host has not loaded.";

    /// <summary>
    /// 初始化用户管理控制器
    /// </summary>
    /// <param name="userService">用户服务</param>
    /// <param name="passwordService">密码服务</param>
    /// <param name="organizationService">组织服务（可选）</param>
    public DefaultUserAdminController(IUserService userService, IPasswordService passwordService, IOrganizationService? organizationService = null)
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
    [ApiAuthorize(PermissionName = "user.create")]
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
    [ApiAuthorize(PermissionName = "user.update")]
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
    [ApiAuthorize(PermissionName = "user.delete")]
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
    [ApiAuthorize(PermissionName = "user.update")]
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
    [ApiAuthorize(PermissionName = "user.update")]
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
    [ApiAuthorize(PermissionName = "user.update")]
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
    [ApiAuthorize(PermissionName = "user.update")]
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
    [ApiAuthorize(PermissionName = "user.update")]
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
    [ApiAuthorize(PermissionName = "user.update")]
    public virtual async Task<ApiResult> ResetPassword(Guid id, [FromBody] ResetPasswordByAdminDto input)
    {
        var result = await PasswordService.ResetPasswordByAdminAsync(id, input.NewPassword, input.RequireChangeOnNextLogin);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量创建用户
    /// </summary>
    /// <param name="inputs">用户信息列表</param>
    /// <returns>创建的用户列表</returns>
    [HttpPost("batch/create")]
    [ApiAuthorize(PermissionName = "user.create")]
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
    [ApiAuthorize(PermissionName = "user.update")]
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
    [ApiAuthorize(PermissionName = "user.delete")]
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
    [ApiAuthorize(PermissionName = "user.update")]
    public virtual async Task<ApiResult> AssignToOrganization(Guid userId, [FromBody] AssignOrganizationDto input)
    {
        if (OrganizationService == null)
        {
            return Error(OrganizationModuleMissing, 501);
        }
        // ★ 结果必须回传：这两个方法返回 Result，丢弃它会让「分配到一个不存在的组织」
        // 也回 200 successfully —— 与本端点自己的 501 守卫（少了包就明说）自相矛盾。
        var assignResult = await OrganizationService.AssignUserToOrganizationAsync(userId, input.OrganizationId);
        return assignResult.ToApiResult();
    }

    /// <summary>
    /// 从组织移除用户
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>操作结果</returns>
    [HttpPost("{userId}/remove-organization")]
    [ApiAuthorize(PermissionName = "user.update")]
    public virtual async Task<ApiResult> RemoveFromOrganization(Guid userId)
    {
        if (OrganizationService == null)
        {
            return Error(OrganizationModuleMissing, 501);
        }
        var removeResult = await OrganizationService.RemoveUserFromOrganizationAsync(userId);
        return removeResult.ToApiResult();
    }

    /// <summary>
    /// 分配角色
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="input">角色ID列表</param>
    /// <returns>操作结果</returns>
    [HttpPost("{userId}/assign-roles")]
    [ApiAuthorize(PermissionName = "user.update")]
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
    [ApiAuthorize(PermissionName = "user.update")]
    public virtual async Task<ApiResult> RemoveRoles(Guid userId, [FromBody] RemoveRolesDto input)
    {
        var result = await UserService.RemoveRolesAsync(userId, input.RoleIds);
        return result.ToApiResult();
    }

    /// <summary>
    /// 把某个用户的邮箱 / 手机号标记为已确认。
    /// </summary>
    /// <remarks>
    /// ★ 单独一个端点，而不是 <c>PUT admin/users/{id}</c> 的副作用：改地址一律清确认位，
    /// 「替这个地址担保」是另一件事，需要有人显式按下并留痕（见 <c>IUserService.ConfirmContactAsync</c>）。
    /// </remarks>
    /// <param name="userId">用户ID</param>
    /// <param name="input">要确认哪几项（null 表示不动）</param>
    /// <returns>操作结果</returns>
    [HttpPost("{userId}/confirm-contact")]
    [ApiAuthorize(PermissionName = "user.update")]
    public virtual async Task<ApiResult> ConfirmContact(Guid userId, [FromBody] ConfirmContactDto input)
    {
        Check.NotNull(input);

        var result = await UserService.ConfirmContactAsync(userId, input.ConfirmEmail, input.ConfirmPhoneNumber);
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

    /// <summary>
    /// Export users as CSV file
    /// </summary>
    [HttpPost("export/csv")]
    public virtual async Task<IActionResult> ExportCsv([FromBody] UserListQueryDto? query = null)
    {
        var result = await UserService.ExportUsersCsvAsync(query);
        return CsvFile(result, "users_export");
    }

    /// <summary>
    /// Import users from CSV file
    /// </summary>
    [HttpPost("import/csv")]
    [ApiAuthorize(PermissionName = "user.create")]
    public virtual async Task<ApiResult<UserImportResult>> ImportCsv(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest<UserImportResult>("CSV file is required");
        }

        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
        var csvContent = await reader.ReadToEndAsync();

        var result = await UserService.ImportUsersCsvAsync(csvContent);
        return result.ToApiResult();
    }
}
