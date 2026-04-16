
namespace Tnzi.Authorization.Services;

/// <summary>
/// 功能授权服务实现
/// </summary>
public class FunctionAuthorizationService : ApplicationService, IFunctionAuthorizationService, IModuleManagementService, IRoleFunctionService
{
    private readonly IRepository<FunctionModule, Guid> _moduleRepository;
    private readonly IRepository<ModuleFunction, Guid> _moduleFunctionRepository;
    private readonly IRepository<ModuleUser, Guid> _moduleUserRepository;
    private readonly IRepository<ModuleRole, Guid> _moduleRoleRepository;
    private readonly IRepository<RoleFunction, Guid> _roleFunctionRepository;
    private readonly IUserRoleService? _userRoleService;
    private readonly FunctionAuthCache? _functionAuthCache;

    /// <summary>
    /// 初始化一个<see cref="FunctionAuthorizationService"/>类型的新实例
    /// </summary>
    public FunctionAuthorizationService(
        IRepository<FunctionModule, Guid> moduleRepository,
        IRepository<ModuleFunction, Guid> moduleFunctionRepository,
        IRepository<ModuleUser, Guid> moduleUserRepository,
        IRepository<ModuleRole, Guid> moduleRoleRepository,
        IRepository<RoleFunction, Guid> roleFunctionRepository,
        IServiceProvider serviceProvider,
        IUserRoleService? userRoleService = null,
        FunctionAuthCache? functionAuthCache = null)
        : base(serviceProvider)
    {
        _moduleRepository = Check.NotNull(moduleRepository);
        _moduleFunctionRepository = Check.NotNull(moduleFunctionRepository);
        _moduleUserRepository = Check.NotNull(moduleUserRepository);
        _moduleRoleRepository = Check.NotNull(moduleRoleRepository);
        _roleFunctionRepository = Check.NotNull(roleFunctionRepository);
        _userRoleService = userRoleService;
        _functionAuthCache = functionAuthCache;
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

        // 获取用户的所有权限名称（内部已处理缓存和启用状态检查）
        var userPermissionNames = await GetUserPermissionNamesAsync(userId);
        return userPermissionNames.Contains(permissionName);
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

        // 一次性获取用户的所有权限名称（带缓存）
        var userPermissions = await GetUserPermissionNamesAsync(userId);
        var userPermissionSet = new HashSet<string>(userPermissions);

        return permissionNameList.ToDictionary(
            p => p,
            p => userPermissionSet.Contains(p)
        );
    }

    /// <summary>
    /// 获取用户的所有权限名称
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>权限名称集合</returns>
    public async Task<IEnumerable<string>> GetUserPermissionNamesAsync(Guid userId)
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

        // a. 用户直接绑定的模块下的所有功能
        var userModuleFunctionCodes = _moduleUserRepository
            .Where(mu => mu.UserId == userId && mu.IsEnabled)
            .Join(enabledFunctions, mu => mu.ModuleId, f => f.ModuleId, (mu, f) => f.Code);

        // b. 用户角色绑定的模块下的所有功能
        var roleModuleFunctionCodes = _moduleRoleRepository
            .Where(mr => roleIdList.Contains(mr.RoleId) && mr.IsEnabled)
            .Join(enabledFunctions, mr => mr.ModuleId, f => f.ModuleId, (mr, f) => f.Code);

        // c. 用户角色直接绑定的具体功能
        var directRoleFunctionCodes = _roleFunctionRepository
            .Where(rf => roleIdList.Contains(rf.RoleId) && rf.IsEnabled)
            .Join(enabledFunctions, rf => rf.FunctionId, f => f.Id, (rf, f) => f.Code);

        // 合并并去重，EF Core 会将其翻译为 UNION SQL
        var permissions = await userModuleFunctionCodes
            .Union(roleModuleFunctionCodes)
            .Union(directRoleFunctionCodes)
            .Distinct()
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

        request.MapTo(function);
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

        // 检查是否有角色关联
        var hasRoleFunctions = await _roleFunctionRepository
            .Where(rf => rf.FunctionId == id)
            .AnyAsync();

        if (hasRoleFunctions)
        {
            return Fail("Cannot delete function with role associations", 400, ErrorCodes.VALIDATION_ERROR);
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
        await InvalidateAllCacheAsync();
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
        await InvalidateAllCacheAsync();
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
    /// Reverse permission query: get all user IDs that have a specific permission (via roles)
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

        if (roleIds.Count == 0)
        {
            return Ok(Enumerable.Empty<PermissionUserDto>());
        }

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

        var moduleRoleCount = await _moduleRoleRepository.AsQueryable().CountAsync();
        var moduleUserCount = await _moduleUserRepository.AsQueryable().CountAsync();

        var statistics = new AuthorizationStatisticsDto
        {
            TotalModules = moduleStats?.Total ?? 0,
            EnabledModules = moduleStats?.Enabled ?? 0,
            TotalFunctions = functionStats?.Total ?? 0,
            EnabledFunctions = functionStats?.Enabled ?? 0,
            TotalRoleFunctionAssignments = rfStats?.Total ?? 0,
            EnabledRoleFunctionAssignments = rfStats?.Enabled ?? 0,
            TotalModuleRoleAssignments = moduleRoleCount,
            TotalModuleUserAssignments = moduleUserCount
        };

        return Ok(statistics);
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
    private async Task InvalidateAllCacheAsync()
    {
        if (_functionAuthCache != null)
        {
            await _functionAuthCache.ClearAllAsync();
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

