namespace Tnzi.Authorization.Permissions;

/// <summary>
/// 把所有 <see cref="IPermissionDefinitionProvider"/> 声明的权限 / 模块
/// upsert 进数据库（<c>Auth_FunctionModule</c> + <c>Auth_ModuleFunction</c>），
/// 让代码声明的权限点能在 admin UI / RoleFunction / RoleManagement 里被
/// 看到、查询、分配。
/// </summary>
/// <remarks>
/// <para>
/// <b>Why</b>: provider-declared permissions used to live only in an
/// in-memory snapshot - they were invisible to admin pages and couldn't be
/// FK-referenced by <c>RoleFunction</c>. This seeder closes that gap (and is
/// now the only path by which a declared code exists at runtime) so a
/// developer can:
/// </para>
/// <list type="number">
///   <item>Implement <see cref="IPermissionDefinitionProvider"/> in a module</item>
///   <item>Restart the app → rows appear in DB, marked <c>IsSystemManaged=true</c></item>
///   <item>Admin assigns them to roles in the regular RoleFunction UI</item>
///   <item><c>[ApiAuthorize(PermissionName="...")]</c> just works</item>
/// </list>
/// <para>
/// <b>Semantics</b>: this seeder is purely additive + content-update.
/// </para>
/// <list type="bullet">
///   <item><b>Add</b>: declared but not in DB → insert with <c>IsSystemManaged=true</c>.</item>
///   <item><b>Update</b>: declared and in DB → update <c>Name</c>/<c>Description</c>
///     /<c>ParentId</c> to match the code declaration. Preserves admin's
///     <c>IsEnabled</c> toggle (ops can disable a permission point without
///     redeploying code; redeploying will not silently re-enable).</item>
///   <item><b>Retire</b>: only <b>system-managed</b> rows whose code is no
///     longer declared by any provider, because system-managed rows are
///     code-owned and a lingering row would keep dead codes grantable in the
///     assignment matrix (e.g. the retired <c>Admin.Manage</c> outer gate).
///     By default the row is <b>marked</b> <c>IsRetired</c> and kept together
///     with all its grants, so a host that simply does not load the declaring
///     module loses nothing and gets everything back when it does
///     (<c>Authorization:PermissionRetirement</c>, see
///     <see cref="Options.PermissionRetirementMode"/>).
///     Admin-created rows are never touched. Retirement is skipped for the
///     whole run when any provider's <c>Define</c> threw: a partial
///     collection cannot tell "no longer shipped" from "broken today".</item>
/// </list>
/// </remarks>
public class PermissionDbSeeder
{
    private readonly IRepository<FunctionModule, Guid> _moduleRepository;
    private readonly IRepository<ModuleFunction, Guid> _functionRepository;
    private readonly ILogger<PermissionDbSeeder> _logger;
    private readonly IOptions<Options.AuthorizationOptions>? _options;
    private readonly IRepository<RoleFunction, Guid>? _roleFunctionRepository;
    private readonly IRepository<UserFunction, Guid>? _userFunctionRepository;

    public PermissionDbSeeder(
        IRepository<FunctionModule, Guid> moduleRepository,
        IRepository<ModuleFunction, Guid> functionRepository,
        ILogger<PermissionDbSeeder> logger,
        IOptions<Options.AuthorizationOptions>? options = null,
        IRepository<RoleFunction, Guid>? roleFunctionRepository = null,
        IRepository<UserFunction, Guid>? userFunctionRepository = null)
    {
        _moduleRepository = Check.NotNull(moduleRepository);
        _functionRepository = Check.NotNull(functionRepository);
        _logger = Check.NotNull(logger);
        _options = options;
        _roleFunctionRepository = roleFunctionRepository;
        _userFunctionRepository = userFunctionRepository;
    }

