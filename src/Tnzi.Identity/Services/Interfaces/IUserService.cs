namespace Tnzi.Identity.Services;

/// <summary>
/// 用户管理服务接口
/// </summary>
public interface IUserService
{
    /// <summary>
    /// 创建用户
    /// </summary>
    /// <remarks>
    /// 账号建好（含角色与 <see cref="CreateUserDto.Nickname"/> 等资料栏）之后发布 <see cref="Events.UserRegisteredEvent"/>。
    /// </remarks>
    Task<Result<UserDto>> CreateAsync(CreateUserDto input);

    /// <summary>
    /// 为一份邀请建号：重名、组织、角色校验与分配、资料写入都与 <see cref="CreateAsync"/> 走同一条路，
    /// 只是<b>不发布</b> <see cref="Events.UserRegisteredEvent"/>。
    /// </summary>
    /// <remarks>
    /// 被邀请的账号此刻没有密码、登录不进去，此时宣布「已注册」是假的：订阅者（例如欢迎邮件）会告诉对方
    /// 「账号已可用」。注册事件由 <see cref="IInvitationService"/> 在接受邀请、账号真正可用的那一刻发布。
    /// </remarks>
    Task<Result<UserDto>> CreateForInvitationAsync(CreateUserDto input);

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
    /// 锁定用户。<paramref name="lockoutEnd"/> 为 null 时锁到管理员解锁为止（一百年后，与停用同一个值）。
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
    /// 由管理员直接把某个用户的邮箱 / 手机号标记为已确认。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★★★ <strong>这是一个显式动作，不是 <c>UpdateAsync</c> 的副作用。</strong>
    /// 改地址一律清确认位（见 <c>UpdateAsync</c>），因为「谁都能给自己盖一个已验证的章」
    /// 正是要防的那件事。但企业场景里「HR 导入的地址就是算数的」是真实需求，堵死它
    /// 只会逼出更糟的绕法（直接改库）。所以把它做成一个单独的、需要
    /// <c>user.update</c> 权限、会进审计的动作 —— 谁在什么时候替谁盖了章，查得到。
    /// </para>
    /// <para>
    /// 只置位、不改地址：要改地址请先 <c>UpdateAsync</c>，再决定要不要盖这个章。
    /// </para>
    /// </remarks>
    /// <param name="userId">目标用户。</param>
    /// <param name="confirmEmail">是否把邮箱标记为已确认（<c>null</c> = 不动）。</param>
    /// <param name="confirmPhoneNumber">是否把手机号标记为已确认（<c>null</c> = 不动）。</param>
    Task<Result> ConfirmContactAsync(Guid userId, bool? confirmEmail, bool? confirmPhoneNumber);

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
    /// 用户自助注销账户：锁定、把账号行标为软删除、撤销全部会话，发布 <see cref="Events.UserDeletedEvent"/>。
    /// </summary>
    /// <remarks>
    /// 不抹除资料（<c>UserDetail</c>）、第三方登录关联与登录日志：抹除与否是消费应用的合规决定，需要时订阅
    /// <see cref="Events.UserDeletedEvent"/> 自行处理。
    /// </remarks>
    /// <param name="userId">用户ID</param>
    Task<Result> DeleteAccountAsync(Guid userId);

    /// <summary>
    /// 导出用户自己的账号数据：账号字段、角色、所属组织名与已关联的第三方登录提供商（不含资料与登录历史）。
    /// </summary>
    /// <param name="userId">用户ID</param>
    Task<Result<PersonalDataExportDto>> ExportPersonalDataAsync(Guid userId);

    /// <summary>
    /// Export users as CSV string (admin operation)
    /// </summary>
    /// <remarks>
    /// 筛选与排序与 <see cref="GetListAsync"/> 相同（分页参数被忽略，导出整个结果集）。
    /// 命中行数超过导出上限（50000）时返回 400 <c>IDENTITY_USER_EXPORT_TOO_LARGE</c>，从不截断。
    /// </remarks>
    /// <param name="query">Optional query filter, the same one the list takes (null = export all users)</param>
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

