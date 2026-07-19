namespace Tnzi.Identity.Services;

/// <summary>
/// 用户管理服务实现
/// </summary>
public class UserService : ApplicationService, IUserService
{
    private static readonly TimeSpan UserCacheExpiration = TimeSpan.FromMinutes(30);
    private const int DefaultLockoutDays = 1;

    private readonly UserManager<User> _userManager;
    private readonly RoleManager<Role> _roleManager;
    private readonly IRepository<User, Guid> _userRepository;
    private readonly IRepository<UserRole>? _userRoleRepository;
    private readonly IOrganizationService? _organizationService;
    private readonly ICurrentUser? _currentUser;
    private readonly IPasswordPolicyService? _passwordPolicyService;
    private readonly ICache? _cache;
    private readonly IUserDetailService? _userDetailService;
    private readonly IUserRoleService? _userRoleService;
    private readonly ICurrentTenant? _currentTenant;
    private readonly bool _multiTenancyEnabled;
    private readonly IFunctionAuthorizationService? _functionAuthorization;

    public UserService(
        UserManager<User> userManager,
        RoleManager<Role> roleManager,
        IRepository<User, Guid> userRepository,
        IServiceProvider serviceProvider,
        IOrganizationService? organizationService = null,
        IEventBus? eventBus = null,
        ICurrentUser? currentUser = null,
        IPasswordPolicyService? passwordPolicyService = null,
        ICache? cache = null,
        IUserDetailService? userDetailService = null,
        IUserRoleService? userRoleService = null,
        IRepository<UserRole>? userRoleRepository = null,
        ICurrentTenant? currentTenant = null,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null,
        IFunctionAuthorizationService? functionAuthorization = null)
        : base(serviceProvider)
    {
        _userManager = Check.NotNull(userManager);
        _roleManager = Check.NotNull(roleManager);
        _userRepository = Check.NotNull(userRepository);
        _organizationService = organizationService;
        _currentUser = currentUser;
        _passwordPolicyService = passwordPolicyService;
        _cache = cache;
        _userDetailService = userDetailService;
        _userRoleService = userRoleService;
        _userRoleRepository = userRoleRepository;
        _currentTenant = currentTenant;
        _multiTenancyEnabled = multiTenancyOptions?.Value.Enabled ?? false;
        _functionAuthorization = functionAuthorization;
    }

    public async Task<Result<UserDto>> CreateAsync(CreateUserDto input)
    {
        var user = input.MapTo<User>();
        if (_multiTenancyEnabled && user.TenantId == null)
        {
            user.TenantId = ResolveNewUserTenantId();
        }

        var result = await _userManager.CreateAsync(user, input.Password);
        if (!result.Succeeded)
        {
            return Fail<UserDto>(
                $"Failed to create user: {result.FormatErrors()}",
                400,
                ErrorCodes.IDENTITY_USER_CREATE_FAILED);
        }

        // 分配角色
        if (input.RoleIds != null && input.RoleIds.Any())
        {
            var assignResult = await AssignRolesAsync(user.Id, input.RoleIds);
            if (!assignResult.Succeeded)
            {
                return Fail<UserDto>(assignResult.Message ?? "Failed to assign roles", assignResult.Code ?? 400, assignResult.ErrorCode);
            }
        }

        // 发布用户注册事件（通过UserManager创建的用户也触发注册事件）
        await PublishUserRegisteredEventAsync(user);

        var userDto = await MapUserToDtoAsync(user);
        LogInformation("User created: {UserId}, UserName: {UserName}", user.Id, user.UserName ?? string.Empty);
        return Ok(userDto, "User created successfully");
    }