    /// <summary>
    /// Walk every registered <see cref="IPermissionDefinitionProvider"/>,
    /// collect group + permission declarations, then upsert to DB.
    /// </summary>
    /// <returns>
    /// Number of rows inserted + updated. Zero on no-op runs (re-startup
    /// with unchanged providers). Failures are logged but do not throw -
    /// startup must not block on this auxiliary step.
    /// </returns>
    public async Task<int> SeedAsync(IEnumerable<IPermissionDefinitionProvider> providers, CancellationToken cancellationToken = default)
    {
        Check.NotNull(providers);

        // Materialise once: the sequence is enumerated here and counted in the
        // closing log line, and a deferred GetServices() enumerable would resolve
        // a second set of provider instances on the second pass.
        var providerList = providers as IReadOnlyCollection<IPermissionDefinitionProvider> ?? providers.ToList();

        // Collapse all provider outputs into one context. Duplicate names
        // across providers are first-wins.
        var context = new PermissionDefinitionContext();
        // Providers whose Define threw. A partial collection can still be
        // upserted (what we did see is real), but it must never drive
        // retirement: "not declared this run" would then mean "that provider
        // has a bug today", not "this deployment no longer ships the code".
        var failedProviders = new List<string>();
        foreach (var provider in providerList)
        {
            try
            {
                provider.Define(context);
            }
            catch (Exception ex)
            {
                var providerName = provider.GetType().FullName ?? provider.GetType().Name;
                failedProviders.Add(providerName);
                _logger.LogError(ex,
                    "PermissionDefinitionProvider {Provider} threw during Define; its declarations are skipped this seed run and no permission is retired this run.",
                    providerName);
            }
        }

        if (context.Groups.Count == 0 && context.Permissions.Count == 0)
        {
            _logger.LogDebug("No IPermissionDefinitionProvider declarations to seed.");
            return 0;
        }

        // Deployment-level category overrides win over provider declarations.
        // Applied on the collapsed definition set so both the insert and the
        // update paths below see the effective category.
        var categoryOverrides = _options?.Value?.PermissionCategoryOverrides;
        if (categoryOverrides is { Count: > 0 })
        {
            // context.Permissions is keyed case-sensitively; permission checks
            // are case-insensitive everywhere else, so match overrides the same way.
            var declaredByCode = context.Permissions.Values
                .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var (code, category) in categoryOverrides)
            {
                if (declaredByCode.TryGetValue(code, out var declared))
                {
                    declared.Category = category;
                }
                else
                {
                    _logger.LogWarning(
                        "Authorization.PermissionCategoryOverrides references code {Code} which no IPermissionDefinitionProvider declares; entry ignored.",
                        code);
                }
            }
        }

        // Pass 1: upsert modules (groups), top-down so parents exist before
        // children. We use a `code` lookup to map declarations to DB rows.
        var existingModules = await _moduleRepository.AsQueryable().ToListAsync(cancellationToken);
        var moduleByCode = existingModules.ToDictionary(m => m.Code, StringComparer.OrdinalIgnoreCase);
        var touched = 0;

        // Topologically sort group declarations: parents first.
        var orderedGroups = TopoSortGroups(context.Groups.Values);
        foreach (var group in orderedGroups)
        {
            Guid? parentId = null;
            if (!string.IsNullOrEmpty(group.ParentName)
                && moduleByCode.TryGetValue(group.ParentName, out var parent))
            {
                parentId = parent.Id;
            }

            if (moduleByCode.TryGetValue(group.Name, out var existing))
            {
                // Update content while preserving admin-controlled fields
                // (IsEnabled, Order). Only rename/reparent fields tracked
                // by the provider DSL.
                var changed = false;
                if (existing.Name != group.DisplayName) { existing.Name = group.DisplayName; changed = true; }
                if (existing.Description != group.Description) { existing.Description = group.Description; changed = true; }
                if (existing.ParentId != parentId) { existing.ParentId = parentId; changed = true; }
                // Reclaim ownership: if a row was admin-created with the
                // same code, mark it system-managed now that a provider
                // declares it. This is intentional - code-as-truth wins.
                if (!existing.IsSystemManaged) { existing.IsSystemManaged = true; changed = true; }
                // Un-retire: the declaring module is loaded again.
                if (existing.IsRetired) { existing.IsRetired = false; changed = true; }
                if (changed)
                {
                    await _moduleRepository.UpdateAsync(existing, cancellationToken: cancellationToken);
                    touched++;
                }
            }
            else
            {
                var inserted = new FunctionModule
                {
                    Code = group.Name,
                    Name = group.DisplayName,
                    Description = group.Description,
                    ParentId = parentId,
                    IsEnabled = group.IsEnabled,
                    IsSystemManaged = true,
                };
                await _moduleRepository.InsertAsync(inserted, cancellationToken: cancellationToken);
                moduleByCode[group.Name] = inserted;
                touched++;
            }
        }

