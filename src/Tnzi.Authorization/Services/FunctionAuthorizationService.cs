
namespace Tnzi.Authorization.Services;

/// <summary>
/// 功能授权服务实现
/// </summary>
public class FunctionAuthorizationService : ApplicationService, IFunctionAuthorizationService, IModuleManagementService, IRoleFunctionService
{
    private readonly IRepository<FunctionModule, Guid> _moduleRepository;
    private readonly IRepository<ModuleFunction, Guid> _moduleFunctionRepository;
    private readonly IRepository<RoleFunction, Guid> _roleFunctionRepository;
    private readonly IRepository<UserFunction, Guid> _userFunctionRepository;
    private readonly IUserRoleService? _userRoleService;
    private readonly FunctionAuthCache? _functionAuthCache;
    private readonly IOptions<Tnzi.Authorization.Options.AuthorizationOptions>? _options;
    private readonly IRepository<Tnzi.Identity.Entities.Role, Guid>? _roleRepository;

    /// <summary>
    /// 初始化一个<see cref="FunctionAuthorizationService"/>类型的新实例
    /// </summary>
    public FunctionAuthorizationService(
        IRepository<FunctionModule, Guid> moduleRepository,
        IRepository<ModuleFunction, Guid> moduleFunctionRepository,
        IRepository<RoleFunction, Guid> roleFunctionRepository,
        IRepository<UserFunction, Guid> userFunctionRepository,
        IServiceProvider serviceProvider,
        IUserRoleService? userRoleService = null,
        FunctionAuthCache? functionAuthCache = null,
        IOptions<Tnzi.Authorization.Options.AuthorizationOptions>? options = null,
        IRepository<Tnzi.Identity.Entities.Role, Guid>? roleRepository = null)
        : base(serviceProvider)
    {
        _moduleRepository = Check.NotNull(moduleRepository);
        _moduleFunctionRepository = Check.NotNull(moduleFunctionRepository);
        _roleFunctionRepository = Check.NotNull(roleFunctionRepository);
        _userFunctionRepository = Check.NotNull(userFunctionRepository);
        _userRoleService = userRoleService;
        _functionAuthCache = functionAuthCache;
        _options = options;
        _roleRepository = roleRepository;
    }

    /// <summary>
    /// Whether the user is a super administrator - member of any role listed
    /// in <c>Authorization:SuperAdminRoles</c> (case-insensitive role-name
    /// match). Super admins bypass every permission check and see the full
    /// enabled-function catalogue; every other user resolves through explicit
    /// grants only (deny-by-default).
    /// </summary>
    public async Task<bool> IsSuperAdminAsync(Guid userId)
    {
        var superRoles = _options?.Value.SuperAdminRoles;
        if (superRoles is not { Count: > 0 } || _userRoleService == null)
            return false;

        var lookup = await _userRoleService.GetUserRolesAsync(new[] { userId });
        if (!lookup.TryGetValue(userId, out var userRoles)) return false;

        var superSet = new HashSet<string>(superRoles, StringComparer.OrdinalIgnoreCase);
        return userRoles.Any(r => superSet.Contains(r));
    }

    /// <summary>
    /// 委托支配判定（权限集包含模型）：超管支配一切角色；其余授权者仅支配
    /// "显式权限集 ⊆ 自己有效权限集" 的角色，且永远不能支配超管配置角色
    /// （超管角色通常零显式授权，否则会被任何人平凡支配）。
    /// </summary>
    public async Task<bool> CanManageRoleAsync(Guid grantorUserId, Guid roleId)
    {
        if (await IsSuperAdminAsync(grantorUserId)) return true;
        if (await IsSuperAdminRoleAsync(roleId)) return false;

        var grantorCodes = ToCaseInsensitiveSet(await GetUserPermissionNamesAsync(grantorUserId));
        var roleCodes = await GetRoleGrantedCodesAsync(roleId);
        return roleCodes.All(grantorCodes.Contains);
    }

