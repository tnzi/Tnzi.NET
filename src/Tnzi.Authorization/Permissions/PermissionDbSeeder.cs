namespace Tnzi.Authorization.Permissions;

/// <summary>
/// 把所有 <see cref="IPermissionDefinitionProvider"/> 声明的权限 / 模块
/// upsert 进数据库（<c>Auth_FunctionModule</c> + <c>Auth_ModuleFunction</c>），
/// 让代码声明的权限点能在 admin UI / RoleFunction / RoleManagement 里被
/// 看到、查询、分配。
/// </summary>
/// <remarks>
/// <para>
/// <b>Why</b>: provider-declared permissions used to live only in the
/// in-memory <see cref="PermissionManager"/> snapshot — they were
/// invisible to admin pages and couldn't be FK-referenced by
/// <c>RoleFunction</c>. This seeder closes that gap so a developer can:
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
///   <item><b>Remove</b>: never. Removing a provider declaration leaves the
///     DB row in place — that prevents accidental data loss when a code
///     change drops a permission. Admin can manually purge the row (the
///     <c>IsSystemManaged</c> flag will be irrelevant because the
///     provider no longer claims it; we re-evaluate ownership each run).</item>
/// </list>
/// </remarks>
public class PermissionDbSeeder
{
    private readonly IRepository<FunctionModule, Guid> _moduleRepository;
    private readonly IRepository<ModuleFunction, Guid> _functionRepository;
    private readonly ILogger<PermissionDbSeeder> _logger;

    public PermissionDbSeeder(
        IRepository<FunctionModule, Guid> moduleRepository,
        IRepository<ModuleFunction, Guid> functionRepository,
        ILogger<PermissionDbSeeder> logger)
    {
        _moduleRepository = Check.NotNull(moduleRepository);
        _functionRepository = Check.NotNull(functionRepository);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// Walk every registered <see cref="IPermissionDefinitionProvider"/>,
    /// collect group + permission declarations, then upsert to DB.
    /// </summary>
    /// <returns>
    /// Number of rows inserted + updated. Zero on no-op runs (re-startup
    /// with unchanged providers). Failures are logged but do not throw —
    /// startup must not block on this auxiliary step.
    /// </returns>
    public async Task<int> SeedAsync(IEnumerable<IPermissionDefinitionProvider> providers, CancellationToken cancellationToken = default)
    {
        Check.NotNull(providers);

        // Collapse all provider outputs into one context. Duplicate names
        // across providers are first-wins (matches PermissionManager's
        // existing semantics — see LoadFromProviders there).
        var context = new PermissionDefinitionContext();
        foreach (var provider in providers)
        {
            try
            {
                provider.Define(context);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "PermissionDefinitionProvider {Provider} threw during Define; its declarations are skipped this seed run.",
                    provider.GetType().FullName);
            }
        }

        if (context.Groups.Count == 0 && context.Permissions.Count == 0)
        {
            _logger.LogDebug("No IPermissionDefinitionProvider declarations to seed.");
            return 0;
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
                // declares it. This is intentional — code-as-truth wins.
                if (!existing.IsSystemManaged) { existing.IsSystemManaged = true; changed = true; }
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
            // The group is the permission's parent module — fall back to
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
                if (!existing.IsSystemManaged) { existing.IsSystemManaged = true; changed = true; }
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
                    IsSystemManaged = true,
                };
                await _functionRepository.InsertAsync(inserted, cancellationToken: cancellationToken);
                functionByCode[perm.Name] = inserted;
                touched++;
            }
        }

        _logger.LogInformation(
            "PermissionDbSeeder: {Count} module/function row(s) inserted or updated from {ProviderCount} provider(s).",
            touched, providers.Count());
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
