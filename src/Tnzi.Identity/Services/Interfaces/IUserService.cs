namespace Tnzi.Identity.Services;

/// <summary>
/// 用户管理服务接口
/// </summary>
public interface IUserService
{
    /// <summary>
    /// 创建用户
    /// </summary>
    Task<Result<UserDto>> CreateAsync(CreateUserDto input);

    /// <summary>
    /// 更新用户（管理端口径，可改角色与所属组织）
    /// </summary>
    Task<Result<UserDto>> UpdateAsync(Guid id, UpdateUserDto input);

    /// <summary>
    /// 更新用户本人的资料（自助口径，<b>不可</b>改角色与所属组织）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="UpdateAsync"/> 的区别只有一个：入参类型。
    /// <see cref="UpdateProfileDto"/> 不含 <c>RoleIds</c>/<c>OrganizationId</c>，
    /// 因此自助路径上的提权不是「被拦住」而是无法表达。理由详见该类型的注释。
    /// </remarks>
    Task<Result<UserDto>> UpdateProfileAsync(Guid id, UpdateProfileDto input);

    /// <summary>
    /// 删除用户
    /// </summary>
    Task<Result> DeleteAsync(Guid id);

    /// <summary>
    /// 根据ID获取用户
    /// </summary>
    Task<Result<UserDto>> GetByIdAsync(Guid id);

    /// <summary>
    /// 获取用户列表
    /// </summary>
    Task<Result<IPagedList<UserListItemDto>>> GetListAsync(UserListQueryDto query);

    /// <summary>
    /// 启用用户
    /// </summary>
    Task<Result> EnableAsync(Guid id);

    /// <summary>
    /// 禁用用户
    /// </summary>
    Task<Result> DisableAsync(Guid id, string? reason = null);

    /// <summary>
    /// 锁定用户
    /// </summary>
    Task<Result> LockAsync(Guid id, DateTimeOffset? lockoutEnd = null, string? reason = null);

    /// <summary>
    /// 解锁用户
    /// </summary>
    Task<Result> UnlockAsync(Guid id);

    /// <summary>
    /// 批量创建用户
    /// </summary>
    Task<Result<IEnumerable<UserListItemDto>>> CreateManyAsync(IEnumerable<CreateUserDto> inputs);

    /// <summary>
    /// 批量更新用户
    /// </summary>
    Task<Result<IEnumerable<UserListItemDto>>> UpdateManyAsync(IEnumerable<(Guid Id, UpdateUserDto Dto)> inputs);

    /// <summary>
    /// 批量删除用户
    /// </summary>
    Task<Result> DeleteManyAsync(IEnumerable<Guid> ids);

    /// <summary>
    /// 分配角色
    /// </summary>
    Task<Result> AssignRolesAsync(Guid userId, IEnumerable<Guid> roleIds);

    /// <summary>
    /// 移除角色
    /// </summary>
    Task<Result> RemoveRolesAsync(Guid userId, IEnumerable<Guid> roleIds);

    /// <summary>
    /// 获取用户统计
    /// </summary>
    Task<Result<UserStatisticsDto>> GetStatisticsAsync(Guid? organizationId = null, Guid? roleId = null);

    /// <summary>
    /// 修改用户邮箱（同时设置 EmailConfirmed = true 并发布变更事件）
    /// </summary>
    Task<Result> ChangeEmailAsync(Guid userId, string newEmail);

    /// <summary>
    /// 修改用户手机号（同时设置 PhoneNumberConfirmed = true 并发布变更事件）
    /// </summary>
    Task<Result> ChangePhoneNumberAsync(Guid userId, string newPhoneNumber);

    /// <summary>
    /// 根据手机号查找用户（仅返回已验证手机号的用户）
    /// </summary>
    /// <param name="phoneNumber">手机号</param>
    /// <returns>用户实体或 null</returns>
    Task<User?> FindByPhoneNumberAsync(string phoneNumber);

    /// <summary>
    /// 用户自助停用账户（自行禁用，可由管理员恢复）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="reason">停用原因</param>
    Task<Result> DeactivateAccountAsync(Guid userId, string? reason = null);

    /// <summary>
    /// 用户自助删除账户（软删除，GDPR 合规）
    /// </summary>
    /// <param name="userId">用户ID</param>
    Task<Result> DeleteAccountAsync(Guid userId);

    /// <summary>
    /// 导出用户个人数据（GDPR 合规）
    /// </summary>
    /// <param name="userId">用户ID</param>
    Task<Result<PersonalDataExportDto>> ExportPersonalDataAsync(Guid userId);

    /// <summary>
    /// Export users as CSV string (admin operation)
    /// </summary>
    /// <param name="query">Optional query filter (null = export all users)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>CSV formatted string</returns>
    Task<Result<string>> ExportUsersCsvAsync(UserListQueryDto? query = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Import users from CSV data (admin operation).
    /// Creates new users; skips existing ones (by email).
    /// </summary>
    /// <param name="csvContent">CSV content with headers: UserName,Email,PhoneNumber,Password</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Import result with success/failure counts and error details</returns>
    Task<Result<UserImportResult>> ImportUsersCsvAsync(string csvContent, CancellationToken cancellationToken = default);
}