    public async Task<Result<UserDto>> UpdateAsync(Guid id, UpdateUserDto input)
    {
        var user = await _userManager.FindByGuidAsync(id);
        if (user == null)
        {
            return Fail<UserDto>("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        // 只更新非空字段，避免空字符串覆盖现有值
        // 只更新 User 表的核心字段（Email, PhoneNumber, OrganizationId）
        // Nickname、Avatar 等个人资料字段在 UserDetail 中更新
        if (!string.IsNullOrWhiteSpace(input.Email))
        {
            user.Email = input.Email;
        }
        if (!string.IsNullOrWhiteSpace(input.PhoneNumber))
        {
            user.PhoneNumber = input.PhoneNumber;
        }
        if (input.OrganizationId.HasValue)
        {
            user.OrganizationId = input.OrganizationId;
        }

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return Fail<UserDto>(
                $"Failed to update user: {result.FormatErrors()}",
                400,
                ErrorCodes.IDENTITY_USER_UPDATE_FAILED);
        }

        // 发布用户更新事件
        if (EventBus != null)
        {
            var updatedFields = new List<string>();
            if (input.Email != null) updatedFields.Add(nameof(User.Email));
            if (input.PhoneNumber != null) updatedFields.Add(nameof(User.PhoneNumber));
            if (input.Nickname != null) updatedFields.Add(nameof(UserDetail.Nickname));  // 在 UserDetail 中
            if (input.AvatarUrl != null || input.AvatarId.HasValue) updatedFields.Add(Metadata.IdentityConstants.UserDetailField.Avatar);  // 在 UserDetail 中
            if (input.OrganizationId.HasValue) updatedFields.Add(nameof(User.OrganizationId));

            await EventBus.PublishAsync(new UserUpdatedEvent
            {
                UserId = user.Id,
                UserName = user.UserName ?? string.Empty,
                UpdatedFields = updatedFields,
                LastModificationTime = DateTime.UtcNow,
                LastModifierId = CurrentUser?.Id
            }, cancellationToken: default);
        }

        // 更新角色
        if (input.RoleIds != null)
        {
            var currentRoles = await _userManager.GetRolesAsync(user);
            var currentRoleIds = await GetRoleIdsByNamesAsync(currentRoles);

            var rolesToRemove = currentRoleIds.Except(input.RoleIds).ToList();
            var rolesToAdd = input.RoleIds.Except(currentRoleIds).ToList();

            if (rolesToRemove.Any())
            {
                var removeResult = await RemoveRolesAsync(user.Id, rolesToRemove);
                if (!removeResult.Succeeded)
                {
                    return Fail<UserDto>(removeResult.Message ?? "Failed to remove roles", removeResult.Code ?? 400, removeResult.ErrorCode);
                }
            }
            if (rolesToAdd.Any())
            {
                var assignResult = await AssignRolesAsync(user.Id, rolesToAdd);
                if (!assignResult.Succeeded)
                {
                    return Fail<UserDto>(assignResult.Message ?? "Failed to assign roles", assignResult.Code ?? 400, assignResult.ErrorCode);
                }
            }
        }

        // 同步更新用户详情
        if (_userDetailService != null)
        {
            var detailDto = new CreateUserDetailDto();
            input.MapTo(detailDto);
            await _userDetailService.CreateOrUpdateAsync(user.Id, detailDto);
        }

        var userDto = await MapUserToDtoAsync(user);

        // 更新成功，清除缓存
        if (_cache != null)
        {
            await _cache.RemoveAsync(CacheKeys.Identity.User(user.Id));
        }

        LogInformation("User updated: {UserId}, UserName: {UserName}", user.Id, user.UserName ?? string.Empty);
        return Ok(userDto, "User updated successfully");
    }

    public async Task<Result> DeleteAsync(Guid id)
    {
        var user = await _userManager.FindByGuidAsync(id);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        // Snapshot current role IDs BEFORE deletion so the cache-invalidation
        // event has the full removed list. We map role-names → IDs via
        // RoleManager — `UserManager.GetRolesAsync` returns names only.
        // Defensive null coalesce: mocked UserManagers in tests may return
        // null from GetRolesAsync when not stubbed (real ASP.NET Identity
        // returns an empty IList<string>, never null, but Moq's loose mocks
        // default to null for reference types).
        var roleNamesBeforeDelete = await _userManager.GetRolesAsync(user) ?? Array.Empty<string>();
        var roleIdsBeforeDelete = roleNamesBeforeDelete.Count > 0
            ? await _roleManager.Roles
                .Where(r => roleNamesBeforeDelete.Contains(r.Name!))
                .Select(r => r.Id)
                .ToListAsync()
            : new List<Guid>();

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            return Fail(
                $"Failed to delete user: {result.FormatErrors()}",
                400,
                ErrorCodes.IDENTITY_USER_DELETE_FAILED);
        }

        // 清除缓存
        if (_cache != null)
        {
            var cacheKey = CacheKeys.Identity.User(id);
            await _cache.RemoveAsync(cacheKey);
        }

        LogInformation("User deleted: {UserId}, UserName: {UserName}", user.Id, user.UserName ?? string.Empty);

        // Publish role-membership removal so downstream caches drop this
        // user's entry. `UserDeleted` change-type tells audit consumers
        // this isn't an admin "unassign", it's an account removal.
        await PublishUserRolesChangedAsync(
            user,
            addedRoleIds: new List<Guid>(),
            removedRoleIds: roleIdsBeforeDelete,
            changeType: UserRolesChangeType.UserDeleted);

        return Ok("User deleted successfully");
    }