        // Pass 2: upsert functions. Lookup needs module reference, hence
        // (moduleCode, functionCode) tuple for uniqueness.
        var existingFunctions = await _functionRepository.AsQueryable().ToListAsync(cancellationToken);
        var functionByCode = existingFunctions.ToDictionary(f => f.Code, StringComparer.OrdinalIgnoreCase);
        foreach (var perm in context.Permissions.Values)
        {
            // The group is the permission's parent module - fall back to
            // ParentName for declarations made outside a group block.
            var moduleCode = perm.Group?.Name ?? perm.ParentName;
            if (string.IsNullOrEmpty(moduleCode))
            {
                _logger.LogWarning(
                    "Permission {Name} declared without a module/group; skipping. Wrap it in context.AddGroup(...).AddPermission(...).",
                    perm.Name);
                continue;
            }
            if (!moduleByCode.TryGetValue(moduleCode, out var module))
            {
                _logger.LogWarning(
                    "Permission {Name} references module {Module} but no such module was declared; skipping.",
                    perm.Name, moduleCode);
                continue;
            }

            if (functionByCode.TryGetValue(perm.Name, out var existing))
            {
                var changed = false;
                if (existing.ModuleId != module.Id) { existing.ModuleId = module.Id; changed = true; }
                if (existing.Name != perm.DisplayName) { existing.Name = perm.DisplayName; changed = true; }
                if (existing.Description != perm.Description) { existing.Description = perm.Description; changed = true; }
                // Category is a code-owned contract (like Name/Description),
                // not an admin toggle - the provider declaration wins.
                if (existing.Category != perm.Category) { existing.Category = perm.Category; changed = true; }
                if (!existing.IsSystemManaged) { existing.IsSystemManaged = true; changed = true; }
                // Un-retire: this deployment declares the code again, so the row
                // becomes grantable and every grant that was kept reattaches.
                if (existing.IsRetired) { existing.IsRetired = false; changed = true; }
                if (changed)
                {
                    await _functionRepository.UpdateAsync(existing, cancellationToken: cancellationToken);
                    touched++;
                }
            }
            else
            {
                var inserted = new ModuleFunction
                {
                    Code = perm.Name,
                    Name = perm.DisplayName,
                    Description = perm.Description,
                    ModuleId = module.Id,
                    IsEnabled = perm.IsEnabled,
                    Category = perm.Category,
                    IsSystemManaged = true,
                };
                await _functionRepository.InsertAsync(inserted, cancellationToken: cancellationToken);
                functionByCode[perm.Name] = inserted;
                touched++;
            }
        }

