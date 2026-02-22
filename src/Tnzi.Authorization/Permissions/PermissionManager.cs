namespace Tnzi.Authorization.Permissions;

/// <summary>
/// 权限管理器实现。
/// 使用不可变快照模式保证线程安全：读操作访问当前快照（无锁），
/// 刷新时构建新快照后原子性替换引用。
/// </summary>
public class PermissionManager : IPermissionManager
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<PermissionManager>? _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>
    /// 权限数据的不可变快照，读操作直接读取此引用（无锁）
    /// </summary>
    private volatile PermissionSnapshot _snapshot = PermissionSnapshot.Empty;
    private volatile bool _initialized;

    public PermissionManager(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<PermissionManager>? logger = null)
    {
        _serviceScopeFactory = Check.NotNull(serviceScopeFactory);
        _logger = logger;
    }

    /// <summary>
    /// 异步初始化权限定义（从数据库和提供者加载）
    /// </summary>
    private async Task EnsureInitializedAsync()
    {
        if (_initialized)
            return;

        await _semaphore.WaitAsync();
        try
        {
            if (_initialized)
                return;

            _snapshot = await BuildSnapshotAsync();
            _initialized = true;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// 构建新的权限快照（包含从提供者和数据库加载的权限定义）
    /// </summary>
    private async Task<PermissionSnapshot> BuildSnapshotAsync()
    {
        var permissions = new Dictionary<string, PermissionDefinition>();
        var groups = new Dictionary<string, PermissionGroupDefinition>();

        using var scope = _serviceScopeFactory.CreateScope();

        // 从代码提供者加载权限定义
        LoadFromProviders(scope.ServiceProvider, permissions, groups);

        // 从数据库加载权限定义
        await LoadFromDatabaseAsync(scope.ServiceProvider, permissions, groups);

        return new PermissionSnapshot(permissions, groups);
    }

    /// <summary>
    /// 从权限定义提供者加载权限
    /// </summary>
    private void LoadFromProviders(IServiceProvider serviceProvider, Dictionary<string, PermissionDefinition> permissions, Dictionary<string, PermissionGroupDefinition> groups)
    {
        var providers = serviceProvider.GetServices<IPermissionDefinitionProvider>();
        foreach (var provider in providers)
        {
            var context = new PermissionDefinitionContext();
            provider.Define(context);

            // 添加权限组
            foreach (var group in context.Groups.Values)
            {
                if (!groups.ContainsKey(group.Name))
                {
                    groups[group.Name] = group;
                }
            }

            // 添加权限
            foreach (var permission in context.Permissions.Values)
            {
                if (!permissions.ContainsKey(permission.Name))
                {
                    permissions[permission.Name] = permission;
                }
            }
        }
    }

    /// <summary>
    /// 从数据库异步加载权限定义
    /// </summary>
    private async Task LoadFromDatabaseAsync(IServiceProvider serviceProvider, Dictionary<string, PermissionDefinition> permissions, Dictionary<string, PermissionGroupDefinition> groups)
    {
        try
        {
            var moduleRepository = serviceProvider.GetService<IRepository<FunctionModule, Guid>>();
            var functionRepository = serviceProvider.GetService<IRepository<ModuleFunction, Guid>>();

            if (moduleRepository == null || functionRepository == null)
            {
                _logger?.LogWarning("Module or ModuleFunction repository not found, skipping database permission loading.");
                return;
            }

            var modules = await moduleRepository.Where(m => m.IsEnabled).ToListAsync();
            var functions = await functionRepository.Where(f => f.IsEnabled).ToListAsync();

            // 加载权限组（模块）
            foreach (var module in modules)
            {
                if (!groups.ContainsKey(module.Code))
                {
                    var group = new PermissionGroupDefinition
                    {
                        Name = module.Code,
                        DisplayName = module.Name,
                        Description = module.Description,
                        ParentName = module.Parent?.Code,
                        IsEnabled = module.IsEnabled
                    };
                    groups[module.Code] = group;
                }
            }

            // 加载权限（功能）
            foreach (var function in functions)
            {
                if (!permissions.ContainsKey(function.Code))
                {
                    var permission = new PermissionDefinition
                    {
                        Name = function.Code,
                        DisplayName = function.Name,
                        Description = function.Description,
                        ParentName = function.FunctionModule?.Code,
                        IsEnabled = function.IsEnabled
                    };

                    // 关联权限组
                    if (function.FunctionModule != null && groups.TryGetValue(function.FunctionModule.Code, out var group))
                    {
                        permission.Group = group;
                        group.Permissions.Add(permission);
                    }

                    permissions[function.Code] = permission;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error loading permissions from database.");
        }
    }

    public async Task<PermissionDefinition?> GetAsync(string name)
    {
        await EnsureInitializedAsync();
        var snapshot = _snapshot;
        snapshot.Permissions.TryGetValue(name, out var permission);
        return permission;
    }

    public async Task<List<PermissionDefinition>> GetAllAsync()
    {
        await EnsureInitializedAsync();
        var snapshot = _snapshot;
        return snapshot.Permissions.Values.ToList();
    }

    public async Task<PermissionGroupDefinition?> GetGroupAsync(string name)
    {
        await EnsureInitializedAsync();
        var snapshot = _snapshot;
        snapshot.Groups.TryGetValue(name, out var group);
        return group;
    }

    public async Task<List<PermissionGroupDefinition>> GetAllGroupsAsync()
    {
        await EnsureInitializedAsync();
        var snapshot = _snapshot;
        return snapshot.Groups.Values.ToList();
    }

    public async Task<bool> IsGrantedAsync(string permissionName)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var currentUser = scope.ServiceProvider.GetService<ICurrentUser>();
        if (currentUser?.Id == null)
            return false;

        return await IsGrantedAsync(currentUser.Id.Value, permissionName);
    }

    public async Task<bool> IsGrantedAsync(Guid userId, string permissionName)
    {
        // 检查权限是否存在
        var permission = await GetAsync(permissionName);
        if (permission == null || !permission.IsEnabled)
            return false;

        using var scope = _serviceScopeFactory.CreateScope();
        var functionAuthService = scope.ServiceProvider.GetRequiredService<IFunctionAuthorizationService>();

        // 检查依赖权限（所有依赖必须满足）
        if (permission.RequiredPermissions.Count > 0)
        {
            foreach (var requiredPermission in permission.RequiredPermissions)
            {
                var hasRequired = await functionAuthService.CheckPermissionAsync(userId, requiredPermission);
                if (!hasRequired)
                {
                    _logger?.LogDebug("Permission {Permission} denied: missing required permission {Required}",
                        permissionName, requiredPermission);
                    return false;
                }
            }
        }

        // 先检查用户是否直接拥有该权限
        var hasDirectPermission = await functionAuthService.CheckPermissionAsync(userId, permissionName);
        if (hasDirectPermission)
            return true;

        // 如果支持从父权限继承，检查父权限
        if (permission.IsInheritedFromParent && !string.IsNullOrEmpty(permission.ParentName))
        {
            var hasParentPermission = await IsGrantedAsync(userId, permission.ParentName);
            if (hasParentPermission)
            {
                _logger?.LogDebug("Permission {Permission} granted via parent {Parent}",
                    permissionName, permission.ParentName);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 刷新权限定义。
    /// 构建新快照后原子性替换引用，读操作不会看到不一致的中间状态。
    /// </summary>
    public async Task RefreshAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            _snapshot = await BuildSnapshotAsync();
            _initialized = true;
        }
        finally
        {
            _semaphore.Release();
        }

        _logger?.LogInformation("Permission definitions refreshed successfully.");
    }

    /// <summary>
    /// 不可变的权限数据快照
    /// </summary>
    private sealed class PermissionSnapshot
    {
        public static readonly PermissionSnapshot Empty = new(
            new Dictionary<string, PermissionDefinition>(),
            new Dictionary<string, PermissionGroupDefinition>());

        public Dictionary<string, PermissionDefinition> Permissions { get; }
        public Dictionary<string, PermissionGroupDefinition> Groups { get; }

        public PermissionSnapshot(
            Dictionary<string, PermissionDefinition> permissions,
            Dictionary<string, PermissionGroupDefinition> groups)
        {
            Permissions = permissions;
            Groups = groups;
        }
    }
}