    public async Task<Result<UserDto>> GetByIdAsync(Guid id)
    {
        // 尝试从缓存获取
        if (_cache != null)
        {
            var cacheKey = CacheKeys.Identity.User(id);
            var cachedUser = await _cache.GetAsync<UserDto>(cacheKey);
            if (cachedUser != null)
            {
                return Ok(cachedUser);
            }
        }

        var user = await _userRepository
            .Where(u => u.Id == id)
            .Include(u => u.Organization)
            .FirstOrDefaultAsync();

        if (user == null)
        {
            return Fail<UserDto>("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        var userDto = await MapUserToDtoAsync(user);

        // 存入缓存（30分钟过期）
        if (_cache != null)
        {
            var cacheKey = CacheKeys.Identity.User(id);
            await _cache.SetAsync(cacheKey, userDto, UserCacheExpiration);
        }

        return Ok(userDto);
    }

    public async Task<Result<IPagedList<UserListItemDto>>> GetListAsync(UserListQueryDto query)
    {
        var queryable = _userRepository
            .Where(u => !u.IsDeleted)
            .Include(u => u.Organization)
            .AsQueryable();

        // 关键词搜索（大小写不敏感）
        if (!string.IsNullOrEmpty(query.Keyword))
        {
            var keyword = query.Keyword!.ToLower();
            // Nickname 已移到 UserDetail，这里只搜索 User 表的字段
            queryable = queryable.Where(u =>
                (u.UserName != null && u.UserName.ToLower().Contains(keyword)) ||
                (u.Email != null && u.Email.ToLower().Contains(keyword)) ||
                (u.PhoneNumber != null && u.PhoneNumber.ToLower().Contains(keyword)));
        }

        // 组织筛选
        if (query.OrganizationId.HasValue)
        {
            queryable = queryable.Where(u => u.OrganizationId == query.OrganizationId.Value);
        }

        // 锁定状态筛选
        if (query.IsLockedOut.HasValue)
        {
            if (query.IsLockedOut.Value)
            {
                queryable = queryable.Where(u => u.LockoutEnd != null && u.LockoutEnd > DateTimeOffset.UtcNow);
            }
            else
            {
                queryable = queryable.Where(u => u.LockoutEnd == null || u.LockoutEnd <= DateTimeOffset.UtcNow);
            }
        }

        // 邮箱确认状态筛选
        if (query.IsEmailConfirmed.HasValue)
        {
            queryable = queryable.Where(u => u.EmailConfirmed == query.IsEmailConfirmed.Value);
        }

        // 角色筛选
        if (query.RoleId.HasValue)
        {
            // 使用 IRepository<UserRole> 进行过滤，避免加载所有用户到内存
            if (_userRoleRepository != null)
            {
                var userIds = _userRoleRepository.Where(ur => ur.RoleId == query.RoleId.Value).Select(ur => ur.UserId);
                queryable = queryable.Where(u => userIds.Contains(u.Id));
            }
        }

        // 排序
        if (!string.IsNullOrEmpty(query.SortBy))
        {
            if (string.Equals(query.SortBy, "username", StringComparison.OrdinalIgnoreCase))
            {
                queryable = query.SortDescending
                    ? queryable.OrderByDescending(u => u.UserName)
                    : queryable.OrderBy(u => u.UserName);
            }
            else if (string.Equals(query.SortBy, "email", StringComparison.OrdinalIgnoreCase))
            {
                queryable = query.SortDescending
                    ? queryable.OrderByDescending(u => u.Email)
                    : queryable.OrderBy(u => u.Email);
            }
            else if (string.Equals(query.SortBy, "creationtime", StringComparison.OrdinalIgnoreCase))
            {
                queryable = query.SortDescending
                    ? queryable.OrderByDescending(u => u.CreationTime)
                    : queryable.OrderBy(u => u.CreationTime);
            }
            else
            {
                queryable = queryable.OrderByDescending(u => u.CreationTime);
            }
        }
        else
        {
            queryable = queryable.OrderByDescending(u => u.CreationTime);
        }

        // 使用 ProjectTo 完成基础映射（轻量版，不加载 UserDetail）
        var paged = await queryable
            .ProjectTo<User, UserListItemDto>()
            .CreateAsync(query);

        // 批量获取用户角色，消除 N+1
        if (_userRoleService != null && paged.Items.Any())
        {
            var userIds = paged.Items.Select(u => u.Id).ToList();
            var userRolesMap = await _userRoleService.GetUserRolesAsync(userIds);

            foreach (var userDto in paged.Items)
            {
                if (userRolesMap.TryGetValue(userDto.Id, out var roles))
                {
                    userDto.Roles = roles.ToList();
                }
            }
        }

        return Ok(paged);
    }

    public async Task<Result> EnableAsync(Guid id)
    {
        var user = await _userManager.FindByGuidAsync(id);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        await _userManager.SetLockoutEnabledAsync(user, false);
        await _userManager.SetLockoutEndDateAsync(user, null);

        // 发布用户启用事件
        if (EventBus != null)
        {
            await EventBus.PublishAsync(new UserEnabledEvent
            {
                UserId = user.Id,
                UserName = user.UserName ?? string.Empty,
                EnabledTime = DateTime.UtcNow,
                EnabledBy = CurrentUser?.Id
            }, cancellationToken: default);
        }

        // 清除缓存
        if (_cache != null)
        {
            await _cache.RemoveAsync(CacheKeys.Identity.User(user.Id));
        }

        LogInformation("User enabled: {UserId}, UserName: {UserName}", user.Id, user.UserName ?? string.Empty);
        return Ok("User enabled successfully");
    }

    public async Task<Result> DisableAsync(Guid id, string? reason = null)
    {
        var user = await _userManager.FindByGuidAsync(id);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        // 禁用用户：锁定到未来某个时间（如100年后）
        await _userManager.SetLockoutEnabledAsync(user, true);
        await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));

        // 发布用户禁用事件
        if (EventBus != null)
        {
            await EventBus.PublishAsync(new UserDisabledEvent
            {
                UserId = user.Id,
                UserName = user.UserName ?? string.Empty,
                DisabledTime = DateTime.UtcNow,
                DisabledBy = CurrentUser?.Id,
                DisableReason = reason
            }, cancellationToken: default);
        }

        // 清除缓存
        if (_cache != null)
        {
            await _cache.RemoveAsync(CacheKeys.Identity.User(user.Id));
        }

        LogInformation("User disabled: {UserId}, UserName: {UserName}, Reason: {Reason}", user.Id, user.UserName ?? string.Empty, reason ?? string.Empty);
        return Ok("User disabled successfully");
    }

