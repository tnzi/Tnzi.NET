namespace Tnzi.Identity.Services;

/// <summary>
/// 角色管理服务实现
/// </summary>
public class RoleService : ApplicationService, IRoleService
{
    private readonly RoleManager<Role> _roleManager;
    private readonly DbContext _dbContext;
    private readonly IFunctionAuthorizationService? _functionAuthorization;

    /// <summary>
    /// 初始化一个 <see cref="RoleService"/> 类型的新实例。
    /// </summary>
    /// <remarks>
    /// <paramref name="functionAuthorization"/> 是可选的：契约住在核心
    /// （<c>Tnzi.Security.Authorization</c>），实现随 <c>Tnzi.Authorization</c> 走 ——
    /// 与 <c>UserService</c> 持有它的方式逐字相同，不引入 Identity → Authorization 的反向引用。
    /// 未加载授权模块时护栏整体跳过：那时系统里根本没有权限与超管的概念。
    /// </remarks>
    public RoleService(
        RoleManager<Role> roleManager,
        DbContext dbContext,
        IServiceProvider serviceProvider,
        IFunctionAuthorizationService? functionAuthorization = null)
        : base(serviceProvider)
    {
        _roleManager = Check.NotNull(roleManager);
        _dbContext = Check.NotNull(dbContext);
        _functionAuthorization = functionAuthorization;
    }

