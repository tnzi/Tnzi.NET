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
    private readonly ICache? _cache;
    private readonly IUserDetailService? _userDetailService;
    private readonly IUserRoleService? _userRoleService;
    private readonly ICurrentTenant? _currentTenant;
    private readonly bool _multiTenancyEnabled;
    private readonly IFunctionAuthorizationService? _functionAuthorization;
    private readonly ISessionRevocationService? _sessionRevocation;
    private readonly IReadOnlyList<IUserUsageProvider> _usageProviders;
    private readonly IOptionsMonitor<IdentityOptions>? _identityOptions;

    public UserService(
        UserManager<User> userManager,
        RoleManager<Role> roleManager,
        IRepository<User, Guid> userRepository,
        IServiceProvider serviceProvider,
        IOrganizationService? organizationService = null,
        IEventBus? eventBus = null,
        ICurrentUser? currentUser = null,
        ICache? cache = null,
        IUserDetailService? userDetailService = null,
        IUserRoleService? userRoleService = null,
        IRepository<UserRole>? userRoleRepository = null,
        ICurrentTenant? currentTenant = null,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null,
        IFunctionAuthorizationService? functionAuthorization = null,
        ISessionRevocationService? sessionRevocation = null,
        IEnumerable<IUserUsageProvider>? usageProviders = null,
        IOptionsMonitor<IdentityOptions>? identityOptions = null)
        : base(serviceProvider)
    {
        _userManager = Check.NotNull(userManager);
        _roleManager = Check.NotNull(roleManager);
        _userRepository = Check.NotNull(userRepository);
        _organizationService = organizationService;
        _currentUser = currentUser;
        _cache = cache;
        _userDetailService = userDetailService;
        _userRoleService = userRoleService;
        _userRoleRepository = userRoleRepository;
        _currentTenant = currentTenant;
        _multiTenancyEnabled = multiTenancyOptions?.Value.Enabled ?? false;
        _functionAuthorization = functionAuthorization;
        _sessionRevocation = sessionRevocation;
        _usageProviders = usageProviders?.ToList() ?? [];
        _identityOptions = identityOptions;
    }

    /// <summary>
    /// 用邮箱作用户名的开关。未注入选项时取选项类的默认值（开启），与未配置的部署同一口径：
    /// 不能因为少了一个可选形参就换成另一套规则。
    /// </summary>
    private bool UseEmailAsUserName => (_identityOptions?.CurrentValue ?? new IdentityOptions()).SignIn.UseEmailAsUserName;

    /// <summary>
    /// 删除之前问一圈「这个账号还有别的记录以它为主体吗」。任一 <see cref="IUserUsageProvider"/>
    /// 答「在用」即返回 409，且此时**一个字节都还没改**。
    /// </summary>
    /// <remarks>
    /// 消费应用的员工档案 / 警员台账 / 薪酬主数据都以可空 <c>UserId</c> 松引用账号且刻意不建外键，
    /// 所以数据库既不会级联也不会拒绝；没有这一问，框架自带的删除端点会把它们留成指向已删账号的
    /// 悬空行，而列表、状态、接口返回全都正常。无实现时返回 null，删除路径与引入本检查之前逐字相同。
    /// </remarks>
    private async Task<Result?> RefuseIfInUseAsync(User user)
    {
        foreach (var provider in _usageProviders)
        {
            var usage = await provider.FindUsageAsync(user.Id);
            if (usage == null)
            {
                continue;
            }

            LogInformation(
                "Refused to delete user {UserId} ({UserName}): still in use per {Provider}: {Detail}",
                user.Id, user.UserName ?? string.Empty, provider.GetType().Name, usage.Detail);
            return Fail(
                $"User '{user.UserName}' cannot be deleted: {usage.Detail}",
                409,
                ErrorCodes.IDENTITY_USER_IN_USE);
        }

        return null;
    }

    /// <summary>
    /// 账号已经删掉、会话已撤销之后的通知。拿这个账号当主体的领域记录从这里善后。
    /// </summary>
    private async Task PublishUserDeletedAsync(User user)
    {
        if (EventBus == null)
        {
            return;
        }

        await EventBus.PublishAsync(new UserDeletedEvent
        {
            UserId = user.Id,
            UserName = user.UserName ?? string.Empty,
            DeletionTime = DateTime.UtcNow,
            DeletedBy = (_currentUser ?? CurrentUser)?.Id
        }, cancellationToken: default);
    }

    /// <summary>
    /// 账号状态发生了「此后不该再进来」的变化时，把该用户已经在线的会话与刷新令牌一并作废。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★★ <b>停用一个账号此前只写了一个锁定时间。</b>锁定判定挂在登录守卫上，只在签发新令牌时生效，
    /// 而已经签出去的 access token 会一直用到过期，刷新令牌更是可以无限续期 ——
    /// 于是「停用」在管理端显示成功，被停用的一方却照常在用。
    /// 这里是「推」的一侧：状态一变就把在线凭据清掉；「拉」的一侧是刷新路径上的守卫链，
    /// 两层都要有 —— 推漏了事件、拉才能兜住；拉有延迟，推才能立刻生效。
    /// </para>
    /// <para>
    /// 撤销失败不改变主动作的结果（账号确实已经停用了），但会留一条告警：
    /// 一次没踢掉人的停用是需要有人知道的。
    /// </para>
    /// </remarks>
    private async Task RevokeSessionsAsync(Guid userId, SessionRevocationReason reason)
    {
        if (_sessionRevocation == null)
        {
            LogWarning(
                "ISessionRevocationService is not available; existing sessions for user {UserId} stay active after {Reason}.",
                userId, reason);
            return;
        }

        try
        {
            await _sessionRevocation.RevokeUserSessionsAsync(userId, reason);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to revoke sessions for user {UserId} after {Reason}.", userId, reason);
        }
    }

    /// <summary>
    /// 把账号从「停用 / 锁定」放出来：清掉 <c>LockoutEnd</c>、归零失败计数，
    /// 并保证登录失败锁定机制仍然武装着。启用与解锁共用这一个原语。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★★★ <b>此前启用的第一句是 <c>SetLockoutEnabledAsync(user, false)</c>，那是一处双重缺陷。</b>
    /// ASP.NET Identity 的 <c>SetLockoutEndDateAsync</c> 在 <c>LockoutEnabled == false</c> 时直接返回
    /// <c>UserLockoutNotEnabled</c> 失败且什么都不写，而返回值又被丢弃 —— 于是「停用 → 启用」的真实结果是
    /// <c>LockoutEnabled</c> 落成 false、<c>LockoutEnd</c> 仍是一百年后、接口答成功、列表继续显示已停用。
    /// 与此同时 <c>IsLockedOutAsync</c> 的第一句就是查 <c>LockoutEnabled</c>，该账号自此密码可以无限次猜，
    /// <c>MaxFailedLoginAttempts</c> 对它逐字失效，且没有任何一处会说出来。
    /// </para>
    /// <para>
    /// ★ 顺序不能反：先武装（<c>true</c>）再清 <c>LockoutEnd</c>。三个 <c>IdentityResult</c> 都要检查 ——
    /// Identity 的 <c>Set*Async</c> 系列有前置条件，失败是静默的（同 <c>InvitationService</c> 的记录）。
    /// 先武装也让解锁对历史上被旧版启用过的行（<c>LockoutEnabled = false</c>）自愈，
    /// 而不是在它们身上永远失败。
    /// </para>
    /// </remarks>
    private async Task<IdentityResult> ClearLockoutAsync(User user)
    {
        var armed = await _userManager.SetLockoutEnabledAsync(user, true);
        if (!armed.Succeeded)
        {
            return armed;
        }

        var released = await _userManager.SetLockoutEndDateAsync(user, null);
        if (!released.Succeeded)
        {
            return released;
        }

        return await _userManager.ResetAccessFailedCountAsync(user);
    }

    /// <summary>
    /// 把账号锁到某个时刻：停用（一百年后）、锁定（到期）、自助停用与自助注销共用这一个原语。
    /// </summary>
    /// <remarks>
    /// ★ 与 <see cref="ClearLockoutAsync"/> 是同一个原语的两面，两个 <c>IdentityResult</c> 同样都要检查。
    /// 此前四处都不看返回值：<c>UserValidator</c> 不过（存量行的用户名 / 邮箱早已不合当前规则）
    /// 或并发戳冲突时 <c>LockoutEnd</c> 一字未写，而后面照样踢会话、发「已停用」事件、答 200 ——
    /// 管理员看到的是「已停用」，审计里也是，账号继续登录。先武装再写 <c>LockoutEnd</c>，
    /// 理由同上：未武装时后者必然以 <c>UserLockoutNotEnabled</c> 失败。
    /// </remarks>
    private async Task<IdentityResult> LockUntilAsync(User user, DateTimeOffset lockoutEnd)
    {
        var armed = await _userManager.SetLockoutEnabledAsync(user, true);
        if (!armed.Succeeded)
        {
            return armed;
        }

        return await _userManager.SetLockoutEndDateAsync(user, lockoutEnd);
    }

    /// <summary>
    /// 本次请求能碰到哪些账号（多租户裁剪口径见 <see cref="UserTenantScope"/>）。
    /// 每次现算：<see cref="ICurrentTenant"/> 是 AsyncLocal 的，构造时读一次会把切换前的值钉死。
    /// </summary>
    private UserTenantScope Scope => UserTenantScope.Resolve(_multiTenancyEnabled, _currentTenant, _currentUser ?? CurrentUser);

    /// <summary>
    /// 按 id 取账号，且只取范围内的：不在范围内与不存在同样返回 <c>null</c>，调用方一律答 404。
    /// </summary>
    /// <remarks>
    /// ★ 这里是全部按 id 的管理动作的唯一入口。<see cref="User"/> 没有全局租户过滤器
    /// （理由见 <see cref="UserTenantScope"/>），少了这一道，租户 A 的管理员能重置、停用、删除
    /// 别家租户的账号。越界留一条 Warning —— 一次真实的越权尝试不该长得和一次 404 一模一样。
    /// </remarks>
    private async Task<User?> FindScopedUserAsync(Guid id)
    {
        var user = await _userManager.FindByGuidAsync(id);
        if (user == null)
        {
            return null;
        }

        var scope = Scope;
        if (scope.Contains(user))
        {
            return user;
        }

        LogWarning(
            "Rejected a cross-tenant user access: user {UserId} belongs to tenant {UserTenantId} but the request is scoped to tenant {TenantId}.",
            user.Id, user.TenantId, scope.TenantId);
        return null;
    }

    /// <summary>调用者（非超管）要对一个超管账号动手时为 true，判据见 <see cref="SuperAdminTargetGuard"/>。</summary>
    private Task<bool> IsSuperAdminTargetForbiddenAsync(Guid targetUserId)
        => SuperAdminTargetGuard.IsForbiddenAsync(_functionAuthorization, _currentUser?.Id ?? CurrentUser?.Id, targetUserId);

    public async Task<Result<UserDto>> CreateAsync(CreateUserDto input)
    {
        var organizationCheck = await CheckOrganizationAssignableAsync(input.OrganizationId);
        if (!organizationCheck.Succeeded)
        {
            return Fail<UserDto>(organizationCheck.Message!, organizationCheck.Code ?? 400, organizationCheck.ErrorCode);
        }

        var userName = UserNamePolicy.ResolveForNewAccount(input.UserName, input.Email, UseEmailAsUserName);
        if (!userName.Succeeded)
        {
            return Fail<UserDto>(userName.Message!, userName.Code ?? 400, userName.ErrorCode);
        }
        if (string.IsNullOrEmpty(userName.Data))
        {
            // 两种部署缺的不是同一件事：用邮箱当用户名时，给了邮箱就不需要用户名；否则邮箱给没给都得有用户名。
            return Fail<UserDto>(
                UseEmailAsUserName ? "Username is required when the account has no email address." : "Username is required.",
                400,
                ErrorCodes.VALIDATION_ERROR);
        }

        var user = input.MapTo<User>();
        user.UserName = userName.Data;
        if (_multiTenancyEnabled && user.TenantId == null)
        {
            user.TenantId = ResolveNewUserTenantId();
        }

        // 无密码创建走 UserManager 的另一个重载：把 null 传给带密码的那个会直接抛。
        // 两个重载都会跑完整的 UserValidator（重名、邮箱格式），差别仅在密码策略 ——
        // 没有密码就没有可校验的密码，而不是「跳过了校验」。
        var result = string.IsNullOrEmpty(input.Password)
            ? await _userManager.CreateAsync(user)
            : await _userManager.CreateAsync(user, input.Password);
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

    /// <inheritdoc />
    public Task<Result<UserDto>> UpdateProfileAsync(Guid id, UpdateProfileDto input)
    {
        Check.NotNull(input);

        // 逐字段显式搬运，不用 MapTo：映射器是「按名字尽量搬」的语义，
        // 将来给 UpdateUserDto 加一个特权字段时，它会连同新字段一起被搬过来，
        // 而这里要的恰恰是「特权字段永远保持 null」。显式赋值让新增字段默认落在安全的一侧。
        var admin = new UpdateUserDto
        {
            // Email / PhoneNumber 刻意不赋值：自助路径不得直改联系方式，见 UpdateProfileDto 的注释。
            FirstName = input.FirstName,
            LastName = input.LastName,
            Nickname = input.Nickname,
            AvatarUrl = input.AvatarUrl,
            AvatarId = input.AvatarId,
            Gender = input.Gender,
            Birthday = input.Birthday,
            Bio = input.Bio,
            Address = input.Address,
            Website = input.Website,
            // OrganizationId / RoleIds 刻意不赋值：自助路径不得改所属组织与角色
            // Email / PhoneNumber 同理：换绑必须走带验证码的换绑端点
        };

        return UpdateAsync(id, admin);
    }

    /// <inheritdoc />
    public async Task<Result<UserDto>> UpdateAsync(Guid id, UpdateUserDto input)
    {
        var user = await FindScopedUserAsync(id);
        if (user == null)
        {
            return Fail<UserDto>("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        if (await IsSuperAdminTargetForbiddenAsync(id))
        {
            return Fail<UserDto>(SuperAdminTargetGuard.Message, 403, ErrorCodes.FORBIDDEN);
        }

        var organizationCheck = await CheckOrganizationAssignableAsync(input.OrganizationId);
        if (!organizationCheck.Succeeded)
        {
            return Fail<UserDto>(organizationCheck.Message!, organizationCheck.Code ?? 400, organizationCheck.ErrorCode);
        }

        // 只更新非空字段，避免空字符串覆盖现有值
        // 只更新 User 表的核心字段（Email, PhoneNumber, OrganizationId）
        // Nickname、Avatar 等个人资料字段在 UserDetail 中更新
        //
        // ★★★ 联系方式经 UserManager 的 Set*Async 写入，而不是直接赋值。
        // 两处差别都是安全性的：
        //   ① Set*Async 会把对应的**确认位清掉**。直接赋值不会，于是改完地址之后
        //      EmailConfirmed 仍是 true —— 而框架把那一位当作「这个地址属于这个人」的断言
        //      （找回密码、邮箱 2FA、第三方按邮箱认领账号、消费应用按域名授权都读它）。
        //      换句话说，直接赋值等于允许任何人给自己盖一个「已验证」的章。
        //   ② SetEmailAsync 顺带维护 NormalizedEmail 并跑一遍用户校验器
        //      （RequireUniqueEmail 在那里生效），直接赋值则要等到 UpdateAsync 才补上归一化。
        //
        // 想改完就算已验证，走 ConfirmContactAsync —— 那是一个显式的、留痕的管理动作。
        if (!string.IsNullOrWhiteSpace(input.Email)
            && !string.Equals(input.Email, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            var emailResult = await _userManager.SetEmailWithUserNameAsync(user, input.Email);
            if (!emailResult.Succeeded)
            {
                return Fail<UserDto>(
                    $"Failed to update user: {emailResult.FormatErrors()}",
                    400,
                    ErrorCodes.IDENTITY_USER_UPDATE_FAILED);
            }
        }
        if (!string.IsNullOrWhiteSpace(input.PhoneNumber)
            && !string.Equals(input.PhoneNumber, user.PhoneNumber, StringComparison.Ordinal))
        {
            var phoneResult = await _userManager.SetPhoneNumberAsync(user, input.PhoneNumber);
            if (!phoneResult.Succeeded)
            {
                return Fail<UserDto>(
                    $"Failed to update user: {phoneResult.FormatErrors()}",
                    400,
                    ErrorCodes.IDENTITY_USER_UPDATE_FAILED);
            }
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
            // ★ 结果不能丢：详情那一侧会拒绝（头像文件归属探针 403 / 存储模块缺席 501），丢掉返回值
            //   就是把「头像没写进去」报成「资料已更新」。请求级工作单元会把前面 UserManager 已写的
            //   部分一并回滚。
            var detailResult = await _userDetailService.CreateOrUpdateAsync(user.Id, detailDto);
            if (!detailResult.Succeeded)
                return Fail<UserDto>(detailResult.Message ?? "Failed to update user details", detailResult.Code ?? 400, detailResult.ErrorCode);
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
        var user = await FindScopedUserAsync(id);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        if (await IsSuperAdminTargetForbiddenAsync(id))
        {
            return Fail(SuperAdminTargetGuard.Message, 403, ErrorCodes.FORBIDDEN);
        }

        var inUse = await RefuseIfInUseAsync(user);
        if (inUse != null)
        {
            return inUse;
        }

        // Snapshot current role IDs BEFORE deletion so the cache-invalidation
        // event has the full removed list. We map role-names → IDs via
        // RoleManager - `UserManager.GetRolesAsync` returns names only.
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

        await RevokeSessionsAsync(id, SessionRevocationReason.AccountDeleted);

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

        await PublishUserDeletedAsync(user);

        return Ok("User deleted successfully");
    }

    public async Task<Result<UserDto>> GetByIdAsync(Guid id)
    {
        // 缓存键不含租户，而 DTO 也不带 TenantId，无从事后核对 ——
        // 裁剪时先用一次主键查询确认归属，再碰缓存。
        var scope = Scope;
        if (!scope.IsUnrestricted)
        {
            var inScope = await scope.Apply(_userRepository.Where(u => u.Id == id)).AnyAsync();
            if (!inScope)
            {
                LogWarning(
                    "Rejected a cross-tenant user read: user {UserId} is outside tenant {TenantId}.",
                    id, scope.TenantId);
                return Fail<UserDto>("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
            }
        }

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
        // 拆分前这里 Include 了 Organization 导航属性以便 ProjectTo 出组织名；
        // 导航属性随实体搬进可选包之后，组织名改为投影完成后按整页批量补一次（见下）。
        var queryable = Scope.Apply(_userRepository.Where(u => !u.IsDeleted));

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
            // 「现在」先取成局部变量：理由见 GetStatisticsAsync（SQLite 翻不了内联的 DateTimeOffset.UtcNow）。
            var now = DateTimeOffset.UtcNow;
            if (query.IsLockedOut.Value)
            {
                queryable = queryable.Where(u => u.LockoutEnd != null && u.LockoutEnd > now);
            }
            else
            {
                queryable = queryable.Where(u => u.LockoutEnd == null || u.LockoutEnd <= now);
            }
        }

        // 待办筛选（「谁还没接受邀请」「谁欠着改密」）。
        // ★ 必须把按位与写进表达式本身：HasPendingAction / Enum.HasFlag 都翻译不成 SQL。
        //   标志值也要先落到局部变量，表达式树里不能直接读可空属性的 .Value。
        if (query.PendingAction.HasValue)
        {
            var flag = query.PendingAction.Value;
            queryable = queryable.Where(u => (u.PendingActions & flag) != PendingUserActions.None);
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

        // 批量获取组织名，消除 N+1（未加载组织包时留空）
        await FillOrganizationNamesAsync(paged.Items as IReadOnlyCollection<UserListItemDto> ?? paged.Items.ToList());

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
        var user = await FindScopedUserAsync(id);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        if (await IsSuperAdminTargetForbiddenAsync(id))
        {
            return Fail(SuperAdminTargetGuard.Message, 403, ErrorCodes.FORBIDDEN);
        }

        // ★★★ 未接受邀请的账号不能被「启用」放出来。这不是多余的守卫：启用就是清掉
        //   LockoutEnd，LockedAccountLoginGuard 从此放行；若邀请状态也被这里一并清掉，
        //   一个没有密码、没有二次验证、角色却已预设好的账号就对全部登录路径敞开了
        //   （验证码登录只需要收到一封邮件）。让人进来的唯一途径必须是接受邀请本身。
        if (user.HasPendingAction(PendingUserActions.InvitationPending))
        {
            return Fail(
                "Cannot enable an account that has not accepted its invitation. Resend the invitation instead.",
                409,
                ErrorCodes.IDENTITY_ACTIVATION_PENDING);
        }

        var cleared = await ClearLockoutAsync(user);
        if (!cleared.Succeeded)
        {
            return Fail(
                $"Failed to enable user: {cleared.FormatErrors()}",
                500,
                ErrorCodes.IDENTITY_USER_UPDATE_FAILED);
        }

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
        var user = await FindScopedUserAsync(id);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        if (await IsSuperAdminTargetForbiddenAsync(id))
        {
            return Fail(SuperAdminTargetGuard.Message, 403, ErrorCodes.FORBIDDEN);
        }

        // 禁用用户：锁定到未来某个时间（如100年后）
        var locked = await LockUntilAsync(user, DateTimeOffset.UtcNow.AddYears(100));
        if (!locked.Succeeded)
        {
            return Fail(
                $"Failed to disable user: {locked.FormatErrors()}",
                500,
                ErrorCodes.IDENTITY_USER_UPDATE_FAILED);
        }

        // 把已经在线的会话与刷新令牌一并作废 —— 否则"停用"只对下一次登录生效。
        await RevokeSessionsAsync(user.Id, SessionRevocationReason.AccountDisabled);

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
        var user = await FindScopedUserAsync(id);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        if (await IsSuperAdminTargetForbiddenAsync(id))
        {
            return Fail(SuperAdminTargetGuard.Message, 403, ErrorCodes.FORBIDDEN);
        }

        var lockoutEndDate = lockoutEnd ?? DateTimeOffset.UtcNow.AddDays(DefaultLockoutDays);

        var locked = await LockUntilAsync(user, lockoutEndDate);
        if (!locked.Succeeded)
        {
            return Fail(
                $"Failed to lock user: {locked.FormatErrors()}",
                500,
                ErrorCodes.IDENTITY_USER_UPDATE_FAILED);
        }

        await RevokeSessionsAsync(user.Id, SessionRevocationReason.AccountLocked);

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
        var user = await FindScopedUserAsync(id);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        if (await IsSuperAdminTargetForbiddenAsync(id))
        {
            return Fail(SuperAdminTargetGuard.Message, 403, ErrorCodes.FORBIDDEN);
        }

        var cleared = await ClearLockoutAsync(user);
        if (!cleared.Succeeded)
        {
            return Fail(
                $"Failed to unlock user: {cleared.FormatErrors()}",
                500,
                ErrorCodes.IDENTITY_USER_UPDATE_FAILED);
        }

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

        // 批量更新逐个提交、不是事务：超管目标必须在动第一个之前整批问完，拒绝就一个都不动。
        if (await SuperAdminTargetGuard.IsAnyForbiddenAsync(_functionAuthorization, _currentUser?.Id ?? CurrentUser?.Id, inputList.Select(x => x.Id)))
        {
            return Fail<IEnumerable<UserListItemDto>>(SuperAdminTargetGuard.Message, 403, ErrorCodes.FORBIDDEN);
        }

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

        // 批量查找用户（使用一次查询而不是循环）；范围外的 id 与不存在的 id 同样落不进来。
        var users = await Scope.Apply(_userRepository.Where(u => idList.Contains(u.Id)))
            .ToListAsync();

        // 超管目标与下面的「被认领」同理：整批问完，含一个就整批拒绝，一个都不删。
        if (await SuperAdminTargetGuard.IsAnyForbiddenAsync(_functionAuthorization, _currentUser?.Id ?? CurrentUser?.Id, users.Select(u => u.Id)))
        {
            return Fail(SuperAdminTargetGuard.Message, 403, ErrorCodes.FORBIDDEN);
        }

        // ★ 整批先问完再动手。批量删除不是事务（UserManager 逐个提交），一旦第二个人被
        //   领域记录认领而第一个人已经删了，既没有回滚，返回值也说不清删掉了谁。
        //   先问一圈，被认领的一个都不动，操作员拿到的是「谁挡住了」而不是半批结果。
        foreach (var user in users)
        {
            var inUse = await RefuseIfInUseAsync(user);
            if (inUse != null)
            {
                return inUse;
            }
        }

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

            // ★★ 与 DeleteAsync 同一步，不能只有单个删除做。access token 的校验只看会话
            //   （OnTokenValidated 不查用户还在不在），所以漏掉这一步的后果是：批量删掉的账号
            //   在令牌剩余寿命里照常通过认证，而管理员那边显示的是「已删除」。
            await RevokeSessionsAsync(user.Id, SessionRevocationReason.AccountDeleted);

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

            await PublishUserDeletedAsync(user);
        }

        LogInformation("Batch deleted {Count} users", users.Count);
        return Ok($"Successfully deleted {users.Count} users");
    }

    public async Task<Result> AssignRolesAsync(Guid userId, IEnumerable<Guid> roleIds)
    {
        var user = await FindScopedUserAsync(userId);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        if (await IsSuperAdminTargetForbiddenAsync(userId))
        {
            return Fail(SuperAdminTargetGuard.Message, 403, ErrorCodes.FORBIDDEN);
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
        var user = await FindScopedUserAsync(userId);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        if (await IsSuperAdminTargetForbiddenAsync(userId))
        {
            return Fail(SuperAdminTargetGuard.Message, 403, ErrorCodes.FORBIDDEN);
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

        // Critical: removal must publish (more so than assignment) - a stale
        // cache after a revocation is a permission-retention security gap.
        await PublishUserRolesChangedAsync(
            user,
            addedRoleIds: new List<Guid>(),
            removedRoleIds: roles.Select(r => r.Id).ToList(),
            changeType: UserRolesChangeType.Removed);

        return Ok("Roles removed successfully");
    }

    /// <summary>
    /// Publish <see cref="UserRolesChangedEvent"/>. Auxiliary: a failed publish
    /// must not break the main role-change flow, so it is caught and logged here
    /// on the PUBLISHER side. (Handlers are the opposite: they must let exceptions
    /// bubble so the bus can retry / dead-letter. See docs/coding-standards/events.md.)
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
        var query = Scope.Apply(_userRepository.Where(u => !u.IsDeleted));

        if (organizationId.HasValue)
        {
            query = query.Where(u => u.OrganizationId == organizationId.Value);
        }

        var totalUsers = await query.CountAsync();
        // ★ 「现在」先取成局部变量再进表达式：EF 不把 DateTimeOffset.UtcNow 当参数（它想翻成数据库函数），
        //   而 SQLite 提供者没有这条翻译 ⇒ 整个谓词翻不了。局部变量走参数，与 LockoutEnd 同一个类型映射。
        var now = DateTimeOffset.UtcNow;
        var activeUsers = await query
            .Where(u => u.LockoutEnd == null || u.LockoutEnd <= now)
            .CountAsync();
        var lockedUsers = await query
            .Where(u => u.LockoutEnd != null && u.LockoutEnd > now)
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
        var user = await FindScopedUserAsync(userId);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        var oldEmail = user.Email;

        // 经 UserManager 设置邮箱（会处理 NormalizedEmail）；用户名绑在旧邮箱上的一起跟过去
        var setResult = await _userManager.SetEmailWithUserNameAsync(user, newEmail);
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
        var user = await FindScopedUserAsync(userId);
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

    /// <inheritdoc />
    public async Task<Result> ConfirmContactAsync(Guid userId, bool? confirmEmail, bool? confirmPhoneNumber)
    {
        if (confirmEmail is null && confirmPhoneNumber is null)
        {
            return Fail("Nothing to confirm", 400, ErrorCodes.VALIDATION_ERROR);
        }

        var user = await FindScopedUserAsync(userId);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        if (await IsSuperAdminTargetForbiddenAsync(userId))
        {
            return Fail(SuperAdminTargetGuard.Message, 403, ErrorCodes.FORBIDDEN);
        }

        if (confirmEmail == true && string.IsNullOrWhiteSpace(user.Email))
        {
            return Fail("Cannot confirm an email address that is not set", 400, ErrorCodes.IDENTITY_EMAIL_NOT_SET);
        }

        if (confirmPhoneNumber == true && string.IsNullOrWhiteSpace(user.PhoneNumber))
        {
            return Fail("Cannot confirm a phone number that is not set", 400, ErrorCodes.VALIDATION_ERROR);
        }

        if (confirmEmail.HasValue) user.EmailConfirmed = confirmEmail.Value;
        if (confirmPhoneNumber.HasValue) user.PhoneNumberConfirmed = confirmPhoneNumber.Value;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return Fail(
                $"Failed to update user: {result.FormatErrors()}",
                400,
                ErrorCodes.IDENTITY_USER_UPDATE_FAILED);
        }

        if (_cache != null)
        {
            await _cache.RemoveAsync(CacheKeys.Identity.User(user.Id));
        }

        // ★ 必须留痕：这一步是「有人替另一个人担保了一个地址」，而框架下游拿那个断言
        // 决定能不能往这个地址发找回密码的码。谁盖的章、什么时候盖的，事后要查得到。
        LogWarning(
            "Contact confirmation set by {ActorId} for user {UserId}: email={ConfirmEmail}, phone={ConfirmPhone}.",
            CurrentUser?.Id, user.Id, confirmEmail, confirmPhoneNumber);

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
    /// 未加载组织包时，凡是要求「把这个用户放进某个组织」的写入路径统一给出的回答。
    /// </summary>
    /// <remarks>
    /// 501 而不是 503/500：这台宿主不提供组织架构，重试永远不会变好；消息点名要加载的包，
    /// 因为「少加载一个可选包」在日志里唯一能自证的方式就是它自己说出来。
    /// 与 <c>DefaultUserAdminController</c> 的两个组织端点同一口径。
    /// </remarks>
    private const string OrganizationModuleMissing =
        "Assigning a user to an organization requires the Tnzi.Identity.Organization module, which this host has not loaded.";

    /// <summary>
    /// 写入 <c>OrganizationId</c> 之前的守卫：组织包在不在、这个组织存不存在。
    /// </summary>
    /// <remarks>
    /// ★ 这一条在拆分之前**不存在**：一个不存在的 OrganizationId 会一路走到外键，
    /// 数据库抛异常，调用方收到一个不透明的 500。拆分之后外键随组织包走，
    /// 不加载该包时连那道数据库级的兜底也没有了 —— 所以判定必须提前到服务层，
    /// 而且必须**失败关闭**：问不到组织就拒绝写入，绝不"先写下去再说"。
    /// </remarks>
    /// <param name="organizationId">要写入的组织 Id；null 表示本次不改组织，直接放行</param>
    private async Task<Result> CheckOrganizationAssignableAsync(Guid? organizationId)
    {
        if (!organizationId.HasValue)
        {
            return Ok();
        }

        if (_organizationService == null)
        {
            return Fail(OrganizationModuleMissing, 501, ErrorCodes.IDENTITY_ORGANIZATION_ERROR);
        }

        var organization = await _organizationService.GetByIdAsync(organizationId.Value);
        if (!organization.Succeeded || organization.Data == null)
        {
            return Fail("Organization not found", 400, ErrorCodes.IDENTITY_ORGANIZATION_NOT_FOUND);
        }

        return Ok();
    }

    /// <summary>
    /// 给一页用户列表项补上组织名（一次批量查询，无 N+1）。
    /// </summary>
    /// <remarks>
    /// 拆分前这一列来自 <c>User → Organization</c> 的 LEFT JOIN；导航属性随实体搬进可选包
    /// 之后改成这里问一次。未加载该包时 <c>_organizationService</c> 为 null，
    /// 名字留空 —— 少一列显示值，不改任何其它字段。
    /// </remarks>
    private async Task FillOrganizationNamesAsync(IReadOnlyCollection<UserListItemDto> rows)
    {
        if (_organizationService == null || rows.Count == 0)
        {
            return;
        }

        var ids = rows.Where(u => u.OrganizationId.HasValue)
            .Select(u => u.OrganizationId!.Value)
            .Distinct()
            .ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var names = await _organizationService.GetNamesAsync(ids);
        foreach (var row in rows)
        {
            if (row.OrganizationId.HasValue && names.TryGetValue(row.OrganizationId.Value, out var name))
            {
                row.OrganizationName = name;
            }
        }
    }

    /// <summary>
    /// 映射用户实体到DTO
    /// </summary>
    private async Task<UserDto> MapUserToDtoAsync(User user)
    {
        var userDto = user.MapTo<UserDto>();
        userDto.Roles = (await _userManager.GetRolesAsync(user)).ToList();

        // 组织名：拆分前只有 GetByIdAsync 那条路径 Include 了导航属性，
        // Create/Update 返回的 DTO 里这一列一直是空的。统一走这里之后三条路径一致
        // （多一次有界查询，且仅在用户确实挂着组织时发生）。
        await FillOrganizationNamesAsync(new[] { userDto });

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
        var user = await FindScopedUserAsync(userId);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        // 使用 lockout 机制禁用用户
        var locked = await LockUntilAsync(user, DateTimeOffset.UtcNow.AddYears(100));
        if (!locked.Succeeded)
        {
            return Fail(
                $"Failed to deactivate account: {locked.FormatErrors()}",
                500,
                ErrorCodes.IDENTITY_USER_UPDATE_FAILED);
        }

        await RevokeSessionsAsync(user.Id, SessionRevocationReason.AccountDisabled);

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
        var user = await FindScopedUserAsync(userId);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        var userName = user.UserName ?? string.Empty;

        // 自助注销同样要问领域记录：员工名册上的人自己把登录注销掉，名册照样指着一个已删账号。
        // 拒绝时那句说明会指路去找管理员，而不是留下一条谁都看不出来的悬空行。
        var inUse = await RefuseIfInUseAsync(user);
        if (inUse != null)
        {
            return inUse;
        }

        // 先锁定再软删：锁定写不进去时账号不能已经被标成删除 ——
        // 此前是先在内存里置 IsDeleted 再调 Set*Async（它们各自 SaveChanges，会把软删标记一并带出去），
        // 于是一次失败的注销留下的是一条已软删、却没锁的行。
        var locked = await LockUntilAsync(user, DateTimeOffset.UtcNow.AddYears(100));
        if (!locked.Succeeded)
        {
            return Fail(
                $"Failed to delete account: {locked.FormatErrors()}",
                500,
                ErrorCodes.IDENTITY_USER_UPDATE_FAILED);
        }

        // 软删除
        user.IsDeleted = true;
        user.LastModificationTime = DateTime.UtcNow;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return Fail(result.FormatErrors(), 400, ErrorCodes.IDENTITY_USER_DELETE_FAILED);
        }

        await RevokeSessionsAsync(userId, SessionRevocationReason.AccountDeleted);

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

        // 领域记录只需订阅一个「账号没了」的事件，不必区分是管理员删的还是本人注销的。
        await PublishUserDeletedAsync(user);

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
        var user = await FindScopedUserAsync(userId);
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
        var queryable = Scope.Apply(_userRepository.Where(u => !u.IsDeleted));

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

        // 用邮箱作用户名时 UserName 列可省：每行的用户名就是那一行的邮箱
        var useEmailAsUserName = UseEmailAsUserName;
        if (emailIdx < 0 || passwordIdx < 0 || (userNameIdx < 0 && !useEmailAsUserName))
        {
            return Fail<UserImportResult>(
                useEmailAsUserName
                    ? "CSV header must contain at least: Email, Password"
                    : "CSV header must contain at least: UserName, Email, Password",
                400,
                ErrorCodes.VALIDATION_ERROR);
        }

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

                var requestedUserName = userNameIdx >= 0 ? fields[userNameIdx].Trim() : null;
                var email = fields[emailIdx].Trim();
                var password = fields[passwordIdx].Trim();

                if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
                {
                    result.Errors[rowNumber] = "Email and Password are required";
                    result.FailedCount++;
                    continue;
                }

                var resolvedUserName = UserNamePolicy.ResolveForNewAccount(requestedUserName, email, useEmailAsUserName);
                if (!resolvedUserName.Succeeded || string.IsNullOrEmpty(resolvedUserName.Data))
                {
                    result.Errors[rowNumber] = resolvedUserName.Succeeded ? "UserName is required" : resolvedUserName.Message!;
                    result.FailedCount++;
                    continue;
                }
                var userName = resolvedUserName.Data;

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