    public async Task<Result> LockAsync(Guid id, DateTimeOffset? lockoutEnd = null, string? reason = null)
    {
        var user = await _userManager.FindByGuidAsync(id);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        var lockoutEndDate = lockoutEnd ?? DateTimeOffset.UtcNow.AddDays(DefaultLockoutDays);

        await _userManager.SetLockoutEnabledAsync(user, true);
        await _userManager.SetLockoutEndDateAsync(user, lockoutEndDate);

        // 发布用户锁定事件
        if (EventBus != null)
        {
            await EventBus.PublishAsync(new UserLockedEvent
            {
                UserId = user.Id,
                UserName = user.UserName ?? string.Empty,
                LockedTime = DateTime.UtcNow,
                LockedBy = CurrentUser?.Id,
                LockReason = reason
            }, cancellationToken: default);
        }

        // 清除缓存
        if (_cache != null)
        {
            await _cache.RemoveAsync(CacheKeys.Identity.User(user.Id));
        }

        LogInformation("User locked: {UserId}, UserName: {UserName}, LockoutEnd: {LockoutEnd}, Reason: {Reason}",
            user.Id, user.UserName ?? string.Empty, lockoutEndDate, reason ?? string.Empty);
        return Ok("User locked successfully");
    }

    public async Task<Result> UnlockAsync(Guid id)
    {
        var user = await _userManager.FindByGuidAsync(id);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        await _userManager.SetLockoutEndDateAsync(user, null);

        // 发布用户解锁事件
        if (EventBus != null)
        {
            await EventBus.PublishAsync(new UserUnlockedEvent
            {
                UserId = user.Id,
                UserName = user.UserName ?? string.Empty,
                UnlockedTime = DateTime.UtcNow,
                UnlockedBy = CurrentUser?.Id
            }, cancellationToken: default);
        }

        // 清除缓存
        if (_cache != null)
        {
            await _cache.RemoveAsync(CacheKeys.Identity.User(user.Id));
        }

        LogInformation("User unlocked: {UserId}, UserName: {UserName}", user.Id, user.UserName ?? string.Empty);
        return Ok("User unlocked successfully");
    }

    public async Task<Result<IEnumerable<UserListItemDto>>> CreateManyAsync(IEnumerable<CreateUserDto> inputs)
    {
        var inputList = inputs.ToList();
        var results = new List<UserListItemDto>();

        // 由于UserManager.CreateAsync需要逐个处理（密码哈希、验证等），
        // 我们仍然需要循环调用，但可以优化后续的数据库操作
        // 使用事务确保原子性（如果支持）
        foreach (var input in inputList)
        {
            var result = await CreateAsync(input);
            if (!result.Succeeded)
            {
                return Fail<IEnumerable<UserListItemDto>>(
                    result.Message ?? "Failed to create user",
                    result.Code ?? 400,
                    result.ErrorCode);
            }
            // 显式映射为 UserListItemDto，避免序列化时泄露 UserDto 额外字段
            results.Add(result.Data!.MapTo<UserListItemDto>());
        }

        LogInformation("Batch created {Count} users", results.Count);
        return Ok<IEnumerable<UserListItemDto>>(results, $"Successfully created {results.Count} users");
    }

    public async Task<Result<IEnumerable<UserListItemDto>>> UpdateManyAsync(IEnumerable<(Guid Id, UpdateUserDto Dto)> inputs)
    {
        var inputList = inputs.ToList();
        var results = new List<UserListItemDto>();

        // 由于UserManager.UpdateAsync需要逐个处理（验证、事件触发等），
        // 我们仍然需要循环调用，但可以优化后续的数据库操作
        foreach (var (id, dto) in inputList)
        {
            var result = await UpdateAsync(id, dto);
            if (!result.Succeeded)
            {
                return Fail<IEnumerable<UserListItemDto>>(
                    result.Message ?? $"Failed to update user {id}",
                    result.Code ?? 400,
                    result.ErrorCode);
            }
            // 显式映射为 UserListItemDto，避免序列化时泄露 UserDto 额外字段
            results.Add(result.Data!.MapTo<UserListItemDto>());
        }

        LogInformation("Batch updated {Count} users", results.Count);
        return Ok<IEnumerable<UserListItemDto>>(results, $"Successfully updated {results.Count} users");
    }

    public async Task<Result> DeleteManyAsync(IEnumerable<Guid> ids)
    {
        var idList = ids.ToList();
        if (!idList.Any())
        {
            return Ok("No users to delete");
        }

        // 批量查找用户（使用一次查询而不是循环）
        var users = await _userRepository
            .Where(u => idList.Contains(u.Id))
            .ToListAsync();

        // 批量删除（使用UserManager的DeleteAsync，因为它会触发相关事件和清理）
        // 注意：UserManager没有批量删除方法，所以仍然需要循环
        // 但我们已经优化了批量查询
        foreach (var user in users)
        {
            // Snapshot roles BEFORE delete so the cache-invalidation event
            // covers each user's full role set (see DeleteAsync for context).
            var roleNamesBeforeDelete = await _userManager.GetRolesAsync(user);
            var roleIdsBeforeDelete = roleNamesBeforeDelete.Count > 0
                ? await _roleManager.Roles
                    .Where(r => roleNamesBeforeDelete.Contains(r.Name!))
                    .Select(r => r.Id)
                    .ToListAsync()
                : new List<Guid>();

            var result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded)
            {
                return Fail(
                    $"Failed to delete user {user.Id}: {result.FormatErrors()}",
                    400,
                    ErrorCodes.IDENTITY_USER_DELETE_FAILED);
            }

            // 清除缓存
            if (_cache != null)
            {
                var cacheKey = CacheKeys.Identity.User(user.Id);
                await _cache.RemoveAsync(cacheKey);
            }

            // Publish per-user (consumers expect one event per affected user).
            await PublishUserRolesChangedAsync(
                user,
                addedRoleIds: new List<Guid>(),
                removedRoleIds: roleIdsBeforeDelete,
                changeType: UserRolesChangeType.UserDeleted);
        }