    /// <summary>
    /// 目标角色名是否落在 <c>Authorization:SuperAdminRoles</c> 上。
    /// </summary>
    /// <remarks>
    /// ★★★ <strong>这道检查补的是一个顺序漏洞。</strong>成员变更那条路径早有委托护栏
    /// （<c>CanManageRoleAsync</c>：只能操作「权限集被自己包含」且非超管配置的角色），
    /// 但它读的是角色<b>当时的名字</b>。于是把顺序倒过来就能绕开：
    /// <list type="number">
    /// <item>建一个零权限的普通角色 —— 空集被任何人包含，护栏平凡通过；</item>
    /// <item>把自己加进去 —— 同上，通过；</item>
    /// <item>把它<b>改名</b>成配置里的超管角色名 —— 改名此前没有任何守卫。</item>
    /// </list>
    /// 第三步之后 <c>IsSuperAdminAsync</c>（按角色名匹配、无缓存）立刻为真，
    /// 于是一个只有 <c>role.update</c> 的人拿到了全系统旁路。
    /// <para>
    /// ★ 唯一让这条路径此前没被走通的，是<b>数据库的角色名唯一约束</b>：
    /// 出厂配置会播种出那个角色，于是改名撞重名而失败。但那是一次巧合的兜底 ——
    /// 把 <c>SeedBuiltInAdminRoles</c> 关掉、或者配一个应用自己不会创建的名字，
    /// 兜底就没了。安全边界不该押在一个恰好存在的唯一索引上。
    /// </para>
    /// </remarks>
    private bool IsProtectedRoleName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || _functionAuthorization == null)
        {
            return false;
        }

        return _functionAuthorization.GetSuperAdminRoleNames()
            .Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 角色定义写路径的委托护栏：非超管调用者只能改 / 删自己支配得了的角色。
    /// </summary>
    /// <remarks>
    /// 与 <c>UserService.GetRoleMembershipViolationAsync</c> 同一判据、同一降级方式
    /// （无授权模块或无用户上下文时整体跳过）。此前只有成员变更受管辖，
    /// 而「改掉一个角色的定义」与「把人放进这个角色」是同一量级的权力。
    /// </remarks>
    private async Task<string?> GetRoleManagementViolationAsync(Guid roleId, string roleName)
    {
        if (_functionAuthorization == null) return null;

        var actorId = CurrentUser?.Id;
        if (actorId == null || actorId == Guid.Empty) return null;
        if (await _functionAuthorization.IsSuperAdminAsync(actorId.Value)) return null;

        return await _functionAuthorization.CanManageRoleAsync(actorId.Value, roleId)
            ? null
            : $"You cannot manage role '{roleName}': its permission set is not contained in yours, or it is a super-admin role.";
    }

    public async Task<Result<IEnumerable<RoleDto>>> GetAllAsync()
    {
        var roles = await _roleManager.Roles.ToListAsync();
        var roleDtos = roles.MapToList<RoleDto>();
        return Ok<IEnumerable<RoleDto>>(roleDtos);
    }

    public async Task<Result<IPagedList<RoleDto>>> GetPagedListAsync(RoleListQueryDto query)
    {
        var queryable = _roleManager.Roles.AsQueryable();

        // 关键词搜索（名称、描述）
        if (!string.IsNullOrEmpty(query.Keyword))
        {
            var keyword = query.Keyword!.ToLower();
            queryable = queryable.Where(r =>
                (r.Name != null && r.Name.ToLower().Contains(keyword)) ||
                (r.Description != null && r.Description.ToLower().Contains(keyword)));
        }

        // 系统角色筛选
        if (query.IsSystem.HasValue)
        {
            queryable = queryable.Where(r => r.IsSystem == query.IsSystem.Value);
        }

        // 默认角色筛选
        if (query.IsDefault.HasValue)
        {
            queryable = queryable.Where(r => r.IsDefault == query.IsDefault.Value);
        }

        // 默认按创建时间降序
        queryable = queryable.OrderByDescending(r => r.CreationTime);

        var paged = await queryable
            .ProjectTo<Role, RoleDto>()
            .CreateAsync(query);

        return Ok(paged);
    }

    public async Task<Result<RoleDetailDto>> GetDetailAsync(Guid id)
    {
        var role = await _roleManager.FindByIdAsync(id.ToString());
        if (role == null)
        {
            return Fail<RoleDetailDto>("Role not found", 404, ErrorCodes.IDENTITY_ROLE_NOT_FOUND);
        }

        var detail = role.MapTo<RoleDetailDto>();

        // 查询角色下的用户数量（同 GetUserCountAsync：经 User 表数，排除软删的幽灵行）
        detail.UserCount = await _dbContext.Set<User>()
            .CountAsync(u => !u.IsDeleted
                && _dbContext.Set<UserRole>().Any(ur => ur.RoleId == id && ur.UserId == u.Id));

        return Ok(detail);
    }

    public async Task<Result<RoleDto>> GetByIdAsync(Guid id)
    {
        var role = await _roleManager.FindByIdAsync(id.ToString());
        if (role == null)
        {
            return Fail<RoleDto>("Role not found", 404, ErrorCodes.IDENTITY_ROLE_NOT_FOUND);
        }

        return Ok(role.MapTo<RoleDto>());
    }

    public async Task<Result<RoleDto>> GetByNameAsync(string name)
    {
        var role = await _roleManager.FindByNameAsync(name);
        if (role == null)
        {
            return Fail<RoleDto>("Role not found", 404, ErrorCodes.IDENTITY_ROLE_NOT_FOUND);
        }

        return Ok(role.MapTo<RoleDto>());
    }

    public async Task<Result<RoleDto>> CreateAsync(CreateRoleDto input)
    {
        // ★ 不允许凭空造出一个「超管角色名」。它此前只被数据库的重名约束挡着，
        //   而那道约束只在该角色恰好已被播种时才存在（见 IsProtectedRoleName）。
        if (IsProtectedRoleName(input.Name))
        {
            return Fail<RoleDto>(
                $"Role name '{input.Name}' is reserved for super administrators and cannot be created here.",
                403,
                ErrorCodes.FORBIDDEN);
        }

        // 检查重名
        if (await _roleManager.RoleExistsAsync(input.Name))
        {
            return Fail<RoleDto>($"Role '{input.Name}' already exists", 409, ErrorCodes.IDENTITY_ROLE_ALREADY_EXISTS);
        }

        var role = new Role
        {
            Name = input.Name,
            Description = input.Description,
            IsDefault = input.IsDefault
        };

        var result = await _roleManager.CreateAsync(role);
        if (!result.Succeeded)
        {
            return Fail<RoleDto>(result.FormatErrors(), 400, ErrorCodes.IDENTITY_ROLE_ERROR);
        }

        // 发布角色创建事件
        if (EventBus != null)
        {
            await EventBus.PublishAsync(new RoleCreatedEvent
            {
                RoleId = role.Id,
                RoleName = role.Name!,
                Description = role.Description,
                CreationTime = DateTime.UtcNow
            }, cancellationToken: default);
        }

        LogInformation("Role created: {RoleId}, Name: {RoleName}", role.Id, role.Name);
        return Ok(role.MapTo<RoleDto>());
    }

    public async Task<Result<RoleDto>> UpdateAsync(Guid id, UpdateRoleDto input)
    {
        var role = await _roleManager.FindByIdAsync(id.ToString());
        if (role == null)
        {
            return Fail<RoleDto>("Role not found", 404, ErrorCodes.IDENTITY_ROLE_NOT_FOUND);
        }

        // 系统角色不允许重命名
        if (role.IsSystem && !string.Equals(role.Name, input.Name, StringComparison.OrdinalIgnoreCase))
        {
            return Fail<RoleDto>("System role cannot be renamed", 403, ErrorCodes.IDENTITY_ROLE_SYSTEM_PROTECTED);
        }

        // ★★★ 改名不得把一个普通角色变成超管角色。这是「先入组、再改名」那条提权路径的封堵点，
        //     判据与角色当前是否存在无关（见 IsProtectedRoleName 的注释）。
        if (!IsProtectedRoleName(role.Name) && IsProtectedRoleName(input.Name))
        {
            return Fail<RoleDto>(
                $"Role name '{input.Name}' is reserved for super administrators; renaming into it is not allowed.",
                403,
                ErrorCodes.FORBIDDEN);
        }

        // ★ 与成员变更同受委托约束：能改一个角色的定义，和能往里放人是同一量级的权力。
        var violation = await GetRoleManagementViolationAsync(role.Id, role.Name ?? input.Name);
        if (violation != null)
        {
            return Fail<RoleDto>(violation, 403, ErrorCodes.FORBIDDEN);
        }

        // Capture rename diagnostic - populated on the published event only
        // when the name *actually* changed (case-insensitive). Authorization
        // subscribes to RoleUpdatedEvent and warns when a renamed role is
        // referenced by Authorization.SuperAdminRoles (config-by-name → DB
        // rename is the canonical "I just locked myself out" footgun).
        var previousName = !string.Equals(role.Name, input.Name, StringComparison.OrdinalIgnoreCase)
            ? role.Name
            : null;

        role.Name = input.Name;
        role.Description = input.Description;

        if (input.IsDefault.HasValue)
        {
            role.IsDefault = input.IsDefault.Value;
        }

        var result = await _roleManager.UpdateAsync(role);
        if (!result.Succeeded)
        {
            return Fail<RoleDto>(result.FormatErrors(), 400, ErrorCodes.IDENTITY_ROLE_ERROR);
        }

        // 发布角色更新事件
        if (EventBus != null)
        {
            await EventBus.PublishAsync(new RoleUpdatedEvent
            {
                RoleId = role.Id,
                RoleName = role.Name!,
                PreviousName = previousName,
                Description = role.Description,
                UpdatedTime = DateTime.UtcNow
            }, cancellationToken: default);
        }

        LogInformation("Role updated: {RoleId}, Name: {RoleName}", role.Id, role.Name);
        return Ok(role.MapTo<RoleDto>());
    }

    public async Task<Result> DeleteAsync(Guid id)
    {
        var role = await _roleManager.FindByIdAsync(id.ToString());
        if (role == null)
        {
            return Fail("Role not found", 404, ErrorCodes.IDENTITY_ROLE_NOT_FOUND);
        }

        // 系统角色不允许删除
        if (role.IsSystem)
        {
            return Fail("System role cannot be deleted", 403, ErrorCodes.IDENTITY_ROLE_SYSTEM_PROTECTED);
        }

        // ★ 删除同受委托约束：把一个自己支配不了的角色删掉，是变相的越权削权
        //   （与 UserService.RemoveRolesAsync 上那条注释同源）。
        var violation = await GetRoleManagementViolationAsync(role.Id, role.Name ?? string.Empty);
        if (violation != null)
        {
            return Fail(violation, 403, ErrorCodes.FORBIDDEN);
        }

        var roleName = role.Name ?? string.Empty;
        var result = await _roleManager.DeleteAsync(role);
        if (!result.Succeeded)
        {
            return Fail(result.FormatErrors(), 400, ErrorCodes.IDENTITY_ROLE_ERROR);
        }

        // 发布角色删除事件
        if (EventBus != null)
        {
            await EventBus.PublishAsync(new RoleDeletedEvent
            {
                RoleId = id,
                RoleName = roleName,
                DeletedTime = DateTime.UtcNow
            }, cancellationToken: default);
        }

        LogInformation("Role deleted: {RoleId}, Name: {RoleName}", id, roleName);
        return Ok();
    }

    public async Task<Result> DeleteManyAsync(IEnumerable<Guid> ids)
    {
        Check.NotNullOrEmpty(ids);

        var failures = new List<string>();

        foreach (var id in ids)
        {
            var role = await _roleManager.FindByIdAsync(id.ToString());
            if (role == null)
            {
                continue; // 已不存在，跳过
            }

            if (role.IsSystem)
            {
                failures.Add($"Role '{role.Name}' is a system role and cannot be deleted");
                continue;
            }

            var roleName = role.Name ?? string.Empty;
            var result = await _roleManager.DeleteAsync(role);
            if (!result.Succeeded)
            {
                failures.Add($"Failed to delete role '{roleName}': {result.FormatErrors()}");
                continue;
            }

            // 发布角色删除事件
            if (EventBus != null)
            {
                await EventBus.PublishAsync(new RoleDeletedEvent
                {
                    RoleId = id,
                    RoleName = roleName,
                    DeletedTime = DateTime.UtcNow
                }, cancellationToken: default);
            }

            LogInformation("Role deleted: {RoleId}, Name: {RoleName}", id, roleName);
        }

        if (failures.Count > 0)
        {
            return Fail(string.Join("; ", failures), 400, ErrorCodes.IDENTITY_ROLE_ERROR);
        }

        return Ok();
    }

    public async Task<Result<bool>> ExistsAsync(string name)
    {
        var exists = await _roleManager.RoleExistsAsync(name);
        return Ok(exists);
    }

    public async Task<Result<IPagedList<UserListItemDto>>> GetUsersInRoleAsync(Guid roleId, PagedQueryDto query)
    {
        // 验证角色存在
        var role = await _roleManager.FindByIdAsync(roleId.ToString());
        if (role == null)
        {
            return Fail<IPagedList<UserListItemDto>>("Role not found", 404, ErrorCodes.IDENTITY_ROLE_NOT_FOUND);
        }

        // 通过 UserRole 关联表查询角色下的用户
        var userIds = _dbContext.Set<UserRole>()
            .Where(ur => ur.RoleId == roleId)
            .Select(ur => ur.UserId);

        var usersQuery = _dbContext.Set<User>()
            .Where(u => userIds.Contains(u.Id) && !u.IsDeleted)
            .OrderByDescending(u => u.CreationTime);

        var paged = await usersQuery
            .ProjectTo<User, UserListItemDto>()
            .CreateAsync(query);

        return Ok(paged);
    }

    public async Task<Result<int>> GetUserCountAsync(Guid roleId)
    {
        // 验证角色存在
        var role = await _roleManager.FindByIdAsync(roleId.ToString());
        if (role == null)
        {
            return Fail<int>("Role not found", 404, ErrorCodes.IDENTITY_ROLE_NOT_FOUND);
        }

        // ★ 必须回到 User 表数：UserRole 是纯关联表，没有软删标记，直接数它会把
        //   已被软删的用户一起算进去（同一处的 GetUsersInRoleAsync 早就过滤了 !IsDeleted，
        //   于是「列表 3 个人」配「计数 5 个人」）。这是既有缺陷，与邀请无关。
        var count = await _dbContext.Set<User>()
            .CountAsync(u => !u.IsDeleted
                && _dbContext.Set<UserRole>().Any(ur => ur.RoleId == roleId && ur.UserId == u.Id));

        return Ok(count);
    }

    public async Task<Result<IEnumerable<RoleDto>>> GetDefaultRolesAsync()
    {
        var roles = await _roleManager.Roles
            .Where(r => r.IsDefault)
            .ToListAsync();

        return Ok<IEnumerable<RoleDto>>(roles.MapToList<RoleDto>());
    }

    public async Task<Result> SetDefaultAsync(Guid id, bool isDefault)
    {
        var role = await _roleManager.FindByIdAsync(id.ToString());
        if (role == null)
        {
            return Fail("Role not found", 404, ErrorCodes.IDENTITY_ROLE_NOT_FOUND);
        }

        role.IsDefault = isDefault;
        var result = await _roleManager.UpdateAsync(role);
        if (!result.Succeeded)
        {
            return Fail(result.FormatErrors(), 400, ErrorCodes.IDENTITY_ROLE_ERROR);
        }

        LogInformation("Role '{RoleName}' default status set to {IsDefault}", role.Name ?? string.Empty, isDefault);
        return Ok();
    }
}