    /// <summary>
    /// 角色名是否列于 <c>Authorization:SuperAdminRoles</c>。已配置超管角色但角色
    /// 仓储不可用时 fail CLOSED(视一切角色为受保护):超管角色的显式授权集通常
    /// 为空,静默撤保护会让任何授权者"平凡支配"它并借成员路径自提权。
    /// </summary>
    private async Task<bool> IsSuperAdminRoleAsync(Guid roleId)
    {
        var superRoles = _options?.Value.SuperAdminRoles;
        if (superRoles is not { Count: > 0 }) return false;
        if (_roleRepository == null) return true;

        var roleName = await _roleRepository
            .Where(r => r.Id == roleId)
            .Select(r => r.Name)
            .FirstOrDefaultAsync();
        return !string.IsNullOrEmpty(roleName)
            && superRoles.Contains(roleName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>角色显式授予的功能码（RoleFunction 直连），仅启用项。</summary>
    private async Task<List<string>> GetRoleGrantedCodesAsync(Guid roleId)
    {
        var enabledFunctions = _moduleFunctionRepository.Where(f => f.IsEnabled);

        return await _roleFunctionRepository
            .Where(rf => rf.RoleId == roleId && rf.IsEnabled)
            .Join(enabledFunctions, rf => rf.FunctionId, f => f.Id, (rf, f) => f.Code)
            .Distinct()
            .ToListAsync();
    }

    /// <summary>
    /// 角色授权写路径的委托护栏。非超管授权者仅能操作自己支配的角色
    /// （见 <see cref="CanManageRoleAsync"/>），且仅能授出自己持有的权限码。
    /// 允许时返回 null，越界时返回英文错误消息（调用方包装为 403）。
    /// 无用户上下文（系统/播种/内部路径与单元测试）时整体跳过。
    /// </summary>
    private async Task<string?> GetRoleGrantViolationAsync(Guid roleId, IReadOnlyCollection<Guid>? functionIdsToGrant = null)
    {
        var grantorId = CurrentUser?.Id;
        if (grantorId == null || grantorId == Guid.Empty) return null;
        if (await IsSuperAdminAsync(grantorId.Value)) return null;

        if (!await CanManageRoleAsync(grantorId.Value, roleId))
        {
            return "You cannot manage this role: its permission set is not contained in yours, or it is a super-admin role.";
        }

        if (functionIdsToGrant is { Count: > 0 })
        {
            var grantorCodes = ToCaseInsensitiveSet(await GetUserPermissionNamesAsync(grantorId.Value));
            var idList = functionIdsToGrant.ToList();
            var requestedCodes = await _moduleFunctionRepository
                .Where(f => idList.Contains(f.Id))
                .Select(f => f.Code)
                .ToListAsync();
            var exceeded = requestedCodes
                .Where(c => !grantorCodes.Contains(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (exceeded.Count > 0)
            {
                return $"You cannot grant permissions you do not hold: {string.Join(", ", exceeded)}";
            }
        }

        return null;
    }

    /// <summary>
    /// 检查用户是否有权限访问指定功能
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="permissionName">权限名称</param>
    /// <returns>是否有权限</returns>
    public async Task<bool> CheckPermissionAsync(Guid userId, string permissionName)
    {
        if (string.IsNullOrEmpty(permissionName))
            return false;

        // SuperAdmin bypass — short-circuit before touching the function
        // tables. Everyone else resolves through explicit grants only
        // (deny-by-default): no role is implicitly granted anything.
        if (await IsSuperAdminAsync(userId)) return true;

        // Permission codes are matched case-insensitively. The admin-roles
        // comparison above already uses OrdinalIgnoreCase; this brings the
        // regular check in line so a stored `Catalog.Product.Create` matches
        // a controller-declared `catalog.product.create` (and vice versa).
        // Without this, a single character-case mismatch between the DB row
        // and the [ApiAuthorize(PermissionName=...)] attribute silently 403s.
        var userPermissionNames = await GetExplicitUserPermissionNamesAsync(userId);
        return ToCaseInsensitiveSet(userPermissionNames).Contains(permissionName);
    }

    /// <summary>
    /// 批量检查用户是否有多个权限
    /// 一次性获取用户权限并检查，比多次调用 CheckPermissionAsync 性能更好
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="permissionNames">权限名称列表</param>
    /// <returns>权限检查结果字典</returns>
    public async Task<Dictionary<string, bool>> CheckPermissionsAsync(Guid userId, IEnumerable<string> permissionNames)
    {
        var permissionNameList = permissionNames.ToList();
        if (permissionNameList.Count == 0)
        {
            return new Dictionary<string, bool>();
        }

        // SuperAdmin bypass - mirror CheckPermissionAsync exactly: supers pass
        // every code, enabled or not, declared or not. Without this the batch
        // path resolved through the ENABLED-only catalogue, so an endpoint
        // gate (single check) and an in-service RequireAllPermissionsAsync
        // (batch check) could reach opposite verdicts for the same super admin.
        if (await IsSuperAdminAsync(userId))
        {
            return permissionNameList.ToDictionary(p => p, _ => true);
        }

        // 一次性获取用户的所有权限名称（带缓存）
        var userPermissions = await GetUserPermissionNamesAsync(userId);
        // Case-insensitive match — see comment in CheckPermissionAsync for rationale.
        var userPermissionSet = ToCaseInsensitiveSet(userPermissions);

        return permissionNameList.ToDictionary(
            p => p,
            p => userPermissionSet.Contains(p)
        );
    }

    /// <summary>
    /// Build a case-insensitive lookup over a permission name collection.
    /// Centralised so every comparison site uses the same comparer.
    /// </summary>
    private static HashSet<string> ToCaseInsensitiveSet(IEnumerable<string> permissions)
        => new(permissions, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolve the super-admin permission catalogue with cache.
    /// Cache miss → single full-table scan + populate. Cache hit → O(1).
    /// </summary>
    private async Task<IReadOnlyList<string>> GetSuperAdminCatalogueAsync()
    {
        if (_functionAuthCache != null)
        {
            var cached = await _functionAuthCache.GetSuperAdminCatalogueAsync();
            if (cached != null) return cached;
        }

        var codes = await _moduleFunctionRepository
            .Where(f => f.IsEnabled)
            .Select(f => f.Code)
            .Distinct()
            .ToListAsync();

        if (_functionAuthCache != null)
        {
            await _functionAuthCache.SetSuperAdminCatalogueAsync(codes);
        }
        return codes;
    }

    /// <summary>
    /// 获取用户的所有权限名称
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>权限名称集合</returns>
    public async Task<IEnumerable<string>> GetUserPermissionNamesAsync(Guid userId)
    {
        // SuperAdmin bypass — return the full catalogue of enabled functions so
        // both the legacy "has X" check and any caller that enumerates the user's
        // permissions (menu builders, audit, etc.) sees a complete grant set.
        // The catalogue is identical for every super-admin so it lives in a
        // shared cache key (1h TTL), invalidated by Function/Module enable
        // changes. This avoids a full ModuleFunction scan per super-admin
        // request when a UI repeatedly enumerates permissions.
        if (await IsSuperAdminAsync(userId))
        {
            return await GetSuperAdminCatalogueAsync();
        }

        return await GetExplicitUserPermissionNamesAsync(userId);
    }

    /// <summary>
    /// Explicit effective permission names, tier-independent, with per-user
    /// cache. Resolution: <c>(RoleFunction ∪ user-allow) − user-deny</c> —
    /// a user-level deny row removes a code no matter which role granted it
    /// (user-level wins; super admins short-circuit upstream and are never
    /// affected by deny rows).
    /// </summary>
    private async Task<IEnumerable<string>> GetExplicitUserPermissionNamesAsync(Guid userId)
    {
        // 1. 尝试从缓存获取
        if (_functionAuthCache != null)
        {
            var cached = await _functionAuthCache.GetUserPermissionNamesAsync(userId);
            if (cached != null)
            {
                return cached;
            }
        }

        // 2. 缓存未命中，从数据库查询
        var userRoles = await GetUserRoleIdsAsync(userId);
        var roleIdList = userRoles.ToList();

        // 基础功能查询器（仅获取启用的功能）
        var enabledFunctions = _moduleFunctionRepository.Where(f => f.IsEnabled);

        // a. 用户角色直接绑定的具体功能
        var directRoleFunctionCodes = _roleFunctionRepository
            .Where(rf => roleIdList.Contains(rf.RoleId) && rf.IsEnabled)
            .Join(enabledFunctions, rf => rf.FunctionId, f => f.Id, (rf, f) => f.Code);

        // b. 用户直接授权的具体功能（不经角色）
        var directUserFunctionCodes = _userFunctionRepository
            .Where(uf => uf.UserId == userId && uf.IsEnabled && uf.IsGranted)
            .Join(enabledFunctions, uf => uf.FunctionId, f => f.Id, (uf, f) => f.Code);

        // c. 用户级否定权限（deny 行）从并集中扣除——"唯独这个用户不行"，
        //    角色给了也无效。缓存存的是扣除后的最终集，检查路径零额外成本。
        var deniedCodes = _userFunctionRepository
            .Where(uf => uf.UserId == userId && uf.IsEnabled && !uf.IsGranted)
            .Join(enabledFunctions, uf => uf.FunctionId, f => f.Id, (uf, f) => f.Code);

        // 合并去重再扣除，EF Core 翻译为 UNION / EXCEPT SQL
        var permissions = await directRoleFunctionCodes
            .Union(directUserFunctionCodes)
            .Except(deniedCodes)
            .ToListAsync();

        // 3. 结果存入缓存 (30分钟)
        if (_functionAuthCache != null)
        {
            await _functionAuthCache.SetUserPermissionNamesAsync(userId, permissions);
        }

        return permissions;
    }

    /// <summary>
    /// 检查用户是否有权限访问指定功能（返回 Result，用于 Controller）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="permissionName">权限名称</param>
    /// <returns>是否有权限</returns>
    public async Task<Result<bool>> CheckPermissionWithResultAsync(Guid userId, string permissionName)
    {
        if (string.IsNullOrEmpty(permissionName))
        {
            return Fail<bool>("Permission name cannot be empty", 400, ErrorCodes.VALIDATION_ERROR);
        }

        try
        {
            var hasPermission = await CheckPermissionAsync(userId, permissionName);
            return Ok(hasPermission);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error checking permission for user {UserId} and permission {PermissionName}", userId, permissionName);
            return Fail<bool>($"Error checking permission: {ex.Message}", 500, ErrorCodes.INTERNAL_SERVER_ERROR);
        }
    }

    /// <summary>
    /// 获取用户的所有权限名称（返回 Result，用于 Controller）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>权限名称集合</returns>
    public async Task<Result<IEnumerable<string>>> GetUserPermissionNamesWithResultAsync(Guid userId)
    {
        try
        {
            var permissionNames = await GetUserPermissionNamesAsync(userId);
            return Ok(permissionNames);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error getting user permission names for user {UserId}", userId);
            return Fail<IEnumerable<string>>($"Error getting user permission names: {ex.Message}", 500, ErrorCodes.INTERNAL_SERVER_ERROR);
        }
    }

    /// <summary>
    /// 获取模块树
    /// </summary>
    /// <returns>模块树</returns>
    public async Task<Result<IEnumerable<FunctionModule>>> GetModuleTreeAsync()
    {
        // 1. 获取所有启用的模块，并包含功能
        // 不需要 Include Children，我们手动构建树以确保层级正确且不受 EF Core 追踪行为影响
        var allModules = await _moduleRepository
            .Where(m => m.IsEnabled)
            .Include(m => m.Functions)
            .OrderBy(m => m.Order)
            .ToListAsync();

        // 2. 内存构建树
        var moduleDict = allModules.ToDictionary(m => m.Id);
        var rootModules = new List<FunctionModule>();

        foreach (var module in allModules)
        {
            // 只要有 ParentId 且父节点在已加载列表中，就作为子节点处理
            if (module.ParentId.HasValue && moduleDict.TryGetValue(module.ParentId.Value, out var parent))
            {
                // 确保不重复添加 (EF Core Fixup 可能会自动添加，这里做保险)
                if (!parent.Children.Contains(module))
                {
                    parent.Children.Add(module);
                }
            }
            else
            {
                // 否则作为根节点
                rootModules.Add(module);
            }
        }

        return Ok((IEnumerable<FunctionModule>)rootModules);
    }

    /// <summary>
    /// 获取模块的功能列表
    /// </summary>
    /// <param name="moduleId">模块ID</param>
    /// <returns>功能列表</returns>
    public async Task<Result<IEnumerable<ModuleFunction>>> GetModuleFunctionsAsync(Guid moduleId)
    {
        var functions = await _moduleFunctionRepository
            .Where(f => f.ModuleId == moduleId && f.IsEnabled)
            .OrderBy(f => f.Order)
            .ToListAsync();
        return Ok((IEnumerable<ModuleFunction>)functions);
    }

    /// <summary>
    /// 根据ID获取模块
    /// </summary>
    /// <param name="id">模块ID</param>
    /// <returns>模块信息</returns>
    public async Task<Result<FunctionModule>> GetModuleByIdAsync(Guid id)
    {
        var module = await _moduleRepository.FindAsync(id);
        if (module == null)
        {
            return Fail<FunctionModule>("Module not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }
        return Ok(module);
    }

    /// <summary>
    /// 获取所有模块
    /// </summary>
    /// <returns>模块列表</returns>
    public async Task<Result<IEnumerable<FunctionModule>>> GetModulesAsync()
    {
        var modules = await _moduleRepository
            .OrderBy(m => m.Order)
            .ToListAsync();
        return Ok((IEnumerable<FunctionModule>)modules);
    }

    /// <summary>
    /// 创建模块
    /// </summary>
    /// <param name="request">模块信息</param>
    /// <returns>创建的模块</returns>
    public async Task<Result<FunctionModule>> CreateModuleAsync(CreateFunctionModuleRequest request)
    {
        // 检查代码是否已存在
        var exists = await _moduleRepository
            .Where(m => m.Code == request.Code)
            .AnyAsync();

        if (exists)
        {
            return Fail<FunctionModule>($"Module with code '{request.Code}' already exists", 409, ErrorCodes.VALIDATION_ERROR);
        }

        // 如果指定了父模块，验证父模块存在
        if (request.ParentId.HasValue)
        {
            var parentExists = await _moduleRepository
                .Where(m => m.Id == request.ParentId.Value)
                .AnyAsync();

            if (!parentExists)
            {
                return Fail<FunctionModule>($"Parent module not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
            }
        }

        var module = request.MapTo<FunctionModule>();
        module.IsEnabled = true;

        await _moduleRepository.InsertAsync(module);
        LogInformation("Module created: {Code}, Name: {Name}", request.Code, request.Name);
        return Ok(module, "Module created successfully");
    }

    /// <summary>
    /// 更新模块
    /// </summary>
    /// <param name="id">模块ID</param>
    /// <param name="request">模块信息</param>
    /// <returns>更新后的模块</returns>
    public async Task<Result<FunctionModule>> UpdateModuleAsync(Guid id, UpdateFunctionModuleRequest request)
    {
        var module = await _moduleRepository.FindAsync(id);
        if (module == null)
        {
            return Fail<FunctionModule>("Module not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        // System-managed rows: only IsEnabled / Order are editable. Code /
        // Name / Description / ParentId mirror the IPermissionDefinitionProvider
        // declaration and changing them via admin UI would drift from code-
        // as-truth on next startup (seeder restores). Treat attempts as 403
        // so admin sees the constraint, not as silent revert.
        if (module.IsSystemManaged && !string.Equals(module.Code, request.Code, StringComparison.Ordinal))
        {
            return Fail<FunctionModule>(
                "System-managed module code cannot be changed. Update the IPermissionDefinitionProvider declaration in code instead.",
                403, ErrorCodes.IDENTITY_ROLE_SYSTEM_PROTECTED);
        }

        // 检查代码是否被其他模块使用
        if (module.Code != request.Code)
        {
            var codeExists = await _moduleRepository
                .Where(m => m.Code == request.Code && m.Id != id)
                .AnyAsync();

            if (codeExists)
            {
                return Fail<FunctionModule>($"Module with code '{request.Code}' already exists", 409, ErrorCodes.VALIDATION_ERROR);
            }
        }

        // 如果指定了父模块，验证父模块存在且不是自己
        if (request.ParentId.HasValue)
        {
            if (request.ParentId.Value == id)
            {
                return Fail<FunctionModule>("Module cannot be its own parent", 400, ErrorCodes.VALIDATION_ERROR);
            }

            var parentExists = await _moduleRepository
                .Where(m => m.Id == request.ParentId.Value)
                .AnyAsync();

            if (!parentExists)
            {
                return Fail<FunctionModule>("Parent module not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
            }
        }

        var oldEnabled = module.IsEnabled;
        request.MapTo(module);
        await _moduleRepository.UpdateAsync(module);
        
        if (oldEnabled != module.IsEnabled)
        {
            await InvalidateAllCacheAsync();
        }

        LogInformation("Module updated: {Code}, Name: {Name}", request.Code, request.Name);
        return Ok(module, "Module updated successfully");
    }

    /// <summary>
    /// 删除模块
    /// </summary>
    /// <param name="id">模块ID</param>
    public async Task<Result> DeleteModuleAsync(Guid id)
    {
        var module = await _moduleRepository.FindAsync(id);
        if (module == null)
        {
            return Fail("Module not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        // System-managed rows are owned by code — delete attempts via admin
        // UI would resurrect on next startup (seeder re-inserts). Refuse so
        // admin sees the constraint instead of silent "phantom" rebirth.
        if (module.IsSystemManaged)
        {
            return Fail(
                "System-managed module cannot be deleted. Remove the IPermissionDefinitionProvider declaration in code instead.",
                403, ErrorCodes.IDENTITY_ROLE_SYSTEM_PROTECTED);
        }

        // 检查是否有子模块
        var hasChildren = await _moduleRepository
            .Where(m => m.ParentId == id)
            .AnyAsync();

        if (hasChildren)
        {
            return Fail("Cannot delete module with child modules", 400, ErrorCodes.VALIDATION_ERROR);
        }

        // 检查是否有功能
        var hasFunctions = await _moduleFunctionRepository
            .Where(f => f.ModuleId == id)
            .AnyAsync();

        if (hasFunctions)
        {
            return Fail("Cannot delete module with functions", 400, ErrorCodes.VALIDATION_ERROR);
        }

        await _moduleRepository.DeleteAsync(module);
        await InvalidateAllCacheAsync();
        LogInformation("Module deleted: {Code}, Name: {Name}", module.Code, module.Name);
        return Ok("Module deleted successfully");
    }

    /// <summary>
    /// 获取模块的子模块
    /// </summary>
    /// <param name="parentId">父模块ID</param>
    /// <returns>子模块列表</returns>
    public async Task<Result<IEnumerable<FunctionModule>>> GetChildModulesAsync(Guid parentId)
    {
        var modules = await _moduleRepository
            .Where(m => m.ParentId == parentId)
            .OrderBy(m => m.Order)
            .ToListAsync();
        return Ok((IEnumerable<FunctionModule>)modules);
    }

    /// <summary>
    /// Paged list of role-function assignments across all roles. Supports
    /// optional filters on roleId / functionId / enabled state.
    /// </summary>
    public async Task<Result<IPagedList<RoleFunctionDto>>> GetRoleFunctionsPagedAsync(RoleFunctionQueryDto query)
    {
        Check.NotNull(query);

        var filtered = _roleFunctionRepository.Where(rf =>
            (!query.RoleId.HasValue || rf.RoleId == query.RoleId.Value) &&
            (!query.FunctionId.HasValue || rf.FunctionId == query.FunctionId.Value) &&
            (!query.IsEnabled.HasValue || rf.IsEnabled == query.IsEnabled.Value));

        var totalCount = await filtered.CountAsync();

        var pageItems = await filtered
            .OrderByDescending(rf => rf.CreationTime)
            .Skip(query.Skip)
            .Take(query.Take)
            .ToListAsync();

        if (pageItems.Count == 0)
        {
            var empty = new PagedList<RoleFunctionDto>(
                Array.Empty<RoleFunctionDto>(),
                query.PageIndex,
                query.PageSize,
                totalCount);
            return Ok<IPagedList<RoleFunctionDto>>(empty);
        }

        // Join to ModuleFunction for denormalized Code / Name / ModuleId.
        // We do this in a second round-trip rather than via Include/join because
        // the repository abstraction here returns plain IQueryable<RoleFunction>
        // and we only need a small distinct set of function ids for the current page.
        var functionIds = pageItems.Select(rf => rf.FunctionId).Distinct().ToList();
        var functions = await _moduleFunctionRepository
            .Where(f => functionIds.Contains(f.Id))
            .ToListAsync();
        var functionsById = functions.ToDictionary(f => f.Id);

        var dtos = pageItems
            .Select(rf =>
            {
                functionsById.TryGetValue(rf.FunctionId, out var fn);
                return new RoleFunctionDto
                {
                    Id = rf.Id,
                    RoleId = rf.RoleId,
                    FunctionId = rf.FunctionId,
                    FunctionCode = fn?.Code ?? string.Empty,
                    FunctionName = fn?.Name ?? string.Empty,
                    ModuleId = fn?.ModuleId ?? Guid.Empty,
                    IsEnabled = rf.IsEnabled,
                    CreationTime = rf.CreationTime,
                };
            })
            .ToList();

        var paged = new PagedList<RoleFunctionDto>(dtos, query.PageIndex, query.PageSize, totalCount);
        return Ok<IPagedList<RoleFunctionDto>>(paged);
    }

    /// <summary>
    /// 获取角色的功能列表
    /// </summary>
    /// <param name="roleId">角色ID</param>
    /// <returns>功能列表</returns>
    public async Task<Result<IEnumerable<ModuleFunction>>> GetRoleFunctionsAsync(Guid roleId)
    {
        var functionIds = await _roleFunctionRepository
            .Where(rf => rf.RoleId == roleId && rf.IsEnabled)
            .Select(rf => rf.FunctionId)
            .ToListAsync();

        if (functionIds.Count == 0)
        {
            return Ok((IEnumerable<ModuleFunction>)Enumerable.Empty<ModuleFunction>());
        }

        var functions = await _moduleFunctionRepository
            .Where(f => functionIds.Contains(f.Id) && f.IsEnabled)
            .OrderBy(f => f.Order)
            .ToListAsync();
        return Ok((IEnumerable<ModuleFunction>)functions);
    }

    /// <summary>
    /// 获取角色的功能ID列表
    /// </summary>
    /// <param name="roleId">角色ID</param>
    /// <returns>功能ID列表</returns>
    public async Task<Result<IEnumerable<Guid>>> GetRoleFunctionIdsAsync(Guid roleId)
    {
        var functionIds = await _roleFunctionRepository
            .Where(rf => rf.RoleId == roleId && rf.IsEnabled)
            .Select(rf => rf.FunctionId)
            .ToListAsync();
        return Ok((IEnumerable<Guid>)functionIds);
    }

    /// <summary>
    /// 分配功能到角色
    /// </summary>
    /// <param name="roleId">角色ID</param>
    /// <param name="functionIds">功能ID列表</param>
    public async Task<Result> AssignFunctionsToRoleAsync(Guid roleId, IEnumerable<Guid> functionIds)
    {
        var functionIdList = functionIds.ToList();
        if (functionIdList.Count == 0)
        {
            return Ok("No functions to assign");
        }

        var violation = await GetRoleGrantViolationAsync(roleId, functionIdList);
        if (violation != null)
        {
            return Fail(violation, 403, ErrorCodes.FORBIDDEN);
        }

        // 验证功能是否存在
        var existingFunctions = await _moduleFunctionRepository
            .Where(f => functionIdList.Contains(f.Id) && f.IsEnabled)
            .Select(f => f.Id)
            .ToListAsync();

        var missingFunctions = functionIdList.Except(existingFunctions).ToList();
        if (missingFunctions.Count > 0)
        {
            return Fail($"Functions not found: {string.Join(", ", missingFunctions)}", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        // 获取已存在的角色功能关联
        var existingRoleFunctions = await _roleFunctionRepository
            .Where(rf => rf.RoleId == roleId && functionIdList.Contains(rf.FunctionId))
            .Select(rf => rf.FunctionId)
            .ToListAsync();

        // 只添加不存在的关联（批量插入）
        var newFunctionIds = functionIdList.Except(existingRoleFunctions).ToList();
        if (newFunctionIds.Count > 0)
        {
            var roleFunctions = newFunctionIds.Select(functionId => new RoleFunction
            {
                RoleId = roleId,
                FunctionId = functionId,
                IsEnabled = true
            }).ToList();
            await _roleFunctionRepository.InsertManyAsync(roleFunctions);
        }

        // 清除该角色下所有用户的权限缓存
        await InvalidateRoleCacheAsync(roleId);
        await PublishRoleFunctionsChangedAsync(roleId, PermissionChangeType.Assigned, newFunctionIds);
        LogInformation("Assigned {Count} functions to role: {RoleId}", newFunctionIds.Count, roleId);
        return Ok($"Assigned {newFunctionIds.Count} functions to role");
    }

    /// <summary>
    /// 从角色移除功能
    /// </summary>
    /// <param name="roleId">角色ID</param>
    /// <param name="functionIds">功能ID列表</param>
    public async Task<Result> RemoveFunctionsFromRoleAsync(Guid roleId, IEnumerable<Guid> functionIds)
    {
        var functionIdList = functionIds.ToList();
        if (functionIdList.Count == 0)
        {
            return Ok("No functions to remove");
        }

        // 回收虽不授出新权限,但支配约束仍适用——弱管理员不得剥夺强角色的权限。
        var violation = await GetRoleGrantViolationAsync(roleId);
        if (violation != null)
        {
            return Fail(violation, 403, ErrorCodes.FORBIDDEN);
        }

        // 批量删除
        await _roleFunctionRepository.DeleteAsync(rf => rf.RoleId == roleId && functionIdList.Contains(rf.FunctionId));

        // 清除该角色下所有用户的权限缓存
        await InvalidateRoleCacheAsync(roleId);
        await PublishRoleFunctionsChangedAsync(roleId, PermissionChangeType.Removed, functionIdList);
        LogInformation("Removed functions from role: {RoleId}", roleId);
        return Ok("Functions removed from role");
    }

    /// <summary>
    /// 设置角色的功能（覆盖原有功能）
    /// </summary>
    /// <param name="roleId">角色ID</param>
    /// <param name="functionIds">功能ID列表</param>
    public async Task<Result> SetRoleFunctionsAsync(Guid roleId, IEnumerable<Guid> functionIds)
    {
        var functionIdList = functionIds.ToList();

        var violation = await GetRoleGrantViolationAsync(roleId, functionIdList);
        if (violation != null)
        {
            return Fail(violation, 403, ErrorCodes.FORBIDDEN);
        }

        // 验证新功能是否存在
        if (functionIdList.Count > 0)
        {
            var existingFunctions = await _moduleFunctionRepository
                .Where(f => functionIdList.Contains(f.Id) && f.IsEnabled)
                .Select(f => f.Id)
                .ToListAsync();

            var missingFunctions = functionIdList.Except(existingFunctions).ToList();
            if (missingFunctions.Count > 0)
            {
                return Fail($"Functions not found: {string.Join(", ", missingFunctions)}", 404, ErrorCodes.RESOURCE_NOT_FOUND);
            }
        }

        // 原子操作：在同一个 UnitOfWork 中删除旧关联并创建新关联，避免权限窗口期
        var result = await ExecuteInUnitOfWorkAsync(async _ =>
        {
            // 删除旧关联
            await _roleFunctionRepository.DeleteAsync(rf => rf.RoleId == roleId);

            // 创建新关联
            if (functionIdList.Count > 0)
            {
                var roleFunctions = functionIdList.Select(functionId => new RoleFunction
                {
                    RoleId = roleId,
                    FunctionId = functionId,
                    IsEnabled = true
                }).ToList();
                await _roleFunctionRepository.InsertManyAsync(roleFunctions);
            }

            LogInformation("Set {Count} functions for role: {RoleId}", functionIdList.Count, roleId);
            return Ok($"Set {functionIdList.Count} functions for role");
        });

        if (result.Succeeded)
        {
            await InvalidateRoleCacheAsync(roleId);
            await PublishRoleFunctionsChangedAsync(roleId, PermissionChangeType.Reset, functionIdList);
        }

        return result;
    }

    /// <summary>
    /// 检查角色是否有指定功能
    /// </summary>
    /// <param name="roleId">角色ID</param>
    /// <param name="functionId">功能ID</param>
    /// <returns>是否有权限</returns>
    public async Task<Result<bool>> RoleHasFunctionAsync(Guid roleId, Guid functionId)
    {
        var hasFunction = await _roleFunctionRepository
            .Where(rf => rf.RoleId == roleId && rf.FunctionId == functionId && rf.IsEnabled)
            .AnyAsync();
        return Ok(hasFunction);
    }

    /// <summary>
    /// 获取功能的角色列表
    /// </summary>
    /// <param name="functionId">功能ID</param>
    /// <returns>角色功能列表</returns>
    public async Task<Result<IEnumerable<RoleFunction>>> GetFunctionRolesAsync(Guid functionId)
    {
        var roleFunctions = await _roleFunctionRepository
            .Where(rf => rf.FunctionId == functionId && rf.IsEnabled)
            .ToListAsync();
        return Ok((IEnumerable<RoleFunction>)roleFunctions);
    }

    /// <summary>
    /// 批量分配功能到多个角色
    /// </summary>
    /// <param name="roleIds">角色ID列表</param>
    /// <param name="functionIds">功能ID列表</param>
    public async Task<Result> BatchAssignFunctionsAsync(IEnumerable<Guid> roleIds, IEnumerable<Guid> functionIds)
    {
        var roleIdList = roleIds.ToList();
        var functionIdList = functionIds.ToList();

        if (roleIdList.Count == 0 || functionIdList.Count == 0)
        {
            return Ok("No roles or functions to assign");
        }

        // Pre-check the delegation guard for EVERY target role before writing
        // anything - the per-role assign below commits one role at a time, so
        // a mid-loop guard failure would otherwise leave a partial batch.
        foreach (var roleId in roleIdList)
        {
            var violation = await GetRoleGrantViolationAsync(roleId, functionIdList);
            if (violation != null)
            {
                return Fail(violation, 403, ErrorCodes.FORBIDDEN);
            }
        }

        foreach (var roleId in roleIdList)
        {
            var result = await AssignFunctionsToRoleAsync(roleId, functionIdList);
            if (!result.Succeeded)
            {
                return result;
            }
        }

        LogInformation("Batch assigned functions to {Count} roles", roleIdList.Count);
        // 缓存已在 AssignFunctionsToRoleAsync 中清除，无需重复
        return Ok($"Batch assigned functions to {roleIdList.Count} roles");
    }

    /// <summary>
    /// 清空角色的所有功能
    /// </summary>
    /// <param name="roleId">角色ID</param>
    public async Task<Result> ClearRoleFunctionsAsync(Guid roleId)
    {
        // 清空同样受支配约束——弱管理员不得清空强角色。
        var violation = await GetRoleGrantViolationAsync(roleId);
        if (violation != null)
        {
            return Fail(violation, 403, ErrorCodes.FORBIDDEN);
        }

        // 批量删除
        await _roleFunctionRepository.DeleteAsync(rf => rf.RoleId == roleId);

        // 清除该角色下所有用户的权限缓存
        await InvalidateRoleCacheAsync(roleId);
        await PublishRoleFunctionsChangedAsync(roleId, PermissionChangeType.Cleared, []);
        LogInformation("Cleared all functions for role: {RoleId}", roleId);
        return Ok("Cleared all functions for role");
    }

    /// <summary>
    /// 根据ID获取功能
    /// </summary>
    /// <param name="id">功能ID</param>
    /// <returns>功能信息</returns>
    public async Task<Result<ModuleFunction>> GetModuleFunctionByIdAsync(Guid id)
    {
        var function = await _moduleFunctionRepository.FindAsync(id);
        if (function == null)
        {
            return Fail<ModuleFunction>("Module function not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }
        return Ok(function);
    }

    /// <summary>
    /// 创建功能
    /// </summary>
    /// <param name="request">功能信息</param>
    /// <returns>创建的功能</returns>
    public async Task<Result<ModuleFunction>> CreateModuleFunctionAsync(CreateModuleFunctionRequest request)
    {
        // 检查代码是否已存在
        var codeExists = await _moduleFunctionRepository
            .Where(f => f.Code == request.Code)
            .AnyAsync();

        if (codeExists)
        {
            return Fail<ModuleFunction>($"Function with code '{request.Code}' already exists", 409, ErrorCodes.VALIDATION_ERROR);
        }

        // 验证模块存在
        var moduleExists = await _moduleRepository
            .Where(m => m.Id == request.ModuleId)
            .AnyAsync();

        if (!moduleExists)
        {
            return Fail<ModuleFunction>("Module not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        var function = request.MapTo<ModuleFunction>();
        function.IsEnabled = true;

        await _moduleFunctionRepository.InsertAsync(function);
        // A new enabled function belongs in the super-admin catalogue
        // immediately; without this, it would be invisible to super admins
        // until the catalogue TTL (1h) expires.
        if (_functionAuthCache != null)
        {
            await _functionAuthCache.InvalidateSuperAdminCatalogueAsync();
        }
        LogInformation("Module function created: {Code}, Name: {Name}", request.Code, request.Name);
        return Ok(function, "Module function created successfully");
    }

    /// <summary>
    /// 更新功能
    /// </summary>
    /// <param name="id">功能ID</param>
    /// <param name="request">功能信息</param>
    /// <returns>更新后的功能</returns>
    public async Task<Result<ModuleFunction>> UpdateModuleFunctionAsync(Guid id, UpdateModuleFunctionRequest request)
    {
        var function = await _moduleFunctionRepository.FindAsync(id);
        if (function == null)
        {
            return Fail<ModuleFunction>("Module function not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        // System-managed: Code/Name/ModuleId are owned by the
        // IPermissionDefinitionProvider; admin can only flip IsEnabled.
        if (function.IsSystemManaged)
        {
            if (!string.Equals(function.Code, request.Code, StringComparison.Ordinal))
            {
                return Fail<ModuleFunction>(
                    "System-managed function code cannot be changed via admin UI. Update the IPermissionDefinitionProvider declaration in code instead.",
                    403, ErrorCodes.IDENTITY_ROLE_SYSTEM_PROTECTED);
            }
            if (function.ModuleId != request.ModuleId)
            {
                return Fail<ModuleFunction>(
                    "System-managed function cannot be moved to a different module.",
                    403, ErrorCodes.IDENTITY_ROLE_SYSTEM_PROTECTED);
            }
        }

        // 检查代码是否被其他功能使用
        if (function.Code != request.Code)
        {
            var codeExists = await _moduleFunctionRepository
                .Where(f => f.Code == request.Code && f.Id != id)
                .AnyAsync();

            if (codeExists)
            {
                return Fail<ModuleFunction>($"Function with code '{request.Code}' already exists", 409, ErrorCodes.VALIDATION_ERROR);
            }
        }

        // 验证模块存在
        var moduleExists = await _moduleRepository
            .Where(m => m.Id == request.ModuleId)
            .AnyAsync();

        if (!moduleExists)
        {
            return Fail<ModuleFunction>("Module not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        // Category needs guarding around the blanket MapTo below:
        // - system-managed rows: category is a code-owned contract (the seeder
        //   re-asserts it), protected like Code/ModuleId — admin edits ignored;
        // - admin-created rows: apply only when the request explicitly provides
        //   a value. A nullable-with-default would silently downgrade Technical
        //   to Business on any update that omits the field, erasing the
        //   warning badge assignment UIs rely on to flag dangerous surfaces.
        var currentCategory = function.Category;
        request.MapTo(function);
        function.Category = function.IsSystemManaged
            ? currentCategory
            : request.Category ?? currentCategory;
        await _moduleFunctionRepository.UpdateAsync(function);
        // 全局清理功能权限缓存，因为功能变更可能影响广泛
        await InvalidateAllCacheAsync();
        LogInformation("Module function updated: {Code}, Name: {Name}", request.Code, request.Name);
        return Ok(function, "Module function updated successfully");
    }

    /// <summary>
    /// 删除功能
    /// </summary>
    /// <param name="id">功能ID</param>
    public async Task<Result> DeleteModuleFunctionAsync(Guid id)
    {
        var function = await _moduleFunctionRepository.FindAsync(id);
        if (function == null)
        {
            return Fail("Module function not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        // System-managed: the seeder will re-insert this row on next
        // startup anyway. Refuse so admin doesn't see "phantom row" behaviour.
        if (function.IsSystemManaged)
        {
            return Fail(
                "System-managed function cannot be deleted. Remove the IPermissionDefinitionProvider declaration in code instead.",
                403, ErrorCodes.IDENTITY_ROLE_SYSTEM_PROTECTED);
        }

        // 检查是否有角色关联
        var hasRoleFunctions = await _roleFunctionRepository
            .Where(rf => rf.FunctionId == id)
            .AnyAsync();

        if (hasRoleFunctions)
        {
            return Fail("Cannot delete function with role associations", 400, ErrorCodes.VALIDATION_ERROR);
        }

        // 检查是否有用户直授关联
        var hasUserFunctions = await _userFunctionRepository
            .Where(uf => uf.FunctionId == id)
            .AnyAsync();

        if (hasUserFunctions)
        {
            return Fail("Cannot delete function with user direct-grant associations", 400, ErrorCodes.VALIDATION_ERROR);
        }

        await _moduleFunctionRepository.DeleteAsync(function);
        await InvalidateAllCacheAsync();
        LogInformation("Module function deleted: {Code}, Name: {Name}", function.Code, function.Name);
        return Ok("Module function deleted successfully");
    }

    /// <summary>
    /// 启用功能
    /// </summary>
    /// <param name="id">功能ID</param>
    public async Task<Result> EnableModuleFunctionAsync(Guid id)
    {
        var function = await _moduleFunctionRepository.FindAsync(id);
        if (function == null)
        {
            return Fail("Module function not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        function.IsEnabled = true;
        await _moduleFunctionRepository.UpdateAsync(function);
        await InvalidateCacheForFunctionAsync(function.Id);
        await PublishFunctionEnabledChangedAsync(function.Id, function.Code, function.ModuleId, true);
        LogInformation("Module function enabled: {Code}, Name: {Name}", function.Code, function.Name);
        return Ok("Module function enabled successfully");
    }

    /// <summary>
    /// 禁用功能
    /// </summary>
    /// <param name="id">功能ID</param>
    public async Task<Result> DisableModuleFunctionAsync(Guid id)
    {
        var function = await _moduleFunctionRepository.FindAsync(id);
        if (function == null)
        {
            return Fail("Module function not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        function.IsEnabled = false;
        await _moduleFunctionRepository.UpdateAsync(function);
        await InvalidateCacheForFunctionAsync(function.Id);
        await PublishFunctionEnabledChangedAsync(function.Id, function.Code, function.ModuleId, false);
        LogInformation("Module function disabled: {Code}, Name: {Name}", function.Code, function.Name);
        return Ok("Module function disabled successfully");
    }

    /// <summary>
    /// 启用模块（级联启用所有子模块及其功能）
    /// </summary>
    /// <param name="id">模块ID</param>
    public async Task<Result> EnableModuleAsync(Guid id)
    {
        var module = await _moduleRepository.FindAsync(id);
        if (module == null)
        {
            return Fail("Module not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        if (module.IsEnabled)
        {
            return Ok("Module is already enabled");
        }

        List<Guid>? affectedIds = null;
        var result = await ExecuteInUnitOfWorkAsync(async _ =>
        {
            affectedIds = await SetModuleEnabledCascadeAsync(id, true);
            await InvalidateAllCacheAsync();
            LogInformation("Module enabled (cascade): {Code}, affected {Count} modules", module.Code, affectedIds.Count);
            return Ok($"Module enabled successfully, affected {affectedIds.Count} modules");
        });

        if (result.Succeeded && affectedIds != null)
        {
            await PublishModuleEnabledChangedAsync(id, module.Code, true, affectedIds);
        }

        return result;
    }

    /// <summary>
    /// 禁用模块（级联禁用所有子模块及其功能）
    /// </summary>
    /// <param name="id">模块ID</param>
    public async Task<Result> DisableModuleAsync(Guid id)
    {
        var module = await _moduleRepository.FindAsync(id);
        if (module == null)
        {
            return Fail("Module not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        if (!module.IsEnabled)
        {
            return Ok("Module is already disabled");
        }

        List<Guid>? affectedIds = null;
        var result = await ExecuteInUnitOfWorkAsync(async _ =>
        {
            affectedIds = await SetModuleEnabledCascadeAsync(id, false);
            await InvalidateAllCacheAsync();
            LogInformation("Module disabled (cascade): {Code}, affected {Count} modules", module.Code, affectedIds.Count);
            return Ok($"Module disabled successfully, affected {affectedIds.Count} modules");
        });

        if (result.Succeeded && affectedIds != null)
        {
            await PublishModuleEnabledChangedAsync(id, module.Code, false, affectedIds);
        }

        return result;
    }

    /// <summary>
    /// Reverse permission query: get all roles that have a specific permission
    /// </summary>
    /// <param name="permissionName">Permission name (function code)</param>
    /// <returns>List of roles with the permission</returns>
    public async Task<Result<IEnumerable<PermissionRoleDto>>> GetPermissionRolesAsync(string permissionName)
    {
        if (string.IsNullOrWhiteSpace(permissionName))
        {
            return Fail<IEnumerable<PermissionRoleDto>>("Permission name cannot be empty", 400, ErrorCodes.VALIDATION_ERROR);
        }

        // Find the function by code
        var function = await _moduleFunctionRepository
            .Where(f => f.Code == permissionName)
            .FirstOrDefaultAsync();

        if (function == null)
        {
            return Fail<IEnumerable<PermissionRoleDto>>($"Permission '{permissionName}' not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        // Find all role-function assignments for this function
        var roleFunctions = await _roleFunctionRepository
            .Where(rf => rf.FunctionId == function.Id)
            .ToListAsync();

        var result = roleFunctions.Select(rf => new PermissionRoleDto
        {
            RoleId = rf.RoleId,
            IsEnabled = rf.IsEnabled,
            FunctionId = function.Id,
            FunctionCode = function.Code,
            FunctionName = function.Name
        });

        return Ok(result);
    }

    /// <summary>
    /// Reverse permission query: get all user IDs that have a specific
    /// permission, via roles or via direct UserFunction grants. Direct-grant
    /// users carry an empty RoleIds list.
    /// </summary>
    /// <param name="permissionName">Permission name (function code)</param>
    /// <returns>List of users with the permission</returns>
    public async Task<Result<IEnumerable<PermissionUserDto>>> GetPermissionUsersAsync(string permissionName)
    {
        if (string.IsNullOrWhiteSpace(permissionName))
        {
            return Fail<IEnumerable<PermissionUserDto>>("Permission name cannot be empty", 400, ErrorCodes.VALIDATION_ERROR);
        }

        if (_userRoleService == null)
        {
            return Fail<IEnumerable<PermissionUserDto>>("User role service is not available", 503, ErrorCodes.INTERNAL_SERVER_ERROR);
        }

        // Find the function by code
        var function = await _moduleFunctionRepository
            .Where(f => f.Code == permissionName)
            .FirstOrDefaultAsync();

        if (function == null)
        {
            return Fail<IEnumerable<PermissionUserDto>>($"Permission '{permissionName}' not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        // Find all roles that have this function (enabled only)
        var roleIds = await _roleFunctionRepository
            .Where(rf => rf.FunctionId == function.Id && rf.IsEnabled)
            .Select(rf => rf.RoleId)
            .Distinct()
            .ToListAsync();

        // For each role, get the users
        var userRoleMap = new Dictionary<Guid, List<Guid>>(); // userId -> roleIds
        foreach (var roleId in roleIds)
        {
            var userIds = await _userRoleService.GetRoleUserIdsAsync(roleId);
            foreach (var userId in userIds)
            {
                if (!userRoleMap.TryGetValue(userId, out var roles))
                {
                    roles = new List<Guid>();
                    userRoleMap[userId] = roles;
                }
                roles.Add(roleId);
            }
        }

        // Users holding the function via a direct grant (no role involved).
        // They appear with an empty RoleIds list unless a role also grants it.
        var directUserIds = await _userFunctionRepository
            .Where(uf => uf.FunctionId == function.Id && uf.IsEnabled && uf.IsGranted)
            .Select(uf => uf.UserId)
            .Distinct()
            .ToListAsync();
        foreach (var userId in directUserIds)
        {
            if (!userRoleMap.ContainsKey(userId))
            {
                userRoleMap[userId] = new List<Guid>();
            }
        }

        // User-level deny rows override every grant path — drop those users
        // so the reverse query mirrors the actual resolution semantics.
        var deniedUserIds = await _userFunctionRepository
            .Where(uf => uf.FunctionId == function.Id && uf.IsEnabled && !uf.IsGranted)
            .Select(uf => uf.UserId)
            .Distinct()
            .ToListAsync();
        foreach (var userId in deniedUserIds)
        {
            userRoleMap.Remove(userId);
        }

        var result = userRoleMap.Select(kvp => new PermissionUserDto
        {
            UserId = kvp.Key,
            RoleIds = kvp.Value
        });

        return Ok(result);
    }

    /// <summary>
    /// Get authorization statistics overview
    /// </summary>
    /// <returns>Authorization statistics</returns>
    public async Task<Result<AuthorizationStatisticsDto>> GetStatisticsAsync()
    {
        // Single query approach using GroupBy for modules
        var moduleStats = await _moduleRepository.AsQueryable()
            .GroupBy(o => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Enabled = g.Count(m => m.IsEnabled)
            })
            .FirstOrDefaultAsync();

        // Single query approach using GroupBy for functions
        var functionStats = await _moduleFunctionRepository.AsQueryable()
            .GroupBy(o => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Enabled = g.Count(f => f.IsEnabled)
            })
            .FirstOrDefaultAsync();

        // Single query approach using GroupBy for role-function assignments
        var rfStats = await _roleFunctionRepository.AsQueryable()
            .GroupBy(o => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Enabled = g.Count(rf => rf.IsEnabled)
            })
            .FirstOrDefaultAsync();

        // Single query approach using GroupBy for user-function direct grants
        var ufStats = await _userFunctionRepository.AsQueryable()
            .GroupBy(o => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Enabled = g.Count(uf => uf.IsEnabled)
            })
            .FirstOrDefaultAsync();

        var statistics = new AuthorizationStatisticsDto
        {
            TotalModules = moduleStats?.Total ?? 0,
            EnabledModules = moduleStats?.Enabled ?? 0,
            TotalFunctions = functionStats?.Total ?? 0,
            EnabledFunctions = functionStats?.Enabled ?? 0,
            TotalRoleFunctionAssignments = rfStats?.Total ?? 0,
            EnabledRoleFunctionAssignments = rfStats?.Enabled ?? 0,
            TotalUserFunctionAssignments = ufStats?.Total ?? 0,
            EnabledUserFunctionAssignments = ufStats?.Enabled ?? 0
        };

        return Ok(statistics);
    }

    /// <summary>
    /// Resolve the user's access profile - effective permission codes plus
    /// the backend-authoritative super-admin flag.
    /// </summary>
    public async Task<Result<AccessProfileDto>> GetAccessProfileAsync(Guid userId)
    {
        var isSuperAdmin = await IsSuperAdminAsync(userId);
        var permissions = await GetUserPermissionNamesAsync(userId);
        return Ok(new AccessProfileDto
        {
            IsSuperAdmin = isSuperAdmin,
            Permissions = permissions.ToList(),
        });
    }

    /// <summary>
    /// Compare permissions between two roles
    /// </summary>
    public async Task<Result<PermissionComparisonDto>> CompareRolePermissionsAsync(Guid roleId1, Guid roleId2)
    {
        // Get function IDs for both roles (enabled only)
        var functionIds1 = await _roleFunctionRepository
            .Where(rf => rf.RoleId == roleId1 && rf.IsEnabled)
            .Select(rf => rf.FunctionId)
            .ToListAsync();

        var functionIds2 = await _roleFunctionRepository
            .Where(rf => rf.RoleId == roleId2 && rf.IsEnabled)
            .Select(rf => rf.FunctionId)
            .ToListAsync();

        var set1 = new HashSet<Guid>(functionIds1);
        var set2 = new HashSet<Guid>(functionIds2);

        var onlyIn1 = set1.Except(set2).ToList();
        var onlyIn2 = set2.Except(set1).ToList();
        var shared = set1.Intersect(set2).ToList();

        // Load function details for all referenced IDs
        var allIds = onlyIn1.Concat(onlyIn2).Concat(shared).Distinct().ToList();
        var functions = allIds.Count > 0
            ? await _moduleFunctionRepository
                .Where(f => allIds.Contains(f.Id))
                .Include(f => f.FunctionModule)
                .ToListAsync()
            : [];

        var functionMap = functions.ToDictionary(f => f.Id);

        FunctionSummaryDto ToSummary(Guid id) => functionMap.TryGetValue(id, out var f)
            ? new FunctionSummaryDto { Id = f.Id, Code = f.Code, Name = f.Name, ModuleCode = f.FunctionModule?.Code }
            : new FunctionSummaryDto { Id = id };

        var result = new PermissionComparisonDto
        {
            RoleId1 = roleId1,
            RoleId2 = roleId2,
            OnlyInRole1 = onlyIn1.Select(ToSummary).ToList(),
            OnlyInRole2 = onlyIn2.Select(ToSummary).ToList(),
            Shared = shared.Select(ToSummary).ToList()
        };

        return Ok(result);
    }

    /// <summary>
    /// Clone all function assignments from source role to target role
    /// </summary>
    public async Task<Result<int>> CloneRoleFunctionsAsync(Guid sourceRoleId, Guid targetRoleId)
    {
        if (sourceRoleId == targetRoleId)
        {
            return Fail<int>("Source and target role cannot be the same", 400, ErrorCodes.VALIDATION_ERROR);
        }

        // Get source role's enabled function IDs
        var sourceFunctionIds = await _roleFunctionRepository
            .Where(rf => rf.RoleId == sourceRoleId && rf.IsEnabled)
            .Select(rf => rf.FunctionId)
            .ToListAsync();

        if (sourceFunctionIds.Count == 0)
        {
            return Ok(0);
        }

        var violation = await GetRoleGrantViolationAsync(targetRoleId, sourceFunctionIds);
        if (violation != null)
        {
            return Fail<int>(violation, 403, ErrorCodes.FORBIDDEN);
        }

        // Get target role's existing function IDs
        var existingTargetFunctionIds = await _roleFunctionRepository
            .Where(rf => rf.RoleId == targetRoleId && sourceFunctionIds.Contains(rf.FunctionId))
            .Select(rf => rf.FunctionId)
            .ToListAsync();

        // Only create assignments that don't already exist
        var newFunctionIds = sourceFunctionIds.Except(existingTargetFunctionIds).ToList();
        if (newFunctionIds.Count == 0)
        {
            return Ok(0);
        }

        var newAssignments = newFunctionIds.Select(fid => new RoleFunction
        {
            RoleId = targetRoleId,
            FunctionId = fid,
            IsEnabled = true
        }).ToList();

        await _roleFunctionRepository.InsertManyAsync(newAssignments);
        await InvalidateRoleCacheAsync(targetRoleId);
        await PublishRoleFunctionsChangedAsync(targetRoleId, PermissionChangeType.Assigned, newFunctionIds);

        LogInformation("Cloned {Count} permissions from role {SourceRoleId} to {TargetRoleId}", newAssignments.Count, sourceRoleId, targetRoleId);
        return Ok(newAssignments.Count);
    }

    /// <summary>
    /// Export role's function assignments as portable JSON data
    /// </summary>
    public async Task<Result<RolePermissionExportDto>> ExportRolePermissionsAsync(Guid roleId)
    {
        // Get all enabled function IDs for the role
        var functionIds = await _roleFunctionRepository
            .Where(rf => rf.RoleId == roleId && rf.IsEnabled)
            .Select(rf => rf.FunctionId)
            .ToListAsync();

        // Resolve function codes (codes are portable across environments)
        var functionCodes = functionIds.Count > 0
            ? await _moduleFunctionRepository
                .Where(f => functionIds.Contains(f.Id))
                .Select(f => f.Code)
                .ToListAsync()
            : [];

        var exportData = new RolePermissionExportDto
        {
            Version = "1.0",
            ExportedAt = DateTime.UtcNow,
            SourceRoleId = roleId,
            FunctionCodes = functionCodes
        };

        return Ok(exportData);
    }

    /// <summary>
    /// Import function assignments to a role from exported data
    /// </summary>
    public IReadOnlyList<string> GetSuperAdminRoleNames()
        => _options?.Value.SuperAdminRoles ?? [];

    public async Task<Result<PermissionImportResultDto>> ImportRolePermissionsAsync(Guid roleId, RolePermissionExportDto importData)
    {
        if (importData.FunctionCodes.Count == 0)
        {
            return Ok(new PermissionImportResultDto());
        }

        // Resolve function codes to IDs
        var codeList = importData.FunctionCodes.Distinct().ToList();
        var functions = await _moduleFunctionRepository
            .Where(f => codeList.Contains(f.Code) && f.IsEnabled)
            .Select(f => new { f.Id, f.Code })
            .ToListAsync();

        var foundCodes = functions.Select(f => f.Code).ToHashSet();
        var notFoundCodes = codeList.Where(c => !foundCodes.Contains(c)).ToList();
        var resolvedFunctionIds = functions.Select(f => f.Id).ToList();

        var violation = await GetRoleGrantViolationAsync(roleId, resolvedFunctionIds);
        if (violation != null)
        {
            return Fail<PermissionImportResultDto>(violation, 403, ErrorCodes.FORBIDDEN);
        }

        // Get existing assignments for this role
        var existingFunctionIds = resolvedFunctionIds.Count > 0
            ? await _roleFunctionRepository
                .Where(rf => rf.RoleId == roleId && resolvedFunctionIds.Contains(rf.FunctionId))
                .Select(rf => rf.FunctionId)
                .ToListAsync()
            : [];

        var newFunctionIds = resolvedFunctionIds.Except(existingFunctionIds).ToList();

        if (newFunctionIds.Count > 0)
        {
            var newAssignments = newFunctionIds.Select(fid => new RoleFunction
            {
                RoleId = roleId,
                FunctionId = fid,
                IsEnabled = true
            }).ToList();

            await _roleFunctionRepository.InsertManyAsync(newAssignments);
            await InvalidateRoleCacheAsync(roleId);
            await PublishRoleFunctionsChangedAsync(roleId, PermissionChangeType.Assigned, newFunctionIds);
        }

        var result = new PermissionImportResultDto
        {
            Imported = newFunctionIds.Count,
            Skipped = existingFunctionIds.Count,
            NotFound = notFoundCodes
        };

        LogInformation("Imported permissions to role {RoleId}: {Imported} new, {Skipped} skipped, {NotFound} not found",
            roleId, result.Imported, result.Skipped, result.NotFound.Count);

        return Ok(result);
    }

    /// <summary>
    /// 级联设置模块及其所有子模块的启用状态，同时更新关联功能
    /// </summary>
    private async Task<List<Guid>> SetModuleEnabledCascadeAsync(Guid moduleId, bool enabled)
    {
        var affectedIds = new List<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(moduleId);

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            affectedIds.Add(currentId);

            // 更新当前模块
            var current = await _moduleRepository.FindAsync(currentId);
            if (current != null && current.IsEnabled != enabled)
            {
                current.IsEnabled = enabled;
                await _moduleRepository.UpdateAsync(current);
            }

            // 更新该模块下所有功能
            var functions = await _moduleFunctionRepository
                .Where(f => f.ModuleId == currentId && f.IsEnabled != enabled)
                .ToListAsync();
            foreach (var function in functions)
            {
                function.IsEnabled = enabled;
            }
            if (functions.Count > 0)
            {
                await _moduleFunctionRepository.UpdateManyAsync(functions);
            }

            // 找到子模块加入队列
            var childIds = await _moduleRepository
                .Where(m => m.ParentId == currentId)
                .Select(m => m.Id)
                .ToListAsync();
            foreach (var childId in childIds)
            {
                queue.Enqueue(childId);
            }
        }

        return affectedIds;
    }

    /// <summary>
    /// 全局清理功能权限缓存
    /// </summary>
    /// <remarks>
    /// Use this when the affected scope is broad or unknown (module cascade
    /// enable/disable, schema migrations, ops "kick the cache" actions).
    /// For narrow scopes (single-function toggle, single-role change),
    /// prefer the precise variants below to avoid invalidating every
    /// active user's cache entry.
    /// </remarks>
    private async Task InvalidateAllCacheAsync()
    {
        if (_functionAuthCache != null)
        {
            await _functionAuthCache.ClearAllAsync();
        }
    }

    /// <summary>
    /// Invalidate only the users actually reachable by a single function —
    /// users granted that function via either path
    /// (<see cref="RoleFunction"/>+UserRole, direct <see cref="UserFunction"/>),
    /// plus the super-admin catalogue
    /// (which always includes every enabled function). Cheaper than
    /// <see cref="InvalidateAllCacheAsync"/> for the common
    /// "admin toggled one function" path.
    /// </summary>
    private async Task InvalidateCacheForFunctionAsync(Guid functionId)
    {
        if (_functionAuthCache == null || _userRoleService == null)
        {
            // Without the user-role lookup we can't compute the precise
            // user set, so fall back to the safe global clear.
            await InvalidateAllCacheAsync();
            return;
        }

        try
        {
            // (a) Direct UserFunction grants on the function itself.
            var directGrantUserIds = await _userFunctionRepository
                .Where(uf => uf.FunctionId == functionId && uf.IsEnabled)
                .Select(uf => uf.UserId)
                .ToListAsync();

            // (b) Role-scoped grants — collect every role that carries this
            // function via RoleFunction and fan out to their users.
            var roleFunctionRoleIds = await _roleFunctionRepository
                .Where(rf => rf.FunctionId == functionId && rf.IsEnabled)
                .Select(rf => rf.RoleId)
                .ToListAsync();

            var affectedUserIds = new HashSet<Guid>(directGrantUserIds);
            foreach (var roleId in roleFunctionRoleIds.Distinct())
            {
                var roleUsers = await _userRoleService.GetRoleUserIdsAsync(roleId);
                if (roleUsers != null)
                {
                    foreach (var uid in roleUsers) affectedUserIds.Add(uid);
                }
            }

            if (affectedUserIds.Count > 0)
            {
                await _functionAuthCache.RemoveUserPermissionNamesAsync(affectedUserIds);
            }

            // The super-admin catalogue is derived from "all enabled
            // functions" - any function enable/disable affects it.
            await _functionAuthCache.InvalidateSuperAdminCatalogueAsync();
        }
        catch (Exception ex)
        {
            // Precise invalidation is an optimisation, not a correctness
            // guarantee — if anything goes wrong (DB transient error etc.),
            // fall back to a global clear so we never leave stale entries.
            Logger.LogWarning(ex,
                "Precise cache invalidation failed for function {FunctionId}; falling back to global clear.",
                functionId);
            await InvalidateAllCacheAsync();
        }
    }

    /// <summary>
    /// 获取用户的角色ID集合
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>角色ID集合</returns>
    private async Task<IEnumerable<Guid>> GetUserRoleIdsAsync(Guid userId)
    {
        if (_userRoleService != null)
        {
            return await _userRoleService.GetUserRoleIdsAsync(userId);
        }

        // 如果没有注入IUserRoleService，返回空集合
        return Enumerable.Empty<Guid>();
    }

    /// <summary>
    /// 清除角色下所有用户的权限缓存
    /// 当角色功能发生变更时调用
    /// </summary>
    /// <param name="roleId">角色ID</param>
    private async Task InvalidateRoleCacheAsync(Guid roleId)
    {
        if (_functionAuthCache == null || _userRoleService == null)
        {
            return;
        }

        try
        {
            // 获取该角色的所有用户
            var userIds = await _userRoleService.GetRoleUserIdsAsync(roleId);
            if (userIds != null && userIds.Any())
            {
                // 批量清除用户权限缓存
                await _functionAuthCache.RemoveUserPermissionNamesAsync(userIds);
            }
        }
        catch (Exception ex)
        {
            // 缓存失效失败不应影响主业务流程，权限缓存会在过期后自动刷新
            Logger.LogWarning(ex, "Failed to invalidate permission cache for role {RoleId}", roleId);
        }
    }

    /// <summary>
    /// 发布角色功能权限变更事件
    /// </summary>
    private async Task PublishRoleFunctionsChangedAsync(Guid roleId, PermissionChangeType changeType, List<Guid> functionIds)
    {
        if (EventBus == null) return;
        try
        {
            await EventBus.PublishAsync(new RoleFunctionsChangedEvent
            {
                RoleId = roleId,
                ChangeType = changeType,
                AffectedFunctionIds = functionIds
            });
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to publish RoleFunctionsChangedEvent for role {RoleId}", roleId);
        }
    }

    /// <summary>
    /// 发布模块启用/禁用变更事件
    /// </summary>
    private async Task PublishModuleEnabledChangedAsync(Guid moduleId, string moduleCode, bool isEnabled, List<Guid> affectedModuleIds)
    {
        if (EventBus == null) return;
        try
        {
            await EventBus.PublishAsync(new ModuleEnabledChangedEvent
            {
                ModuleId = moduleId,
                ModuleCode = moduleCode,
                IsEnabled = isEnabled,
                AffectedModuleIds = affectedModuleIds
            });
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to publish ModuleEnabledChangedEvent for module {ModuleId}", moduleId);
        }
    }

    /// <summary>
    /// 发布功能启用/禁用变更事件
    /// </summary>
    private async Task PublishFunctionEnabledChangedAsync(Guid functionId, string functionCode, Guid moduleId, bool isEnabled)
    {
        if (EventBus == null) return;
        try
        {
            await EventBus.PublishAsync(new FunctionEnabledChangedEvent
            {
                FunctionId = functionId,
                FunctionCode = functionCode,
                ModuleId = moduleId,
                IsEnabled = isEnabled
            });
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to publish FunctionEnabledChangedEvent for function {FunctionId}", functionId);
        }
    }
}