        LogInformation("Batch deleted {Count} users", users.Count);
        return Ok($"Successfully deleted {users.Count} users");
    }

    public async Task<Result> AssignRolesAsync(Guid userId, IEnumerable<Guid> roleIds)
    {
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        var idList = roleIds.ToList();
        var roles = await _roleManager.Roles.Where(r => idList.Contains(r.Id)).ToListAsync();
        if (roles.Count != idList.Distinct().Count())
        {
            return Fail("Some roles were not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        var membershipViolation = await GetRoleMembershipViolationAsync(roles);
        if (membershipViolation != null)
        {
            return Fail(membershipViolation, 403, ErrorCodes.FORBIDDEN);
        }

        var result = await _userManager.AddToRolesAsync(user, roles.Select(r => r.Name!));
        if (!result.Succeeded)
        {
            return Fail(
                $"Failed to assign roles: {result.FormatErrors()}",
                400,
                ErrorCodes.IDENTITY_ROLE_ASSIGN_FAILED);
        }

        LogInformation("Roles assigned to user: {UserId}, Roles: {RoleNames}",
            userId, string.Join(", ", roles.Select(r => r.Name)));

        // Tell downstream consumers (Authorization cache, audit log) the
        // user's role set changed. Without this signal the Authorization
        // module's FunctionAuthCache (30 min TTL) would keep handing out
        // permissions derived from the old role list.
        await PublishUserRolesChangedAsync(
            user,
            addedRoleIds: roles.Select(r => r.Id).ToList(),
            removedRoleIds: new List<Guid>(),
            changeType: UserRolesChangeType.Assigned);

        return Ok("Roles assigned successfully");
    }

    public async Task<Result> RemoveRolesAsync(Guid userId, IEnumerable<Guid> roleIds)
    {
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        var idList = roleIds.ToList();
        var roles = await _roleManager.Roles.Where(r => idList.Contains(r.Id)).ToListAsync();
        if (roles.Count != idList.Distinct().Count())
        {
            return Fail("Some roles were not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        // 摘除成员与授予成员同受支配约束——弱管理员把用户从强角色里摘出去
        // 同样是越权干预(变相削权/锁死他人访问)。
        var membershipViolation = await GetRoleMembershipViolationAsync(roles);
        if (membershipViolation != null)
        {
            return Fail(membershipViolation, 403, ErrorCodes.FORBIDDEN);
        }

        var result = await _userManager.RemoveFromRolesAsync(user, roles.Select(r => r.Name!));
        if (!result.Succeeded)
        {
            return Fail(
                $"Failed to remove roles: {result.FormatErrors()}",
                400,
                ErrorCodes.IDENTITY_ROLE_REMOVE_FAILED);
        }

        LogInformation("Roles removed from user: {UserId}, Roles: {RoleNames}",
            userId, string.Join(", ", roles.Select(r => r.Name)));

        // Critical: removal must publish (more so than assignment) — a stale
        // cache after a revocation is a permission-retention security gap.
        await PublishUserRolesChangedAsync(
            user,
            addedRoleIds: new List<Guid>(),
            removedRoleIds: roles.Select(r => r.Id).ToList(),
            changeType: UserRolesChangeType.Removed);

        return Ok("Roles removed successfully");
    }

    /// <summary>
    /// Publish <see cref="UserRolesChangedEvent"/>. Auxiliary — failures here
    /// must not break the main role-change flow (matches the framework's
    /// event-handler convention: catch + log warning, never bubble).
    /// </summary>
    private async Task PublishUserRolesChangedAsync(
        User user,
        List<Guid> addedRoleIds,
        List<Guid> removedRoleIds,
        UserRolesChangeType changeType)
    {
        if (EventBus == null) return;
        try
        {
            await EventBus.PublishAsync(new UserRolesChangedEvent
            {
                UserId = user.Id,
                UserName = user.UserName ?? string.Empty,
                AddedRoleIds = addedRoleIds,
                RemovedRoleIds = removedRoleIds,
                ChangeType = changeType,
                ChangedTime = DateTime.UtcNow,
                ChangedBy = CurrentUser?.Id,
            });
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex,
                "Failed to publish UserRolesChangedEvent for user {UserId}", user.Id);
        }
    }

    /// <summary>
    /// 角色成员变更的委托护栏。非超管调用者仅能变更自己支配的角色的成员
    /// (支配语义由 Authorization 模块的 CanManageRoleAsync 提供:权限集包含
    /// 且非超管配置角色)。允许时返回 null,越界返回英文错误消息。
    /// Authorization 模块未加载(_functionAuthorization null)或无用户上下文
    /// (系统/播种路径与单元测试)时整体跳过,保持旧行为。
    /// </summary>
    private async Task<string?> GetRoleMembershipViolationAsync(IReadOnlyCollection<Role> roles)
    {
        if (_functionAuthorization == null) return null;

        var grantorId = CurrentUser?.Id;
        if (grantorId == null || grantorId == Guid.Empty) return null;
        if (await _functionAuthorization.IsSuperAdminAsync(grantorId.Value)) return null;

        foreach (var role in roles)
        {
            if (!await _functionAuthorization.CanManageRoleAsync(grantorId.Value, role.Id))
            {
                return $"You cannot change membership of role '{role.Name}': " +
                       "its permission set is not contained in yours, or it is a super-admin role.";
            }
        }

        return null;
    }

    public async Task<Result<UserStatisticsDto>> GetStatisticsAsync(Guid? organizationId = null, Guid? roleId = null)
    {
        var query = _userRepository.Where(u => !u.IsDeleted);

        if (organizationId.HasValue)
        {
            query = query.Where(u => u.OrganizationId == organizationId.Value);
        }

        var totalUsers = await query.CountAsync();
        var activeUsers = await query
            .Where(u => u.LockoutEnd == null || u.LockoutEnd <= DateTimeOffset.UtcNow)
            .CountAsync();
        var lockedUsers = await query
            .Where(u => u.LockoutEnd != null && u.LockoutEnd > DateTimeOffset.UtcNow)
            .CountAsync();

        // 统计组织用户数：如果指定了组织ID，则等于总用户数；否则统计所有有组织的用户数
        int usersByOrganization;
        if (organizationId.HasValue)
        {
            usersByOrganization = totalUsers; // 已过滤到指定组织，所以等于总用户数
        }
        else
        {
            // 统计所有有组织的用户数
            usersByOrganization = await _userRepository
                .Where(u => !u.IsDeleted && u.OrganizationId != null)
                .CountAsync();
        }

        var usersByRole = 0;
        if (roleId.HasValue)
        {
            if (_userRoleRepository != null)
            {
                usersByRole = await _userRoleRepository.CountAsync(ur => ur.RoleId == roleId.Value);
            }
        }

        var recentRegistrations = await query
            .Where(u => u.CreationTime >= DateTime.UtcNow.AddDays(-7))
            .CountAsync();

        var statistics = new UserStatisticsDto
        {
            TotalUsers = totalUsers,
            ActiveUsers = activeUsers,
            LockedUsers = lockedUsers,
            UsersByOrganization = usersByOrganization,
            UsersByRole = usersByRole,
            RecentRegistrations = recentRegistrations
        };

        return Ok(statistics);
    }

    public async Task<Result> ChangeEmailAsync(Guid userId, string newEmail)
    {
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        var oldEmail = user.Email;

        // 使用 UserManager 设置邮箱（会处理 NormalizedEmail）
        var setResult = await _userManager.SetEmailAsync(user, newEmail);
        if (!setResult.Succeeded)
        {
            return Fail($"Failed to change email: {setResult.FormatErrors()}", 400, ErrorCodes.IDENTITY_USER_UPDATE_FAILED);
        }

        // 已通过验证码验证，直接确认邮箱
        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        var confirmResult = await _userManager.ConfirmEmailAsync(user, token);
        if (!confirmResult.Succeeded)
        {
            return Fail($"Failed to confirm email: {confirmResult.FormatErrors()}", 400, ErrorCodes.IDENTITY_USER_UPDATE_FAILED);
        }

        // 清除缓存
        if (_cache != null)
        {
            await _cache.RemoveAsync(CacheKeys.Identity.User(userId));
        }

        // 发布邮箱变更事件
        if (EventBus != null)
        {
            await EventBus.PublishAsync(new UserEmailChangedEvent
            {
                UserId = userId,
                UserName = user.UserName ?? string.Empty,
                OldEmail = oldEmail,
                NewEmail = newEmail,
                ChangedTime = DateTime.UtcNow
            });
        }

        return Ok();
    }

    public async Task<Result> ChangePhoneNumberAsync(Guid userId, string newPhoneNumber)
    {
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        var oldPhoneNumber = user.PhoneNumber;

        // 使用 UserManager 设置手机号
        var token = await _userManager.GenerateChangePhoneNumberTokenAsync(user, newPhoneNumber);
        var changeResult = await _userManager.ChangePhoneNumberAsync(user, newPhoneNumber, token);
        if (!changeResult.Succeeded)
        {
            return Fail($"Failed to change phone number: {changeResult.FormatErrors()}", 400, ErrorCodes.IDENTITY_USER_UPDATE_FAILED);
        }

        // ChangePhoneNumberAsync 已自动设置 PhoneNumberConfirmed = true

        // 清除缓存
        if (_cache != null)
        {
            await _cache.RemoveAsync(CacheKeys.Identity.User(userId));
        }

        // 发布手机号变更事件
        if (EventBus != null)
        {
            await EventBus.PublishAsync(new UserPhoneChangedEvent
            {
                UserId = userId,
                UserName = user.UserName ?? string.Empty,
                OldPhoneNumber = oldPhoneNumber,
                NewPhoneNumber = newPhoneNumber,
                ChangedTime = DateTime.UtcNow
            });
        }

        return Ok();
    }

    public async Task<User?> FindByPhoneNumberAsync(string phoneNumber)
    {
        if (string.IsNullOrEmpty(phoneNumber))
            return null;

        return await _userManager.Users
            .FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber && u.PhoneNumberConfirmed);
    }

    /// <summary>
    /// 映射用户实体到DTO
    /// </summary>
    private async Task<UserDto> MapUserToDtoAsync(User user)
    {
        var userDto = user.MapTo<UserDto>();
        userDto.Roles = (await _userManager.GetRolesAsync(user)).ToList();

        // 加载用户详情并合并到 DTO
        // Nickname、Avatar 等个人资料信息已从 User 移到 UserDetail
        if (_userDetailService != null)
        {
            var detailResult = await _userDetailService.GetByUserIdAsync(user.Id);
            if (detailResult.Succeeded && detailResult.Data != null)
            {
                var detail = detailResult.Data;
                userDto.FirstName = detail.FirstName;
                userDto.LastName = detail.LastName;
                userDto.Nickname = detail.Nickname;
                userDto.AvatarId = detail.AvatarId;
                userDto.Avatar = detail.AvatarUrl;  // 外部头像 URL 作为备用
                userDto.Gender = detail.Gender;
                userDto.Birthday = detail.Birthday;
                userDto.Bio = detail.Bio;
                userDto.Address = detail.Address;
                userDto.Website = detail.Website;
            }
        }

        return userDto;
    }

    /// <summary>
    /// 根据角色名称获取角色ID列表
    /// </summary>
    private async Task<List<Guid>> GetRoleIdsByNamesAsync(IEnumerable<string> roleNames)
    {
        var names = roleNames.ToList();
        if (!names.Any()) return new List<Guid>();

        return await _roleManager.Roles
            .Where(r => names.Contains(r.Name!))
            .Select(r => r.Id)
            .ToListAsync();
    }

    #region Private Methods

    /// <summary>
    /// 发布用户注册事件
    /// </summary>
    /// <param name="user">注册的用户</param>
    private async Task PublishUserRegisteredEventAsync(User user)
    {
        if (EventBus != null)
        {
            await EventBus.PublishAsync(new UserRegisteredEvent
            {
                UserId = user.Id,
                UserName = user.UserName ?? string.Empty,
                Email = user.Email,
                RegistrationTime = DateTime.UtcNow
            }, cancellationToken: default);
        }
    }

    #endregion

    #region 用户自助账户管理

    public async Task<Result> DeactivateAccountAsync(Guid userId, string? reason = null)
    {
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        // 使用 lockout 机制禁用用户
        await _userManager.SetLockoutEnabledAsync(user, true);
        await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));

        // 发布账户停用事件
        if (EventBus != null)
        {
            await EventBus.PublishAsync(new UserAccountDeactivatedEvent
            {
                UserId = user.Id,
                UserName = user.UserName ?? string.Empty,
                DeactivatedTime = DateTime.UtcNow,
                Reason = reason
            }, cancellationToken: default);
        }

        // 清除缓存
        if (_cache != null)
        {
            await _cache.RemoveAsync(CacheKeys.Identity.User(user.Id));
        }

        LogInformation("User account deactivated: {UserId}, UserName: {UserName}", user.Id, user.UserName ?? string.Empty);
        return Ok("Account deactivated successfully");
    }

    public async Task<Result> DeleteAccountAsync(Guid userId)
    {
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        var userName = user.UserName ?? string.Empty;

        // 软删除
        user.IsDeleted = true;
        user.LastModificationTime = DateTime.UtcNow;

        // 锁定账户
        await _userManager.SetLockoutEnabledAsync(user, true);
        await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return Fail(result.FormatErrors(), 400, ErrorCodes.IDENTITY_USER_DELETE_FAILED);
        }

        // 发布账户删除事件
        if (EventBus != null)
        {
            await EventBus.PublishAsync(new UserAccountDeletedEvent
            {
                UserId = userId,
                UserName = userName,
                DeletedTime = DateTime.UtcNow
            }, cancellationToken: default);
        }

        // 清除缓存
        if (_cache != null)
        {
            await _cache.RemoveAsync(CacheKeys.Identity.User(userId));
        }

        LogInformation("User account self-deleted: {UserId}, UserName: {UserName}", userId, userName);
        return Ok("Account deleted successfully");
    }

    public async Task<Result<PersonalDataExportDto>> ExportPersonalDataAsync(Guid userId)
    {
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return Fail<PersonalDataExportDto>("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        var export = new PersonalDataExportDto
        {
            UserId = user.Id,
            UserName = user.UserName ?? string.Empty,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            CreationTime = user.CreationTime,
            LastModificationTime = user.LastModificationTime,
            TwoFactorEnabled = user.TwoFactorEnabled,
            EmailConfirmed = user.EmailConfirmed,
            PhoneNumberConfirmed = user.PhoneNumberConfirmed,
            ExportedAt = DateTime.UtcNow
        };

        // 获取角色
        var roles = await _userManager.GetRolesAsync(user);
        export.Roles = roles.ToList();

        // 获取组织名称
        if (user.OrganizationId.HasValue && _organizationService != null)
        {
            var orgResult = await _organizationService.GetByIdAsync(user.OrganizationId.Value);
            if (orgResult.Succeeded && orgResult.Data != null)
            {
                export.OrganizationName = orgResult.Data.Name;
            }
        }

        // 获取外部登录提供者
        var logins = await _userManager.GetLoginsAsync(user);
        export.LinkedProviders = logins.Select(l => l.LoginProvider).ToList();

        // 发布数据导出事件（审计目的）
        if (EventBus != null)
        {
            await EventBus.PublishAsync(new PersonalDataExportedEvent
            {
                UserId = user.Id,
                UserName = user.UserName ?? string.Empty,
                ExportedTime = DateTime.UtcNow
            }, cancellationToken: default);
        }

        LogInformation("Personal data exported for user: {UserId}", userId);
        return Ok(export);
    }

    /// <summary>
    /// Export users as CSV string
    /// </summary>
    public async Task<Result<string>> ExportUsersCsvAsync(UserListQueryDto? query = null, CancellationToken cancellationToken = default)
    {
        var queryable = _userRepository.Where(u => !u.IsDeleted);

        if (query != null)
        {
            if (!string.IsNullOrEmpty(query.Keyword))
            {
                var keyword = query.Keyword.ToLower();
                queryable = queryable.Where(u =>
                    (u.UserName != null && u.UserName.ToLower().Contains(keyword)) ||
                    (u.Email != null && u.Email.ToLower().Contains(keyword)) ||
                    (u.PhoneNumber != null && u.PhoneNumber.ToLower().Contains(keyword)));
            }

            if (query.OrganizationId.HasValue)
            {
                queryable = queryable.Where(u => u.OrganizationId == query.OrganizationId.Value);
            }
        }

        var users = await queryable
            .OrderBy(u => u.CreationTime)
            .Take(50000) // Export safety limit
            .ToListAsync(cancellationToken);

        // 单元格转义统一走核心 CsvBuilder(含公式注入防护),日期保持 ISO 8601 往返格式
        var csv = new CsvBuilder();
        csv.AppendRow("Id", "UserName", "Email", "PhoneNumber", "IsActive", "EmailConfirmed", "PhoneNumberConfirmed", "CreationTime", "LastModificationTime");

        foreach (var user in users)
        {
            var isActive = user.LockoutEnd == null || user.LockoutEnd <= DateTimeOffset.UtcNow;
            csv.AppendRow(user.Id, user.UserName, user.Email, user.PhoneNumber,
                isActive, user.EmailConfirmed, user.PhoneNumberConfirmed,
                user.CreationTime, user.LastModificationTime);
        }

        LogInformation("Exported {Count} users to CSV", users.Count);
        return Ok<string>(csv.ToString());
    }

    /// <summary>
    /// Import users from CSV data
    /// </summary>
    public async Task<Result<UserImportResult>> ImportUsersCsvAsync(string csvContent, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(csvContent))
            return Fail<UserImportResult>("CSV content cannot be empty", 400, ErrorCodes.VALIDATION_ERROR);

        var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim('\r'))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (lines.Count < 2)
            return Fail<UserImportResult>("CSV must contain a header row and at least one data row", 400, ErrorCodes.VALIDATION_ERROR);

        // Parse header (case-insensitive)
        var header = lines[0].Split(',').Select(h => h.Trim().ToLowerInvariant()).ToArray();
        var userNameIdx = Array.IndexOf(header, "username");
        var emailIdx = Array.IndexOf(header, "email");
        var phoneIdx = Array.IndexOf(header, "phonenumber");
        var passwordIdx = Array.IndexOf(header, "password");

        if (userNameIdx < 0 || emailIdx < 0 || passwordIdx < 0)
            return Fail<UserImportResult>("CSV header must contain at least: UserName, Email, Password", 400, ErrorCodes.VALIDATION_ERROR);

        var result = new UserImportResult { TotalRows = lines.Count - 1 };

        for (var i = 1; i < lines.Count; i++)
        {
            var fields = ParseCsvLine(lines[i]);
            var rowNumber = i + 1;

            try
            {
                if (fields.Length <= Math.Max(Math.Max(userNameIdx, emailIdx), passwordIdx))
                {
                    result.Errors[rowNumber] = "Insufficient columns";
                    result.FailedCount++;
                    continue;
                }

                var userName = fields[userNameIdx].Trim();
                var email = fields[emailIdx].Trim();
                var password = fields[passwordIdx].Trim();

                if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
                {
                    result.Errors[rowNumber] = "UserName, Email, and Password are required";
                    result.FailedCount++;
                    continue;
                }

                // Check if user already exists
                var existingUser = await _userManager.FindByEmailAsync(email);
                if (existingUser != null)
                {
                    result.SkippedCount++;
                    continue;
                }

                var user = new User
                {
                    UserName = userName,
                    Email = email,
                    PhoneNumber = phoneIdx >= 0 && fields.Length > phoneIdx ? fields[phoneIdx].Trim() : null,
                    EmailConfirmed = true, // Auto-confirm for imported users
                    CreationTime = DateTime.UtcNow,
                    TenantId = ResolveNewUserTenantId()
                };

                var createResult = await _userManager.CreateAsync(user, password);
                if (createResult.Succeeded)
                {
                    result.SuccessCount++;
                }
                else
                {
                    result.Errors[rowNumber] = createResult.FormatErrors();
                    result.FailedCount++;
                }
            }
            catch (Exception ex)
            {
                result.Errors[rowNumber] = ex.Message;
                result.FailedCount++;
            }
        }

        LogInformation("User import completed: {Success} success, {Failed} failed, {Skipped} skipped out of {Total} rows",
            result.SuccessCount, result.FailedCount, result.SkippedCount, result.TotalRows);
        return Ok(result);
    }

    /// <summary>
    /// Parse a CSV line respecting quoted fields
    /// </summary>
    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++; // Skip escaped quote
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(ch);
                }
            }
            else
            {
                if (ch == '"')
                {
                    inQuotes = true;
                }
                else if (ch == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(ch);
                }
            }
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }

    private Guid? ResolveNewUserTenantId()
    {
        if (!_multiTenancyEnabled)
        {
            return null;
        }

        return _currentTenant?.Id ?? _currentUser?.TenantId ?? CurrentUser?.TenantId;
    }

    #endregion
}