        // Pass 3: retire system-managed rows the code no longer declares.
        // System-managed rows are code-owned (this seeder re-asserts their
        // content every boot), so a vanished declaration means the code is not
        // part of THIS deployment - a lingering grantable row would keep dead
        // codes in the assignment matrix. Admin-created rows are never touched.
        //
        // How it retires is a deployment decision, and the default is
        // Options.PermissionRetirementMode.Disable - marking the row rather than
        // deleting it. See PermissionRetirementMode for why: the commonest
        // reason a code stops being declared is a host that does not load the
        // owning module, and deleting takes the role grants with it,
        // irreversibly (the soft-delete filter hides the tombstone, so
        // re-declaring inserts a fresh id and the grants never reattach).
        //
        // Retirement only runs when THIS collection is complete. A provider
        // that threw contributed nothing, so every code it owns would look
        // orphaned; Disable mode would silently strip those permissions from
        // every non-super-admin user, and Delete mode would drop their role
        // grants and user direct grants irreversibly - all while the process
        // starts and the health check stays green.
        var retirement = _options?.Value?.PermissionRetirement ?? Options.PermissionRetirementMode.Disable;
        var declaredFunctionCodes = new HashSet<string>(
            context.Permissions.Values.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

        if (retirement != Options.PermissionRetirementMode.Off && failedProviders.Count > 0)
        {
            _logger.LogError(
                "PermissionDbSeeder: permission retirement skipped this run because {Count} provider(s) threw during Define ({Providers}). Fix the provider; retirement resumes on the next clean seed run.",
                failedProviders.Count, string.Join(", ", failedProviders));
        }
        else if (retirement != Options.PermissionRetirementMode.Off)
        {
            var orphanFunctions = existingFunctions
                .Where(f => f.IsSystemManaged && !f.IsRetired && !declaredFunctionCodes.Contains(f.Code))
                .ToList();
            foreach (var orphan in orphanFunctions)
            {
                if (retirement == Options.PermissionRetirementMode.Delete)
                {
                    // Both grant tables go with the row. ModuleFunction is soft-deleted, so
                    // the cascade on UserFunction.FunctionId never fires: without this
                    // explicit delete the allow/deny rows keep pointing at a tombstone, and
                    // re-declaring the code inserts a fresh id (the soft-delete filter hides
                    // the tombstone), leaving them permanently invisible and uncleanable.
                    // The manual delete path refuses a function that still has user grants;
                    // Delete mode is the deliberate "clean the database" choice, so it removes
                    // them instead.
                    if (_roleFunctionRepository != null)
                    {
                        await _roleFunctionRepository.DeleteAsync(rf => rf.FunctionId == orphan.Id, cancellationToken: cancellationToken);
                    }
                    if (_userFunctionRepository != null)
                    {
                        await _userFunctionRepository.DeleteAsync(uf => uf.FunctionId == orphan.Id, cancellationToken: cancellationToken);
                    }
                    await _functionRepository.DeleteAsync(f => f.Id == orphan.Id, cancellationToken: cancellationToken);
                    functionByCode.Remove(orphan.Code);
                    _logger.LogInformation(
                        "PermissionDbSeeder: deleted system-managed permission {Code} together with its role grants and user direct grants (no provider declares it anymore; Authorization:PermissionRetirement=Delete).",
                        orphan.Code);
                }
                else
                {
                    orphan.IsRetired = true;
                    await _functionRepository.UpdateAsync(orphan, cancellationToken: cancellationToken);
                    _logger.LogInformation(
                        "PermissionDbSeeder: retired system-managed permission {Code} (no provider declares it anymore). The row and its grants are kept and will come back if the declaring module is loaded again.",
                        orphan.Code);
                }
                touched++;
            }

            // Retire system-managed modules no provider declares once they hold
            // no live functions and no child modules (admin-created content keeps
            // the module alive).
            var declaredGroupCodes = new HashSet<string>(
                context.Groups.Values.Select(g => g.Name), StringComparer.OrdinalIgnoreCase);
            var liveModuleIds = functionByCode.Values.Where(f => !f.IsRetired).Select(f => f.ModuleId).ToHashSet();
            var orphanModules = moduleByCode.Values
                .Where(m => m.IsSystemManaged
                            && !m.IsRetired
                            && !declaredGroupCodes.Contains(m.Code)
                            && !liveModuleIds.Contains(m.Id)
                            && moduleByCode.Values.All(child => child.ParentId != m.Id))
                .ToList();
            foreach (var orphan in orphanModules)
            {
                if (retirement == Options.PermissionRetirementMode.Delete)
                {
                    await _moduleRepository.DeleteAsync(m => m.Id == orphan.Id, cancellationToken: cancellationToken);
                    moduleByCode.Remove(orphan.Code);
                }
                else
                {
                    orphan.IsRetired = true;
                    await _moduleRepository.UpdateAsync(orphan, cancellationToken: cancellationToken);
                }
                _logger.LogInformation(
                    "PermissionDbSeeder: retired empty system-managed module {Code} (no provider declares it anymore).",
                    orphan.Code);
                touched++;
            }
        }

        _logger.LogInformation(
            "PermissionDbSeeder: {Count} module/function row(s) inserted, updated or retired from {ProviderCount} provider(s).",
            touched, providerList.Count);
        return touched;
    }

    /// <summary>
    /// Topological sort of permission groups so a child group's parent
    /// is always processed first. Groups whose <c>ParentName</c> doesn't
    /// resolve are emitted last with parentId=null (admin sees them at
    /// the tree root and can manually re-parent).
    /// </summary>
    private static List<PermissionGroupDefinition> TopoSortGroups(IEnumerable<PermissionGroupDefinition> groups)
    {
        var byName = groups.ToDictionary(g => g.Name, StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<PermissionGroupDefinition>();

        void Visit(PermissionGroupDefinition g)
        {
            if (!visited.Add(g.Name)) return;
            if (!string.IsNullOrEmpty(g.ParentName)
                && byName.TryGetValue(g.ParentName, out var parent))
            {
                Visit(parent);
            }
            result.Add(g);
        }

        foreach (var g in byName.Values) Visit(g);
        return result;
    }
}
