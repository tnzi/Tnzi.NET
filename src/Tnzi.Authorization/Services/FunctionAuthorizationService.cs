
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
    private readonly IRepository<Tnzi.Identity.Entities.User, Guid>? _userRepository;
    private readonly IUserTenantScopeProvider? _userScope;
    private readonly bool _multiTenancyEnabled;
    private readonly ICurrentTenant? _currentTenant;
    private readonly IDataFilterManager? _dataFilterManager;
    private readonly ITnziApplication? _application;
    private readonly SuperAdminBypassPolicy? _bypassPolicy;

    /// <summary>Identity 用户管理面的读门；持有者反查只把用户名给持有它的调用者。</summary>
    private const string UserViewPermission = "user.view";

    /// <summary>
    /// 权限目录（模块 / 功能）是全部租户共用的一张表，租户上下文里的写入一律拒绝。
    /// </summary>
    private const string CatalogueIsHostManaged =
        "The permission catalogue is shared by every tenant and can only be changed outside a tenant context.";

    /// <summary>
    /// 初始化一个<see cref="FunctionAuthorizationService"/>类型的新实例
    /// </summary>
    /// <remarks>
    /// 最后两个可选依赖只被 <see cref="GetPermissionHoldersAsync"/> 读：用户仓储把持有者的 id 换成用户名
    /// 并剔除已不存在的账号，范围提供者把名单收窄到调用者的租户。用户仓储缺席时不判存在、用户名留空；
    /// 范围提供者是安全边界，缺席时持有者反查答 501 而不是把名单放宽到所有租户。
    /// 范围提供者在场时，按用户 id 读权限的两个管理端入口也先问它（越界与不存在同答 404）。
    /// <para>
    /// 最后三个描述「这次计算落在哪个租户过滤器之下」，与 <c>TnziDbContext</c> 同一组判据：
    /// 每用户权限缓存按它分区（见 <see cref="PermissionCachePartition"/>），权限目录的写入按它判定是否在宿主上下文。
    /// 多租户开关缺席按未开启处理，与数据库侧一致。
    /// </para>
    /// </remarks>
    public FunctionAuthorizationService(
        IRepository<FunctionModule, Guid> moduleRepository,
        IRepository<ModuleFunction, Guid> moduleFunctionRepository,
        IRepository<RoleFunction, Guid> roleFunctionRepository,
        IRepository<UserFunction, Guid> userFunctionRepository,
        IServiceProvider serviceProvider,
        IUserRoleService? userRoleService = null,
        FunctionAuthCache? functionAuthCache = null,
        IOptions<Tnzi.Authorization.Options.AuthorizationOptions>? options = null,
        IRepository<Tnzi.Identity.Entities.Role, Guid>? roleRepository = null,
        IRepository<Tnzi.Identity.Entities.User, Guid>? userRepository = null,
        IUserTenantScopeProvider? userScope = null,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null,
        ICurrentTenant? currentTenant = null,
        IDataFilterManager? dataFilterManager = null,
        ITnziApplication? application = null,
        SuperAdminBypassPolicy? bypassPolicy = null)
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
        _userRepository = userRepository;
        _userScope = userScope;
        _multiTenancyEnabled = multiTenancyOptions?.Value.Enabled ?? false;
        _currentTenant = currentTenant;
        _dataFilterManager = dataFilterManager;
        _application = application;
        _bypassPolicy = bypassPolicy;
    }

    /// <summary>
    /// 超管旁路是否覆盖全部码（<c>Authorization:SuperAdminBypassScope = All</c>，默认）。
    /// 策略缺席（手工组装服务的宿主与单元测试）按全部覆盖处理，与引入范围之前逐字相同。
    /// </summary>
    private bool BypassCoversEveryCode => _bypassPolicy?.CoversEveryCode ?? true;

    /// <summary>超管旁路是否覆盖这个码（见 <see cref="SuperAdminBypassPolicy.Covers"/>）。</summary>
    private bool BypassCovers(string permissionName) => _bypassPolicy?.Covers(permissionName) ?? true;

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
    /// Forward lookup of every super-admin user id - the union of members across
    /// all roles named in <c>Authorization:SuperAdminRoles</c>. Symmetric with
    /// <see cref="IsSuperAdminAsync"/> (the reverse "is this one user a super admin").
    /// Callers use it to strip super admins out of business-facing user listings
    /// (chat directory, group-member candidates, broadcast audience). Returns an
    /// empty set when super admins are not configured or the role / user-role
    /// services are unavailable (no super-admin concept → hide no one).
    /// </summary>
    public async Task<IReadOnlySet<Guid>> GetSuperAdminUserIdsAsync()
    {
        var superRoles = _options?.Value.SuperAdminRoles;
        if (superRoles is not { Count: > 0 } || _userRoleService == null || _roleRepository == null)
            return new HashSet<Guid>();

        // Match on NormalizedName (upper-cased) - Identity keeps it in sync and the
        // seeder writes it upper-cased, so this is the case-insensitive equivalent
        // of the OrdinalIgnoreCase name match in IsSuperAdminAsync, translated to a
        // single SQL IN. Use the repository ToListAsync(predicate) overload (not
        // IQueryable+EF ToListAsync) so the set is testable without a live provider.
        var normalized = superRoles
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.ToUpperInvariant())
            .ToHashSet();
        if (normalized.Count == 0) return new HashSet<Guid>();

        var superRoleRows = await _roleRepository.ToListAsync(
            r => r.NormalizedName != null && normalized.Contains(r.NormalizedName));

        var result = new HashSet<Guid>();
        foreach (var roleId in superRoleRows.Select(r => r.Id))
        {
            var userIds = await _userRoleService.GetRoleUserIdsAsync(roleId);
            foreach (var uid in userIds) result.Add(uid);
        }
        return result;
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

        // 已确认不是超管：有效集就是显式集，不必经 GetUserPermissionNamesAsync 再做一次超管判定。
        var grantorCodes = ToCaseInsensitiveSet(await GetExplicitUserPermissionNamesAsync(grantorUserId));
        var roleCodes = await GetRoleGrantedCodesAsync(roleId);
        return roleCodes.All(grantorCodes.Contains);
    }

    /// <summary>
    /// 成员变更（以及角色本身的改动与删除）的支配判定：在 <see cref="CanManageRoleAsync"/> 之上，
    /// 角色的休眠授权（启用的授权行指向已停用或已退役的功能）按码一并计入。
    /// </summary>
    /// <remarks>
    /// 休眠码不在任何人的有效集里（超管的目录也只含生效功能），所以结果就是「带休眠授权的角色只有超管能改成员」。
    /// 这正是要的：此前弱管理员可以先停用一个码（或等它因模块未加载而退役）、把自己挂进带着它的角色
    /// （只看生效码，支配平凡成立）、再启用它，全程无人授出就持有了那个码。
    /// 授权矩阵的写入与克隆仍只算生效码（<see cref="GetRoleGrantViolationAsync"/>），那边的放宽是为了不被休眠授权锁死。
    /// </remarks>
    public async Task<bool> CanManageRoleMembershipAsync(Guid grantorUserId, Guid roleId)
    {
        if (await IsSuperAdminAsync(grantorUserId)) return true;
        if (await IsSuperAdminRoleAsync(roleId)) return false;

        var grantorCodes = ToCaseInsensitiveSet(await GetExplicitUserPermissionNamesAsync(grantorUserId));
        var roleCodes = await GetRoleGrantedCodesAsync(roleId, includeDormant: true);
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

    /// <summary>
    /// 角色显式授予的功能码（RoleFunction 直连，授权行启用）。默认只取生效功能；
    /// <paramref name="includeDormant"/> 为 true 时连指向已停用 / 已退役功能的休眠授权一起取（已删除的功能不算，它们不会回来）。
    /// </summary>
    private async Task<List<string>> GetRoleGrantedCodesAsync(Guid roleId, bool includeDormant = false)
    {
        var functions = includeDormant
            ? _moduleFunctionRepository.AsQueryable()
            : _moduleFunctionRepository.Where(f => f.IsEnabled && !f.IsRetired);

        return await _roleFunctionRepository
            .Where(rf => rf.RoleId == roleId && rf.IsEnabled)
            .Join(functions, rf => rf.FunctionId, f => f.Id, (rf, f) => f.Code)
            .Distinct()
            .ToListAsync();
    }

    /// <summary>
    /// 角色授权写路径的委托护栏。非超管授权者仅能操作自己支配的角色
    /// （见 <see cref="CanManageRoleAsync"/>），且仅能授出自己持有的权限码。
    /// 允许时返回 null，越界时返回英文错误消息（调用方包装为 403）。
    /// 无用户上下文（系统/播种/内部路径与单元测试）时整体跳过。
    /// </summary>
    /// <remarks>
    /// 支配判定与 <see cref="CanManageRoleAsync"/> 逐字相同，只是授权者已确认不是超管、他的有效集也只读一次：
    /// 经公开方法走一遍会把同一个超管判定（一次不经缓存的角色查询）在一次写入里做上四次。
    /// </remarks>
    private async Task<string?> GetRoleGrantViolationAsync(Guid roleId, IReadOnlyCollection<Guid>? functionIdsToGrant = null)
    {
        var grantorId = CurrentUser?.Id;
        if (grantorId == null || grantorId == Guid.Empty) return null;
        if (await IsSuperAdminAsync(grantorId.Value)) return null;

        const string notDominated = "You cannot manage this role: its permission set is not contained in yours, or it is a super-admin role.";
        if (await IsSuperAdminRoleAsync(roleId)) return notDominated;

        // 非超管的有效集就是显式集（超管才取整个目录）。
        var grantorCodes = ToCaseInsensitiveSet(await GetExplicitUserPermissionNamesAsync(grantorId.Value));
        if (!(await GetRoleGrantedCodesAsync(roleId)).All(grantorCodes.Contains)) return notDominated;

        if (functionIdsToGrant is { Count: > 0 })
        {
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

        // SuperAdmin bypass - short-circuit before touching the function
        // tables. Everyone else resolves through explicit grants only
        // (deny-by-default): no role is implicitly granted anything.
        // With SuperAdminBypassScope = Platform the bypass only reaches platform
        // codes; an application code falls through to the explicit grants below,
        // for super admins as for everyone else (deny rows included).
        if (BypassCovers(permissionName) && await IsSuperAdminAsync(userId)) return true;

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
    /// 一次解析 N 个用户对同一个码的授予（常数次查询）。与逐个
    /// <see cref="CheckPermissionAsync"/> 逐用户一致：超管旁路 → 用户级 deny 优先 →
    /// 用户直授 / 角色授权（仅启用且未退役的功能）；码大小写不敏感。
    /// </summary>
    /// <remarks>
    /// 调用方是「按用户名单过滤」的场景（IM 通讯录 / 群成员候选 / 广播受众）。
    /// 逐个判定每人至少一次无缓存的角色查询，名单一长就是 O(N) 次往返；这里改成
    /// 一次角色名查询（超管）+ 一次功能定位 + 一次角色授权 + 一次用户授权 + 一次角色 Id 查询。
    /// 刻意不经每用户权限缓存：缓存按用户分片，批量场景下命中率与代价都不划算。
    /// </remarks>
    public async Task<IReadOnlySet<Guid>> FilterGrantedAsync(IReadOnlyCollection<Guid> userIds, string permissionName)
    {
        var granted = new HashSet<Guid>();
        if (userIds == null || userIds.Count == 0 || string.IsNullOrEmpty(permissionName))
            return granted;

        var ids = userIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0) return granted;

        // 1. 超管旁路：一次角色名查询覆盖整份名单。旁路收窄到平台码时，应用码不走这一步。
        var superRoles = _options?.Value.SuperAdminRoles;
        if (BypassCovers(permissionName) && superRoles is { Count: > 0 } && _userRoleService != null)
        {
            var superSet = new HashSet<string>(superRoles, StringComparer.OrdinalIgnoreCase);
            var roleNamesByUser = await _userRoleService.GetUserRolesAsync(ids)
                ?? new Dictionary<Guid, IEnumerable<string>>();
            foreach (var (userId, roleNames) in roleNamesByUser)
            {
                if (roleNames.Any(superSet.Contains)) granted.Add(userId);
            }
        }

        var remaining = ids.Where(id => !granted.Contains(id)).ToList();
        if (remaining.Count == 0) return granted;

        // 2. 码 → 启用且未退役的功能（大小写不敏感，与单查一致）。
        var code = permissionName.ToLower();
        var functionIds = await _moduleFunctionRepository
            .Where(f => f.IsEnabled && !f.IsRetired && f.Code.ToLower() == code)
            .Select(f => f.Id)
            .ToListAsync();
        if (functionIds.Count == 0) return granted;

        // 3. 用户直授 / 用户级 deny（deny 优先，与 GetExplicitUserPermissionNamesAsync 的 EXCEPT 一致）。
        var userRows = await _userFunctionRepository
            .Where(uf => remaining.Contains(uf.UserId) && functionIds.Contains(uf.FunctionId) && uf.IsEnabled)
            .Select(uf => new { uf.UserId, uf.IsGranted })
            .ToListAsync();
        var denied = userRows.Where(r => !r.IsGranted).Select(r => r.UserId).ToHashSet();
        var allowedDirectly = userRows.Where(r => r.IsGranted).Select(r => r.UserId).ToHashSet();

        // 4. 授予该功能的角色 → 名单里谁持有这些角色。
        var grantingRoleIds = (await _roleFunctionRepository
            .Where(rf => functionIds.Contains(rf.FunctionId) && rf.IsEnabled)
            .Select(rf => rf.RoleId)
            .ToListAsync()).ToHashSet();
        var roleIdsByUser = grantingRoleIds.Count > 0 && _userRoleService != null
            ? await _userRoleService.GetUserRoleIdsAsync(remaining) ?? new Dictionary<Guid, IEnumerable<Guid>>()
            : new Dictionary<Guid, IEnumerable<Guid>>();

        foreach (var userId in remaining)
        {
            if (denied.Contains(userId)) continue;
            if (allowedDirectly.Contains(userId)
                || (roleIdsByUser.TryGetValue(userId, out var roleIds) && roleIds.Any(grantingRoleIds.Contains)))
            {
                granted.Add(userId);
            }
        }

        return granted;
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
        if (BypassCoversEveryCode)
        {
            if (await IsSuperAdminAsync(userId))
            {
                return permissionNameList.ToDictionary(p => p, _ => true);
            }
        }
        else if (permissionNameList.Any(BypassCovers) && await IsSuperAdminAsync(userId))
        {
            // 收窄的旁路：平台码直接通过，其余码与单查一样走显式集。
            var explicitSet = ToCaseInsensitiveSet(await GetExplicitUserPermissionNamesAsync(userId));
            return permissionNameList.ToDictionary(p => p, p => BypassCovers(p) || explicitSet.Contains(p));
        }

        // 一次性获取用户的所有权限名称（带缓存）。上面已判定不是超管，直接取显式集：
        // 经 GetUserPermissionNamesAsync 会把同一个超管判定（一次不经缓存的角色查询）再做一遍。
        var userPermissions = await GetExplicitUserPermissionNamesAsync(userId);
        // Case-insensitive match - see comment in CheckPermissionAsync for rationale.
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
    /// 系统托管行上由声明决定的文字（名称、说明）是否与请求里的相同：去首尾空白后逐字比较，
    /// null、空串与纯空白视为同一个「没写」（表单把 null 渲染成空输入框，回传的可能是任何一种）。
    /// </summary>
    private static bool SameDeclaredText(string? stored, string? requested)
    {
        var a = string.IsNullOrWhiteSpace(stored) ? null : stored.Trim();
        var b = string.IsNullOrWhiteSpace(requested) ? null : requested.Trim();
        return string.Equals(a, b, StringComparison.Ordinal);
    }

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
            .Where(f => f.IsEnabled && !f.IsRetired)
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
        // SuperAdmin bypass - return the full catalogue of enabled functions so
        // both the legacy "has X" check and any caller that enumerates the user's
        // permissions (menu builders, audit, etc.) sees a complete grant set.
        // The catalogue is identical for every super-admin so it lives in a
        // shared cache key (1h TTL), invalidated by Function/Module enable
        // changes. This avoids a full ModuleFunction scan per super-admin
        // request when a UI repeatedly enumerates permissions.
        if (await IsSuperAdminAsync(userId))
        {
            return BypassCoversEveryCode
                ? await GetSuperAdminCatalogueAsync()
                : await GetNarrowedSuperAdminPermissionNamesAsync(userId);
        }

        return await GetExplicitUserPermissionNamesAsync(userId);
    }

    /// <summary>
    /// 旁路收窄到平台码时超管的有效集：目录里的平台码 ∪ 他的显式集。
    /// 与 <see cref="CheckPermissionAsync"/> 逐码一致：平台码由旁路放行（否定行不起作用），
    /// 应用码只认显式授权（否定行照常扣除）。
    /// </summary>
    private async Task<IEnumerable<string>> GetNarrowedSuperAdminPermissionNamesAsync(Guid userId)
    {
        var platformCodes = (await GetSuperAdminCatalogueAsync()).Where(BypassCovers);
        var explicitCodes = await GetExplicitUserPermissionNamesAsync(userId);
        return platformCodes.Union(explicitCodes, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Explicit effective permission names, tier-independent, with per-user
    /// cache. Resolution: <c>(RoleFunction ∪ user-allow) − user-deny</c> -
    /// a user-level deny row removes a code no matter which role granted it
    /// (user-level wins; super admins short-circuit upstream and are never
    /// affected by deny rows).
    /// </summary>
    private async Task<IEnumerable<string>> GetExplicitUserPermissionNamesAsync(Guid userId)
    {
        // 1. 尝试从缓存获取。缓存按「这份集合在哪个租户过滤器下算出来」分区：
        //    别的租户视角下算出的集合不能读给这个请求（见 PermissionCachePartition）。
        var partition = _functionAuthCache != null ? CurrentCachePartition() : null;
        if (partition != null)
        {
            var cached = await _functionAuthCache!.GetUserPermissionNamesAsync(userId, partition);
            if (cached != null)
            {
                return cached;
            }
        }

        // 2. 缓存未命中，从数据库查询
        var userRoles = await GetUserRoleIdsAsync(userId);
        var roleIdList = userRoles.ToList();

        // 基础功能查询器（仅获取启用的功能）
        var enabledFunctions = _moduleFunctionRepository.Where(f => f.IsEnabled && !f.IsRetired);

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

        // 合并去重在 SQL 里做，扣除在内存里按大小写不敏感做：码的匹配处处大小写不敏感，
        // 而 SQL 的 EXCEPT 按数据库排序规则比较（PostgreSQL / SQLite 区分大小写）；
        // 库里若留着只差大小写的两行，对其中一行的否定在 SQL 里扣不掉另一行。
        var granted = await directRoleFunctionCodes
            .Union(directUserFunctionCodes)
            .ToListAsync();
        var denied = ToCaseInsensitiveSet(await deniedCodes.ToListAsync());
        var permissions = denied.Count == 0
            ? granted
            : granted.Where(code => !denied.Contains(code)).ToList();

        // 3. 结果存入缓存 (30分钟)，连同算出它的分区
        if (partition != null)
        {
            await _functionAuthCache!.SetUserPermissionNamesAsync(userId, partition, permissions);
        }

        return permissions;
    }

    /// <summary>当前请求的权限缓存分区；<c>null</c> = 这次计算不经过缓存。</summary>
    private string? CurrentCachePartition()
        => PermissionCachePartition.Resolve(_multiTenancyEnabled, _dataFilterManager, _currentTenant, CurrentUser);

    /// <summary>
    /// 权限目录（模块 / 功能，均为全局表）的写入是否被拒绝：多租户开启且请求带着租户上下文时拒绝。
    /// </summary>
    /// <remarks>
    /// <see cref="FunctionModule"/> / <see cref="ModuleFunction"/> 没有 <c>TenantId</c>，一次停用、改码、删除影响所有租户；
    /// 而写它们的权限码是按租户授予的（角色与角色授权都按租户隔离），租户管理员持有它们是常态。
    /// 「当前租户为空 = 全局管理员」与用户管理端的 <see cref="UserTenantScope"/> 同一口径；
    /// 没有已认证主体的调用（播种、后台任务）不在此列。多租户未开启时恒为 false，与此前逐字相同。
    /// </remarks>
    private bool IsCatalogueWriteForbidden()
        => !UserTenantScope.Resolve(_multiTenancyEnabled, _currentTenant, CurrentUser).IsUnrestricted;

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

        if (!await IsUserInCallerScopeAsync(userId))
        {
            return Fail<bool>("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
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
        if (!await IsUserInCallerScopeAsync(userId))
        {
            return Fail<IEnumerable<string>>("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

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
        // 不跟踪：树由下面手动构建，不能让变更跟踪器的关系修复替我们把 Children 填上：
        // 库里若留着父链环（写入口加守卫之前的数据），修复会把环原样填进 Children，序列化溢出深度。
        var allModules = await _moduleRepository
            .Where(m => m.IsEnabled && !m.IsRetired)
            .Include(m => m.Functions.Where(f => !f.IsRetired))
            .OrderBy(m => m.Order)
            .AsNoTracking()
            .ToListAsync();

        // 2. 内存构建树
        var moduleDict = allModules.ToDictionary(m => m.Id);
        var rootModules = new List<FunctionModule>();

        foreach (var module in allModules)
        {
            // 有 ParentId、父节点在已加载列表中、且不在父链环上，就作为子节点处理。
            // 环上的模块作为根输出：否则环上谁都不是根，整组连同功能从树里消失。
            if (module.ParentId.HasValue
                && moduleDict.TryGetValue(module.ParentId.Value, out var parent)
                && !IsOnParentCycle(module, moduleDict))
            {
                parent.Children.Add(module);
                module.Parent = parent;
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
    /// 获取模块的功能列表（管理面）。
    /// </summary>
    /// <remarks>
    /// 这里<b>刻意不</b>过滤 <c>IsRetired</c> 与 <c>IsEnabled</c>：休眠行（退役或停用）不参与权限解析、
    /// 也不出现在分配矩阵里，但管理页必须看得见它 —— 退役行不可见则「授权明明还在、却不生效」没有任何可查之处；
    /// 停用行不可见则「启用」按钮没有宿主，停用在 UI 上变成单向操作，恢复只能拿着 guid 直调端点或改库。
    /// DTO 带 <c>IsRetired</c> / <c>IsEnabled</c> 供前端渲染置灰的退役标与停用徽标。
    /// 生效判据只在权限解析、超管/业务管理员目录与矩阵写路径上。
    /// </remarks>
    /// <param name="moduleId">模块ID</param>
    /// <returns>功能列表</returns>
    public async Task<Result<IEnumerable<ModuleFunction>>> GetModuleFunctionsAsync(Guid moduleId)
    {
        var functions = await _moduleFunctionRepository
            .Where(f => f.ModuleId == moduleId)
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
    /// <remarks>
    /// 平铺列表，层级只由 <see cref="FunctionModule.ParentId"/> 表达（前端按它在客户端建树）。
    /// 不跟踪：跟踪查询会让关系修复把每个模块的子树填进 <see cref="FunctionModule.Children"/>，
    /// 平铺列表因此按层级重复输出，库里有父链环时序列化溢出深度、整页 500，而这一页正是修复数据的入口。
    /// </remarks>
    public async Task<Result<IEnumerable<FunctionModule>>> GetModulesAsync()
    {
        var modules = await _moduleRepository
            .OrderBy(m => m.Order)
            .AsNoTracking()
            .ToListAsync();
        return Ok((IEnumerable<FunctionModule>)modules);
    }

    /// <summary>
    /// 权限目录：全部模块连同各自的全部功能，两条查询读出。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="GetModulesAsync"/> 加逐模块 <see cref="GetModuleFunctionsAsync"/> 同一口径（含停用与退役行），
    /// 只是把前端原先的 1 + N 次请求并成一次。模块按运行中的模块图标出 <c>IsBuiltIn</c>，与 <c>GET admin/modules</c> 相同。
    /// </remarks>
    public async Task<Result<IReadOnlyList<PermissionCatalogueModuleDto>>> GetPermissionCatalogueAsync()
    {
        var modules = await _moduleRepository
            .OrderBy(m => m.Order)
            .AsNoTracking()
            .ToListAsync();
        var functions = await _moduleFunctionRepository
            .AsNoTracking()
            .ToListAsync();

        var functionsByModule = functions.ToLookup(f => f.ModuleId);
        var builtInCodes = _application is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : FrameworkModuleResolver.GetLoadedFrameworkModuleCodes(_application);

        // 归属只在旁路收窄时才有意义，也只在那时下发（默认模式的响应不变）。没有 provider 声明的模块（后台手建）是应用。
        var registry = BypassCoversEveryCode ? null : _bypassPolicy!.Registry;
        IReadOnlyList<PermissionCatalogueModuleDto> catalogue = modules
            .Select(m =>
            {
                var dto = PermissionCatalogueMapping.ToCatalogueModule(m, functionsByModule[m.Id], builtInCodes.Contains(m.Code));
                if (registry != null)
                {
                    dto.Scope = registry.GetGroupScope(m.Code) ?? PermissionScope.Application;
                }
                return dto;
            })
            .ToList();
        return Ok(catalogue);
    }

    /// <summary>
    /// 创建模块
    /// </summary>
    /// <param name="request">模块信息</param>
    /// <returns>创建的模块</returns>
    public async Task<Result<FunctionModule>> CreateModuleAsync(CreateFunctionModuleRequest request)
    {
        if (IsCatalogueWriteForbidden())
        {
            return Fail<FunctionModule>(CatalogueIsHostManaged, 403, ErrorCodes.FORBIDDEN);
        }

        // 检查代码是否已存在（大小写不敏感，见 IsModuleCodeTakenAsync）
        var exists = await IsModuleCodeTakenAsync(request.Code);

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
        if (IsCatalogueWriteForbidden())
        {
            return Fail<FunctionModule>(CatalogueIsHostManaged, 403, ErrorCodes.FORBIDDEN);
        }

        var module = await _moduleRepository.FindAsync(id);
        if (module == null)
        {
            return Fail<FunctionModule>("Module not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        // System-managed rows: only Order is editable here (enable / disable go
        // through their own endpoints). Code / Name / Description / ParentId
        // mirror the IPermissionDefinitionProvider declaration and the seeder
        // restores them on the next startup, so an edit would take effect now and
        // silently disappear later. Refuse with 403 so the admin sees the
        // constraint. Compared by value: the admin form posts every field.
        if (module.IsSystemManaged)
        {
            if (!string.Equals(module.Code, request.Code, StringComparison.Ordinal))
            {
                return Fail<FunctionModule>(
                    "System-managed module code cannot be changed. Update the IPermissionDefinitionProvider declaration in code instead.",
                    403, ErrorCodes.IDENTITY_ROLE_SYSTEM_PROTECTED);
            }

            if (!SameDeclaredText(module.Name, request.Name)
                || !SameDeclaredText(module.Description, request.Description)
                || module.ParentId != request.ParentId)
            {
                return Fail<FunctionModule>(
                    "The name, description and parent of a system-managed module come from its IPermissionDefinitionProvider declaration and cannot be changed here; only the order can.",
                    403, ErrorCodes.IDENTITY_ROLE_SYSTEM_PROTECTED);
            }
        }

        // 检查代码是否被其他模块使用（大小写不敏感）
        if (module.Code != request.Code)
        {
            var codeExists = await IsModuleCodeTakenAsync(request.Code, exceptId: id);

            if (codeExists)
            {
                return Fail<FunctionModule>($"Module with code '{request.Code}' already exists", 409, ErrorCodes.VALIDATION_ERROR);
            }
        }

        // 如果指定了父模块，验证父模块存在、且挂上去不会成环
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

            var parentViolation = await DescribeParentCycleAsync(id, request.ParentId.Value);
            if (parentViolation != null)
            {
                return Fail<FunctionModule>(parentViolation, 400, ErrorCodes.VALIDATION_ERROR);
            }
        }

        // 系统托管行只落排序：其余字段上面已判定与声明相同，不让请求里的空白差异写进库。
        // 请求里没有启用状态（启停走专门的级联端点），所以这里的改动不触及权限解析，不必失效缓存。
        if (module.IsSystemManaged)
        {
            module.Order = request.Order;
        }
        else
        {
            request.MapTo(module);
        }
        await _moduleRepository.UpdateAsync(module);

        LogInformation("Module updated: {Code}, Name: {Name}", request.Code, request.Name);
        return Ok(module, "Module updated successfully");
    }

    /// <summary>
    /// 删除模块
    /// </summary>
    /// <param name="id">模块ID</param>
    public async Task<Result> DeleteModuleAsync(Guid id)
    {
        if (IsCatalogueWriteForbidden())
        {
            return Fail(CatalogueIsHostManaged, 403, ErrorCodes.FORBIDDEN);
        }

        var module = await _moduleRepository.FindAsync(id);
        if (module == null)
        {
            return Fail("Module not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        // System-managed rows are owned by code - delete attempts via admin
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
            .Where(f => functionIds.Contains(f.Id) && f.IsEnabled && !f.IsRetired)
            .OrderBy(f => f.Order)
            .ToListAsync();
        return Ok((IEnumerable<ModuleFunction>)functions);
    }

    /// <summary>
    /// 获取角色的功能ID列表（仅当前生效的功能）
    /// </summary>
    /// <param name="roleId">角色ID</param>
    /// <returns>功能ID列表</returns>
    /// <remarks>
    /// 指向退役 / 已禁用功能的休眠授权刻意不返回：这个列表喂的是分配矩阵，而矩阵只渲染生效项。
    /// 若把休眠 id 一并返回，矩阵会原样提交回来，<see cref="SetRoleFunctionsAsync"/> 按同一判据
    /// 拒绝 —— 该角色的矩阵就再也保存不了。GET → PUT 的往返必须是一次空操作。
    /// </remarks>
    public async Task<Result<IEnumerable<Guid>>> GetRoleFunctionIdsAsync(Guid roleId)
    {
        var liveFunctions = _moduleFunctionRepository.Where(f => f.IsEnabled && !f.IsRetired);
        var functionIds = await _roleFunctionRepository
            .Where(rf => rf.RoleId == roleId && rf.IsEnabled)
            .Join(liveFunctions, rf => rf.FunctionId, f => f.Id, (rf, _) => rf.FunctionId)
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

        // 验证功能存在且当前生效 —— 与权限解析同一判据，否则授出去的是一条没人读的行
        var notGrantable = await FunctionLiveness.DescribeNotGrantableAsync(_moduleFunctionRepository, functionIdList);
        if (notGrantable != null)
        {
            return Fail(notGrantable, 404, ErrorCodes.RESOURCE_NOT_FOUND);
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
    /// 设置角色的功能（覆盖原有的<b>生效</b>功能集）
    /// </summary>
    /// <param name="roleId">角色ID</param>
    /// <param name="functionIds">功能ID列表，须全部为当前生效的功能</param>
    /// <remarks>
    /// 覆盖的范围是矩阵够得到的范围：指向退役 / 已禁用功能的休眠授权<b>原样保留</b>。
    /// 退役是「宿主没加载那个模块」的正常产物，禁用是管理员的临时开关，两者结束时授权都要回来 ——
    /// 顺手删掉等于把 <c>PermissionRetirementMode.Disable</c> 承诺的「保留」偷换成「清空」。
    /// 明确要全部删除的走 <see cref="ClearRoleFunctionsAsync"/>。
    /// </remarks>
    public async Task<Result> SetRoleFunctionsAsync(Guid roleId, IEnumerable<Guid> functionIds)
    {
        var functionIdList = functionIds.Distinct().ToList();

        var violation = await GetRoleGrantViolationAsync(roleId, functionIdList);
        if (violation != null)
        {
            return Fail(violation, 403, ErrorCodes.FORBIDDEN);
        }

        // 验证新功能存在且当前生效
        var notGrantable = await FunctionLiveness.DescribeNotGrantableAsync(_moduleFunctionRepository, functionIdList);
        if (notGrantable != null)
        {
            return Fail(notGrantable, 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        // 休眠授权（指向退役 / 已禁用功能）在覆盖范围之外
        var dormantIds = await FunctionLiveness.GetDormantFunctionIdsAsync(
            _moduleFunctionRepository, _roleFunctionRepository.Where(rf => rf.RoleId == roleId), rf => rf.FunctionId);

        // 原子操作：在同一个 UnitOfWork 中删除旧关联并创建新关联，避免权限窗口期
        var result = await ExecuteInUnitOfWorkAsync(async _ =>
        {
            // 删除旧关联（休眠行除外）
            if (dormantIds.Count == 0)
            {
                await _roleFunctionRepository.DeleteAsync(rf => rf.RoleId == roleId);
            }
            else
            {
                await _roleFunctionRepository.DeleteAsync(rf => rf.RoleId == roleId && !dormantIds.Contains(rf.FunctionId));
            }

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
    /// <returns>角色功能列表，与 <see cref="GetRoleFunctionsPagedAsync"/> 同一个形状（带码、名称与所属模块）</returns>
    /// <remarks>
    /// 此前直接返回授权行实体：<c>Function</c> 导航不加载，线上没有码、名称与模块，
    /// 而前端按 <c>RoleFunctionDto</c> 读它，得到的是 undefined 且编译期看不出来。
    /// </remarks>
    public async Task<Result<IEnumerable<RoleFunctionDto>>> GetFunctionRolesAsync(Guid functionId)
    {
        var function = await _moduleFunctionRepository
            .Where(f => f.Id == functionId)
            .Select(f => new { f.Code, f.Name, f.ModuleId })
            .FirstOrDefaultAsync();

        var roleFunctions = await _roleFunctionRepository
            .Where(rf => rf.FunctionId == functionId && rf.IsEnabled)
            .ToListAsync();

        var dtos = roleFunctions.Select(rf => new RoleFunctionDto
        {
            Id = rf.Id,
            RoleId = rf.RoleId,
            FunctionId = rf.FunctionId,
            FunctionCode = function?.Code ?? string.Empty,
            FunctionName = function?.Name ?? string.Empty,
            ModuleId = function?.ModuleId ?? Guid.Empty,
            IsEnabled = rf.IsEnabled,
            CreationTime = rf.CreationTime
        }).ToList();
        return Ok((IEnumerable<RoleFunctionDto>)dtos);
    }

    /// <summary>
    /// 批量分配功能到多个角色
    /// </summary>
    /// <param name="roleIds">角色ID列表</param>
    /// <param name="functionIds">功能ID列表</param>
    public async Task<Result> BatchAssignFunctionsAsync(IEnumerable<Guid> roleIds, IEnumerable<Guid> functionIds)
    {
        // 集合语义：重复的角色不该被判两次、写两次。
        var roleIdList = roleIds.Distinct().ToList();
        var functionIdList = functionIds.Distinct().ToList();

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
        if (IsCatalogueWriteForbidden())
        {
            return Fail<ModuleFunction>(CatalogueIsHostManaged, 403, ErrorCodes.FORBIDDEN);
        }

        // 检查代码是否已存在（大小写不敏感，见 IsFunctionCodeTakenAsync）
        var codeExists = await IsFunctionCodeTakenAsync(request.Code);

        if (codeExists)
        {
            return Fail<ModuleFunction>($"Function with code '{request.Code}' already exists", 409, ErrorCodes.VALIDATION_ERROR);
        }

        // 验证模块存在
        var module = await _moduleRepository
            .Where(m => m.Id == request.ModuleId)
            .Select(m => new { m.IsEnabled })
            .FirstOrDefaultAsync();

        if (module == null)
        {
            return Fail<ModuleFunction>("Module not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        // 停用模块是级联操作而不是生效判据：建在停用模块下的功能随模块一起停用，
        // 否则停用模块下会冒出一个生效的码；重新启用模块时被级联带起来。
        var function = request.MapTo<ModuleFunction>();
        function.IsEnabled = module.IsEnabled;

        await _moduleFunctionRepository.InsertAsync(function);
        // A new enabled function belongs in the super-admin catalogue
        // immediately; without this, it would be invisible to super admins
        // until the catalogue TTL (1h) expires.
        await InvalidateSuperAdminCatalogueAsync();
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
        if (IsCatalogueWriteForbidden())
        {
            return Fail<ModuleFunction>(CatalogueIsHostManaged, 403, ErrorCodes.FORBIDDEN);
        }

        var function = await _moduleFunctionRepository.FindAsync(id);
        if (function == null)
        {
            return Fail<ModuleFunction>("Module function not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        // System-managed: Code / Name / Description / ModuleId / Category are owned
        // by the IPermissionDefinitionProvider (the seeder restores them on the
        // next startup); admin can only reorder here and flip IsEnabled through
        // its own endpoints. Category is ignored below rather than refused.
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
            if (!SameDeclaredText(function.Name, request.Name) || !SameDeclaredText(function.Description, request.Description))
            {
                return Fail<ModuleFunction>(
                    "The name and description of a system-managed function come from its IPermissionDefinitionProvider declaration and cannot be changed here; only the order can.",
                    403, ErrorCodes.IDENTITY_ROLE_SYSTEM_PROTECTED);
            }
        }

        // 检查代码是否被其他功能使用（大小写不敏感）
        if (function.Code != request.Code)
        {
            var codeExists = await IsFunctionCodeTakenAsync(request.Code, exceptId: id);

            if (codeExists)
            {
                return Fail<ModuleFunction>($"Function with code '{request.Code}' already exists", 409, ErrorCodes.VALIDATION_ERROR);
            }

            if (await IsCodeRenameForbiddenAsync(function.Code, request.Code))
            {
                return Fail<ModuleFunction>(
                    "Only a super administrator can change a permission code: the new code would be granted to everyone who holds this one.",
                    403, ErrorCodes.FORBIDDEN);
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

        // Category needs guarding:
        // - system-managed rows: category is a code-owned contract (the seeder
        //   re-asserts it); only Order is written for these rows, so admin
        //   category edits are ignored;
        // - admin-created rows: apply only when the request explicitly provides
        //   a value. A nullable-with-default would silently downgrade Technical
        //   to Business on any update that omits the field, erasing the
        //   warning badge assignment UIs rely on to flag dangerous surfaces.
        if (function.IsSystemManaged)
        {
            // 其余声明字段上面已判定与声明相同；只落排序。
            function.Order = request.Order;
        }
        else
        {
            var currentCategory = function.Category;
            request.MapTo(function);
            function.Category = request.Category ?? currentCategory;
        }
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
        if (IsCatalogueWriteForbidden())
        {
            return Fail(CatalogueIsHostManaged, 403, ErrorCodes.FORBIDDEN);
        }

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

        // 检查是否有角色关联。功能是全局行，授权按租户：关联要跨租户数（绕开租户过滤器，软删照旧排除），
        // 否则调用者看不见别家租户还挂着的授权，删掉之后那些授权指向一行墓碑。
        var hasRoleFunctions = await _roleFunctionRepository.AsQueryable()
            .IgnoreQueryFilters()
            .Where(rf => rf.FunctionId == id && !rf.IsDeleted)
            .AnyAsync();

        if (hasRoleFunctions)
        {
            return Fail("Cannot delete function with role associations", 400, ErrorCodes.VALIDATION_ERROR);
        }

        // 检查是否有用户直授关联（同上，跨租户）
        var hasUserFunctions = await _userFunctionRepository.AsQueryable()
            .IgnoreQueryFilters()
            .Where(uf => uf.FunctionId == id && !uf.IsDeleted)
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
        if (IsCatalogueWriteForbidden())
        {
            return Fail(CatalogueIsHostManaged, 403, ErrorCodes.FORBIDDEN);
        }

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
        if (IsCatalogueWriteForbidden())
        {
            return Fail(CatalogueIsHostManaged, 403, ErrorCodes.FORBIDDEN);
        }

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
        if (IsCatalogueWriteForbidden())
        {
            return Fail(CatalogueIsHostManaged, 403, ErrorCodes.FORBIDDEN);
        }

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
        if (IsCatalogueWriteForbidden())
        {
            return Fail(CatalogueIsHostManaged, 403, ErrorCodes.FORBIDDEN);
        }

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
    /// 谁持有这个码（访问审查的反向问题）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 持有者判定与 <see cref="CheckPermissionAsync"/> 同一套规则，只是从码出发：超管角色成员一律持有
    /// （检查最前短路，否定行与功能是否生效都不看）；其余人经「授权行启用且功能生效」的角色、或启用的用户直授持有，
    /// 用户级否定行把他扣掉。「功能是否生效」与 <see cref="FunctionLiveness"/> 同一判据（启用且未退役）。
    /// </para>
    /// <para>
    /// 常数次查询，与角色数、人数无关：功能、授权行、角色（含超管角色识别）、全部相关角色的成员（一次批量）、
    /// 用户直授 / 否定、范围收窄、用户名。刻意不经每用户权限缓存：缓存按用户分片，反查面要的是此刻库里的事实。
    /// </para>
    /// <para>
    /// ★ 超管角色的识别与 <see cref="IsSuperAdminAsync"/> 同口径：按角色<b>名</b>、在内存里用同一个比较器
    /// （<see cref="StringComparer.OrdinalIgnoreCase"/>）比，不交给 SQL 折叠大小写。角色表与
    /// <see cref="IUserRoleService"/> 走同一个 DbContext 的全局过滤器，多租户下两边看见的是同一批角色。
    /// </para>
    /// <para>
    /// ★ 用户名只给持有 <c>user.view</c> 的调用者（超管照常放行）：这个端点的门是 <c>authorization.permission.view</c>，
    /// 而成批的账号名（包括每一个超管账号）本属于用户管理面。没有 <c>user.view</c> 时名单照样完整，只是只有 id，
    /// <see cref="PermissionHoldersDto.UserNamesHidden"/> 为 true。没有用户上下文的内部调用照常带用户名。
    /// </para>
    /// </remarks>
    public async Task<Result<PermissionHoldersDto>> GetPermissionHoldersAsync(string permissionName)
    {
        if (string.IsNullOrWhiteSpace(permissionName))
        {
            return Fail<PermissionHoldersDto>("Permission name cannot be empty", 400, ErrorCodes.VALIDATION_ERROR);
        }

        // 角色到成员这一跳、超管角色的识别都离不开 Identity。缺了它只能答出直授的那一小撮人，
        // 漏掉的恰恰是超管与全部经角色持有的人。宁可明说做不到，也不给一份看起来完整的短名单。
        // 租户范围同理：它是安全边界，缺了它就只能把别家租户的账号一起列出来。
        if (_userRoleService == null || _roleRepository == null || _userScope == null)
        {
            return Fail<PermissionHoldersDto>(
                "Listing permission holders needs the Identity module's role membership, role lookup and user tenant scope, which are not available.", 501);
        }

        // 码大小写不敏感（与运行时检查同口径），所以同一个码可能对应不止一行；展示用写法完全一致的那一行。
        var requested = permissionName.Trim();
        var lowered = requested.ToLowerInvariant();
        var functions = await _moduleFunctionRepository
            .Where(f => f.Code.ToLower() == lowered)
            .Select(f => new { f.Id, f.Code, f.Name, f.Category, f.IsEnabled, f.IsRetired })
            .ToListAsync();
        if (functions.Count == 0)
        {
            return Fail<PermissionHoldersDto>($"Permission '{requested}' not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        var primary = functions.FirstOrDefault(f => f.Code == requested)
            ?? functions.FirstOrDefault(f => f.IsEnabled && !f.IsRetired)
            ?? functions[0];
        var functionIds = functions.Select(f => f.Id).ToList();
        var liveIds = functions.Where(f => f.IsEnabled && !f.IsRetired).Select(f => f.Id).ToList();

        // 1. 授权行：行停用的、指向休眠功能的也取，角色视图要把它们标出来。
        var grants = await _roleFunctionRepository
            .Where(rf => functionIds.Contains(rf.FunctionId))
            .Select(rf => new { rf.RoleId, rf.FunctionId, rf.IsEnabled })
            .ToListAsync();

        // 2. 角色名与超管角色，一次查询。超管角色只能在内存里认：SQL 的 upper() / lower() 在 SQLite 与
        //    C 排序规则的 PostgreSQL 上只折叠 ASCII，库里的 'général' 折不成 'GÉNÉRAL'，按它预筛会让配置了
        //    非 ASCII 名字的超管角色整个消失，而 IsSuperAdminAsync 照样认他们是超管。角色表很小，按 (Id, Name) 全取。
        //    旁路收窄到平台码、而这是一个应用码时，超管角色对它没有特权：按普通角色看它的授权行。
        var superNames = new HashSet<string>(
            BypassCovers(requested)
                ? (_options?.Value.SuperAdminRoles ?? []).Where(n => !string.IsNullOrWhiteSpace(n))
                : [],
            StringComparer.OrdinalIgnoreCase);
        var roles = await _roleRepository
            .Select(r => new { r.Id, r.Name })
            .ToListAsync();
        var roleNames = roles.ToDictionary(r => r.Id, r => r.Name ?? string.Empty);
        var superRoleIds = roles
            .Where(r => r.Name != null && superNames.Contains(r.Name))
            .Select(r => r.Id)
            .ToHashSet();

        // 每个角色与这个码的关系：超管角色压过一切，其余看授权行。
        var states = superRoleIds.ToDictionary(id => id, _ => PermissionRoleGrantState.SuperAdmin);
        foreach (var roleGrants in grants.GroupBy(g => g.RoleId).Where(g => !states.ContainsKey(g.Key)))
        {
            states[roleGrants.Key] = roleGrants.Any(g => g.IsEnabled && liveIds.Contains(g.FunctionId))
                ? PermissionRoleGrantState.Effective
                : roleGrants.Any(g => g.IsEnabled)
                    ? PermissionRoleGrantState.Dormant
                    : PermissionRoleGrantState.GrantDisabled;
        }

        // 3. 全部相关角色的成员，一次批量（不起作用的角色也要报成员数）。
        var membersByRole = states.Count == 0
            ? new Dictionary<Guid, IEnumerable<Guid>>()
            : await _userRoleService.GetRoleUserIdsAsync(states.Keys.ToList()) ?? new Dictionary<Guid, IEnumerable<Guid>>();
        IEnumerable<Guid> MembersOf(Guid roleId) => membersByRole.TryGetValue(roleId, out var ids) ? ids : [];

        // 4. 用户直授 / 否定：只有生效功能上的启用行参与解析（GetExplicitUserPermissionNamesAsync 同判据）。
        var userRows = liveIds.Count == 0
            ? []
            : await _userFunctionRepository
                .Where(uf => liveIds.Contains(uf.FunctionId) && uf.IsEnabled)
                .Select(uf => new { uf.UserId, uf.IsGranted })
                .ToListAsync();
        var direct = userRows.Where(r => r.IsGranted).Select(r => r.UserId).ToHashSet();
        var denied = userRows.Where(r => !r.IsGranted).Select(r => r.UserId).ToHashSet();

        // 5. 持有路径：超管角色在前，其后是生效的授予角色，同组按名称。
        var roleOrder = states
            .OrderBy(s => s.Value)
            .ThenBy(s => roleNames.GetValueOrDefault(s.Key, string.Empty), StringComparer.OrdinalIgnoreCase)
            .Select(s => s.Key)
            .ToList();
        var rolesByUser = new Dictionary<Guid, List<Guid>>();
        foreach (var roleId in roleOrder.Where(id => states[id] is PermissionRoleGrantState.SuperAdmin or PermissionRoleGrantState.Effective))
        {
            foreach (var userId in MembersOf(roleId))
            {
                if (!rolesByUser.TryGetValue(userId, out var via))
                {
                    rolesByUser[userId] = via = [];
                }
                via.Add(roleId);
            }
        }

        var superAdmins = superRoleIds.SelectMany(MembersOf).ToHashSet();

        // 否定行只对非超管有效：超管在检查最前短路。
        var holderIds = rolesByUser.Keys.Union(direct)
            .Where(id => superAdmins.Contains(id) || !denied.Contains(id))
            .ToList();
        var deniedIds = denied.Where(id => !superAdmins.Contains(id)).ToList();

        // 6. 收窄到调用者范围内、账号仍存在的那些，调用者看得了账号时顺带取用户名。
        var withNames = await CallerMayReadUserNamesAsync();
        var accounts = await ResolveVisibleAccountsAsync(
            holderIds.Concat(deniedIds).Concat(states.Keys.SelectMany(MembersOf)).ToHashSet(), _userScope, withNames);

        return Ok(new PermissionHoldersDto
        {
            FunctionId = primary.Id,
            Code = primary.Code,
            Name = primary.Name,
            Category = primary.Category,
            IsEnabled = primary.IsEnabled,
            IsRetired = primary.IsRetired,
            IsInEffect = liveIds.Count > 0,
            UserNamesHidden = !withNames,
            Roles = roleOrder.Select(roleId => new PermissionHolderRoleDto
            {
                Id = roleId,
                Name = roleNames.GetValueOrDefault(roleId, string.Empty),
                State = states[roleId],
                MemberCount = MembersOf(roleId).Count(accounts.ContainsKey)
            }).ToList(),
            Users = holderIds
                .Where(accounts.ContainsKey)
                .Select(userId => new PermissionHolderUserDto
                {
                    Id = userId,
                    UserName = accounts[userId],
                    IsSuperAdmin = superAdmins.Contains(userId),
                    IsDirect = direct.Contains(userId),
                    Roles = rolesByUser.TryGetValue(userId, out var via)
                        ? via.Select(roleId => new PermissionHolderRoleRefDto
                        {
                            Id = roleId,
                            Name = roleNames.GetValueOrDefault(roleId, string.Empty)
                        }).ToList()
                        : []
                })
                .OrderBy(u => u.UserName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(u => u.Id)
                .ToList(),
            DeniedUsers = deniedIds
                .Where(accounts.ContainsKey)
                .Select(userId => new PermissionDeniedUserDto { Id = userId, UserName = accounts[userId] })
                .OrderBy(u => u.UserName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(u => u.Id)
                .ToList()
        });
    }

    /// <summary>
    /// 目标账号是否在调用者的租户范围内（范围提供者缺席时不判）。
    /// </summary>
    /// <remarks>
    /// 按用户 id 读权限的两个管理端入口（检查某人有没有某码、列出某人的有效权限）此前接受任意 id：
    /// 用户-角色关联不按租户过滤，租户 A 的管理员借此读得到别家租户账号的权限，
    /// 越界的计算结果还曾写进那个人自己读的缓存。越界与不存在同一个回答，口径同 <see cref="UserTenantScope"/>。
    /// </remarks>
    private async Task<bool> IsUserInCallerScopeAsync(Guid userId)
        => _userScope == null || await _userScope.ContainsAsync(userId);

    /// <summary>
    /// 调用者能否看到持有者的用户名：持有 <c>user.view</c>（超管照常放行），或者没有用户上下文（框架内部调用）。
    /// </summary>
    private async Task<bool> CallerMayReadUserNamesAsync()
    {
        var callerId = CurrentUser?.Id;
        if (callerId == null || callerId == Guid.Empty) return true;
        return await CheckPermissionAsync(callerId.Value, UserViewPermission);
    }

    /// <summary>
    /// 把一批用户 id 收窄到调用者租户范围内、账号仍存在的那些；<paramref name="withNames"/> 为 true 时取用户名，否则用户名留空。
    /// </summary>
    /// <remarks>
    /// 用户表不按租户过滤（登录链路要跨租户找人），范围由 <see cref="IUserTenantScopeProvider"/> 补；
    /// 已软删的账号登不进来，不算持有者。用户仓储缺席时不判存在、用户名留空。
    /// </remarks>
    private async Task<Dictionary<Guid, string>> ResolveVisibleAccountsAsync(
        IReadOnlyCollection<Guid> userIds, IUserTenantScopeProvider userScope, bool withNames)
    {
        var inScope = userIds.Count > 0 ? await userScope.FilterAsync(userIds) : userIds;
        if (inScope.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        if (_userRepository == null)
        {
            return inScope.Distinct().ToDictionary(id => id, _ => string.Empty);
        }

        var idList = inScope.ToList();
        var existing = _userRepository.Where(u => idList.Contains(u.Id));
        return withNames
            ? await existing
                .Select(u => new { u.Id, u.UserName })
                .ToDictionaryAsync(u => u.Id, u => u.UserName ?? string.Empty)
            : await existing
                .Select(u => u.Id)
                .ToDictionaryAsync(id => id, _ => string.Empty);
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
        // 与 GetUserPermissionNamesAsync 同一个结果，只是超管判定只做一次（它是一次不经缓存的角色查询，
        // 而访问画像在每次进管理端时都要取）。
        var isSuperAdmin = await IsSuperAdminAsync(userId);
        var permissions = !isSuperAdmin
            ? await GetExplicitUserPermissionNamesAsync(userId)
            : BypassCoversEveryCode
                ? await GetSuperAdminCatalogueAsync()
                : await GetNarrowedSuperAdminPermissionNamesAsync(userId);
        return Ok(new AccessProfileDto
        {
            IsSuperAdmin = isSuperAdmin,
            Permissions = permissions.ToList(),
            // 只在收窄时下发：前端据此停止按 isSuperAdmin 放行，只认这份列表。默认模式不写这个字段。
            SuperAdminBypassScope = BypassCoversEveryCode ? null : _bypassPolicy!.Scope,
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

        // Get source role's enabled function IDs. Only grants on functions that are
        // currently in effect are copied: a dormant grant (retired / disabled function)
        // would be a row nobody reads, and its code is in no grantor's effective set,
        // so a non-super grantor could never clone a role that carries one.
        var liveFunctions = _moduleFunctionRepository.Where(f => f.IsEnabled && !f.IsRetired);
        var sourceFunctionIds = await _roleFunctionRepository
            .Where(rf => rf.RoleId == sourceRoleId && rf.IsEnabled)
            .Join(liveFunctions, rf => rf.FunctionId, f => f.Id, (rf, _) => rf.FunctionId)
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
    /// Role names configured in <c>Authorization:SuperAdminRoles</c> (read-only).
    /// The permission-matrix UI renders those roles as read-only explanatory rows,
    /// because their explicit RoleFunction rows have no effect on members.
    /// </summary>
    public IReadOnlyList<string> GetSuperAdminRoleNames()
        => _options?.Value.SuperAdminRoles ?? [];

    /// <summary>
    /// Import function assignments to a role from exported data
    /// </summary>
    public async Task<Result<PermissionImportResultDto>> ImportRolePermissionsAsync(Guid roleId, RolePermissionExportDto importData)
    {
        if (importData.FunctionCodes.Count == 0)
        {
            return Ok(new PermissionImportResultDto());
        }

        // Resolve function codes to IDs. A code that exists here but is retired or
        // disabled resolves to nothing: importing it would write a grant nobody reads.
        // It is reported under NotFound, which is what it is in this deployment.
        var codeList = importData.FunctionCodes.Distinct().ToList();
        var functions = await _moduleFunctionRepository
            .Where(f => codeList.Contains(f.Code) && f.IsEnabled && !f.IsRetired)
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
    /// 模块码是否已被（另一个）模块占用，大小写不敏感。
    /// </summary>
    /// <remarks>
    /// ★ 判重必须与运行时同一个比较口径。权限检查、播种器、持有者反查都按大小写不敏感认码，
    /// 而 SQL 的 <c>==</c> 与唯一索引在 PostgreSQL / SQLite 上区分大小写：此前建得出与系统码只差大小写的行，
    /// 播种器把两行收进同一个不区分大小写的字典时抛异常、整轮跳过（之后每次启动都是），
    /// 功能码上更能借改码拿到一个系统码的「别名」，运行时按它放行。码按约定是 ASCII，
    /// 两侧都降成小写比较（服务端 <c>LOWER()</c>，客户端 <c>ToLowerInvariant</c>）在各数据库上同义。
    /// </remarks>
    private Task<bool> IsModuleCodeTakenAsync(string code, Guid? exceptId = null)
    {
        var lowered = (code ?? string.Empty).ToLowerInvariant();
        return _moduleRepository
            .Where(m => m.Code.ToLower() == lowered && (exceptId == null || m.Id != exceptId))
            .AnyAsync();
    }

    /// <summary>
    /// 功能码是否已被（另一个）功能占用，大小写不敏感（理由见 <see cref="IsModuleCodeTakenAsync"/>）。
    /// </summary>
    private Task<bool> IsFunctionCodeTakenAsync(string code, Guid? exceptId = null)
    {
        var lowered = (code ?? string.Empty).ToLowerInvariant();
        return _moduleFunctionRepository
            .Where(f => f.Code.ToLower() == lowered && (exceptId == null || f.Id != exceptId))
            .AnyAsync();
    }

    /// <summary>
    /// 非超管能否改一个功能的码：不能（只改大小写除外）。
    /// </summary>
    /// <remarks>
    /// 角色授权与用户直授按功能 id 挂在这一行上，改码等于把新码一次授给这一行的每一个持有者，
    /// 却不经过「只能授出自己持有的码」那道护栏。新码在判重之后不对应任何现存的行，任何非超管都不可能持有它，
    /// 所以这里就是「只有超管能改码」。没有用户上下文的内部调用不受限。
    /// 只差大小写的改动不改变任何人的权限（码处处大小写不敏感），照常放行。
    /// </remarks>
    private async Task<bool> IsCodeRenameForbiddenAsync(string currentCode, string newCode)
    {
        if (string.Equals(currentCode, newCode, StringComparison.OrdinalIgnoreCase)) return false;

        var callerId = CurrentUser?.Id;
        if (callerId == null || callerId == Guid.Empty) return false;
        return !await IsSuperAdminAsync(callerId.Value);
    }

    /// <summary>
    /// 把 <paramref name="moduleId"/> 挂到 <paramref name="parentId"/> 下会不会成环；会则返回英文说明，不会返回 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// ★★★ 只拦「自己做自己的父」不够：把模块挂到自己的子孙下面，同样造出一个谁都不是根的环。
    /// 环一旦落库，级联启停的遍历永不结束（事务与连接一直占着），模块树里整组连同功能一起消失，
    /// 而模块列表返回的实体带着关系修复填进去的 Children，序列化溢出深度、整页 500，界面上再也修不回来。
    /// 从新父模块沿父链向上走，碰到自己就是挂进了自己的子树；碰到走过的节点说明那条链本身已经成环
    /// （守卫之前留下的数据），挂上去同样从树上不可达，一并拒绝。
    /// 模块表是一张小的目录表，一次读出全部父子关系在内存里走，比逐级查询便宜，也不受链长影响。
    /// </remarks>
    private async Task<string?> DescribeParentCycleAsync(Guid moduleId, Guid parentId)
    {
        var parentOf = await _moduleRepository
            .Select(m => new { m.Id, m.ParentId })
            .ToDictionaryAsync(m => m.Id, m => m.ParentId);

        var visited = new HashSet<Guid>();
        Guid? current = parentId;
        while (current.HasValue)
        {
            if (current.Value == moduleId)
            {
                return "Module cannot be moved under itself or one of its descendants";
            }

            if (!visited.Add(current.Value))
            {
                return "The parent module's ancestry is cyclic; fix its parent first";
            }

            current = parentOf.GetValueOrDefault(current.Value);
        }

        return null;
    }

    /// <summary>
    /// 模块是否在一个父链环上（沿父链向上能走回自己）。只在内存里的一批模块上判断。
    /// </summary>
    private static bool IsOnParentCycle(FunctionModule module, IReadOnlyDictionary<Guid, FunctionModule> modulesById)
    {
        var visited = new HashSet<Guid>();
        var current = module.ParentId;
        while (current.HasValue && visited.Add(current.Value))
        {
            if (current.Value == module.Id)
            {
                return true;
            }

            current = modulesById.TryGetValue(current.Value, out var parent) ? parent.ParentId : null;
        }

        return false;
    }

    /// <summary>
    /// 级联设置模块及其所有子模块的启用状态，同时更新关联功能
    /// </summary>
    /// <remarks>
    /// 遍历按模块去重：写入口已拒绝成环，但库里可能留着守卫之前的环数据，
    /// 没有去重的广度优先遍历在环上永不结束。
    /// </remarks>
    private async Task<List<Guid>> SetModuleEnabledCascadeAsync(Guid moduleId, bool enabled)
    {
        var affectedIds = new List<Guid>();
        var visited = new HashSet<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(moduleId);

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            if (!visited.Add(currentId))
            {
                continue;
            }

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
    /// 此刻清一遍，身处事务时提交后再清一遍（见 <see cref="PermissionCacheInvalidation"/>）。
    /// </remarks>
    private async Task InvalidateAllCacheAsync()
    {
        if (_functionAuthCache != null)
        {
            await PermissionCacheInvalidation.RunAsync(
                _functionAuthCache, cache => cache.ClearAllAsync(), UnitOfWorkManager, PostCommitActionQueue, Logger, "all users");
        }
    }

    /// <summary>
    /// 只作废超管全功能目录（新增一个启用的功能时：它属于目录，不属于任何人的显式授权）。
    /// </summary>
    private async Task InvalidateSuperAdminCatalogueAsync()
    {
        if (_functionAuthCache != null)
        {
            await PermissionCacheInvalidation.RunAsync(
                _functionAuthCache, cache => cache.InvalidateSuperAdminCatalogueAsync(), UnitOfWorkManager, PostCommitActionQueue, Logger, "the super-admin catalogue");
        }
    }

    /// <summary>
    /// Invalidate only the users actually reachable by a single function -
    /// users granted that function via either path
    /// (<see cref="RoleFunction"/>+UserRole, direct <see cref="UserFunction"/>),
    /// plus the super-admin catalogue
    /// (which always includes every enabled function). Cheaper than
    /// <see cref="InvalidateAllCacheAsync"/> for the common
    /// "admin toggled one function" path.
    /// </summary>
    private async Task InvalidateCacheForFunctionAsync(Guid functionId)
    {
        if (_functionAuthCache == null || _userRoleService == null || _multiTenancyEnabled)
        {
            // Without the user-role lookup we can't compute the precise
            // user set, so fall back to the safe global clear.
            // Multi-tenant: the function row is global but the grant rows below
            // are tenant-filtered, so the precise set would only cover the
            // caller's tenant and every other tenant's holders would keep a
            // stale entry until the TTL. A function toggle is rare; clear all.
            await InvalidateAllCacheAsync();
            return;
        }

        HashSet<Guid> affectedUserIds;
        try
        {
            // (a) Direct UserFunction grants on the function itself.
            var directGrantUserIds = await _userFunctionRepository
                .Where(uf => uf.FunctionId == functionId && uf.IsEnabled)
                .Select(uf => uf.UserId)
                .ToListAsync();

            // (b) Role-scoped grants - collect every role that carries this
            // function via RoleFunction and fan out to their users.
            var roleFunctionRoleIds = await _roleFunctionRepository
                .Where(rf => rf.FunctionId == functionId && rf.IsEnabled)
                .Select(rf => rf.RoleId)
                .ToListAsync();

            affectedUserIds = new HashSet<Guid>(directGrantUserIds);
            foreach (var roleId in roleFunctionRoleIds.Distinct())
            {
                var roleUsers = await _userRoleService.GetRoleUserIdsAsync(roleId);
                if (roleUsers != null)
                {
                    foreach (var uid in roleUsers) affectedUserIds.Add(uid);
                }
            }
        }
        catch (Exception ex)
        {
            // Precise invalidation is an optimisation, not a correctness
            // guarantee - if the holder set cannot be computed (DB transient
            // error etc.), fall back to a global clear so we never leave stale entries.
            Logger.LogWarning(ex,
                "Precise cache invalidation failed for function {FunctionId}; falling back to global clear.",
                functionId);
            await InvalidateAllCacheAsync();
            return;
        }

        // The super-admin catalogue is derived from "all enabled
        // functions" - any function enable/disable affects it.
        // A failed removal falls back to a global clear inside the helper.
        await PermissionCacheInvalidation.RunAsync(
            _functionAuthCache,
            async cache =>
            {
                if (affectedUserIds.Count > 0)
                {
                    await cache.RemoveUserPermissionNamesAsync(affectedUserIds);
                }

                await cache.InvalidateSuperAdminCatalogueAsync();
            },
            UnitOfWorkManager,
            PostCommitActionQueue,
            Logger,
            $"holders of function {functionId}");
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
    /// <remarks>
    /// 此刻清一遍，身处事务时提交后再清一遍；按人删除失败退到全清，全清也失败则抛出
    /// （见 <see cref="PermissionCacheInvalidation"/>）。此前失败被吞成一条警告、接口照样报成功，
    /// 被撤销的码在缓存期内继续放行。成员查不出来时同样退到全清：不知道该删谁，就不能假装删过了。
    /// </remarks>
    /// <param name="roleId">角色ID</param>
    private async Task InvalidateRoleCacheAsync(Guid roleId)
    {
        if (_functionAuthCache == null || _userRoleService == null)
        {
            return;
        }

        List<Guid> userIds;
        try
        {
            userIds = (await _userRoleService.GetRoleUserIdsAsync(roleId))?.ToList() ?? [];
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not list the members of role {RoleId}; clearing the whole permission cache instead.", roleId);
            await InvalidateAllCacheAsync();
            return;
        }

        if (userIds.Count == 0)
        {
            return;
        }

        await PermissionCacheInvalidation.RunAsync(
            _functionAuthCache,
            cache => cache.RemoveUserPermissionNamesAsync(userIds),
            UnitOfWorkManager,
            PostCommitActionQueue,
            Logger,
            $"members of role {roleId}");
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

