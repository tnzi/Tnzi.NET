namespace Tnzi.Authorization.Permissions;

/// <summary>
/// Which <see cref="PermissionScope"/> every declared permission group and code
/// belongs to, resolved once from the registered
/// <see cref="IPermissionDefinitionProvider"/>s plus
/// <c>Authorization:PermissionScopeOverrides</c>.
/// </summary>
/// <remarks>
/// <para>
/// A group's scope, first rule that answers:
/// </para>
/// <list type="number">
///   <item>the deployment override for its code;</item>
///   <item>the scope a provider declared on it (<see cref="PermissionGroupDefinition.Scope"/>);</item>
///   <item>its parent group's resolved scope, when it has one;</item>
///   <item><see cref="PermissionScope.Platform"/> when the provider that created the group
///     lives in a framework (<c>Tnzi.*</c>) assembly, <see cref="PermissionScope.Application"/> otherwise.</item>
/// </list>
/// <para>
/// A code takes the scope of the nearest group up its <c>parentName</c> chain. A code
/// whose chain reaches no declared group takes the default of the provider that declared it.
/// A code no provider declares has no scope at all (<see cref="GetScope"/> returns <c>null</c>):
/// callers deciding access treat that as "not platform", so the narrowed bypass fails closed.
/// </para>
/// <para>
/// ★ The resolution needs to know which provider created a group, which the merged
/// <see cref="PermissionDefinitionContext"/> forgets. So the providers run into one shared
/// context (same first-wins semantics as the seeder) and the group keys are compared before
/// and after each provider.
/// </para>
/// </remarks>
public sealed class PermissionScopeRegistry
{
    private const int MaxParentDepth = 32;

    private readonly Dictionary<string, PermissionScope> _groupScopes;
    private readonly Dictionary<string, PermissionScope> _codeScopes;

    private PermissionScopeRegistry(Dictionary<string, PermissionScope> groupScopes, Dictionary<string, PermissionScope> codeScopes)
    {
        _groupScopes = groupScopes;
        _codeScopes = codeScopes;
    }

    /// <summary>Resolved scope of every declared group, keyed by group code (case-insensitive).</summary>
    public IReadOnlyDictionary<string, PermissionScope> GroupScopes => _groupScopes;

    /// <summary>Scope of a declared permission code; <c>null</c> when no provider declares it.</summary>
    public PermissionScope? GetScope(string permissionName)
        => !string.IsNullOrEmpty(permissionName) && _codeScopes.TryGetValue(permissionName, out var scope) ? scope : null;

    /// <summary>Scope of a declared group; <c>null</c> when no provider declares it (e.g. an admin-created module).</summary>
    public PermissionScope? GetGroupScope(string groupCode)
        => !string.IsNullOrEmpty(groupCode) && _groupScopes.TryGetValue(groupCode, out var scope) ? scope : null;

    /// <summary>Whether an assembly name counts as framework code for the default scope.</summary>
    public static bool IsFrameworkAssembly(string? assemblyName)
        => assemblyName != null && (assemblyName == "Tnzi" || assemblyName.StartsWith("Tnzi.", StringComparison.Ordinal));

    /// <summary>
    /// Resolve every group and code the providers declare.
    /// </summary>
    /// <param name="providers">The registered permission definition providers.</param>
    /// <param name="overrides">Deployment overrides keyed by group code; may be null.</param>
    /// <param name="logger">Receives provider failures and overrides that name no declared group.</param>
    public static PermissionScopeRegistry Build(IEnumerable<IPermissionDefinitionProvider> providers, IReadOnlyDictionary<string, PermissionScope>? overrides = null, ILogger? logger = null)
    {
        Check.NotNull(providers);

        var context = new PermissionDefinitionContext();
        var groupOwners = new Dictionary<string, string?>(StringComparer.Ordinal);
        var permissionOwners = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var provider in providers)
        {
            var groupsBefore = context.Groups.Keys.ToHashSet(StringComparer.Ordinal);
            var permissionsBefore = context.Permissions.Keys.ToHashSet(StringComparer.Ordinal);
            try
            {
                provider.Define(context);
            }
            catch (Exception ex)
            {
                // What it declared before throwing is still real; anything it did not get to stays
                // undeclared and therefore outside the narrowed bypass (fails closed).
                logger?.LogError(ex,
                    "PermissionDefinitionProvider {Provider} threw while resolving permission scopes; codes it did not declare are treated as application-scoped.",
                    provider.GetType().FullName ?? provider.GetType().Name);
            }

            var assembly = provider.GetType().Assembly.GetName().Name;
            foreach (var key in context.Groups.Keys.Where(k => !groupsBefore.Contains(k)))
            {
                groupOwners[key] = assembly;
            }
            foreach (var key in context.Permissions.Keys.Where(k => !permissionsBefore.Contains(k)))
            {
                permissionOwners[key] = assembly;
            }
        }

        var overrideMap = new Dictionary<string, PermissionScope>(StringComparer.OrdinalIgnoreCase);
        if (overrides != null)
        {
            foreach (var (groupCode, scope) in overrides)
            {
                if (string.IsNullOrWhiteSpace(groupCode)) continue;
                overrideMap[groupCode.Trim()] = scope;
                if (!context.Groups.Keys.Any(k => string.Equals(k, groupCode.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    logger?.LogWarning(
                        "Authorization.PermissionScopeOverrides references group {Group} which no IPermissionDefinitionProvider declares; entry ignored.",
                        groupCode);
                }
            }
        }

        var groupScopes = new Dictionary<string, PermissionScope>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in context.Groups.Keys)
        {
            // First declaration wins on a case-variant collision, as in the seeder.
            if (!groupScopes.ContainsKey(name))
            {
                groupScopes[name] = ResolveGroup(name, context, groupOwners, overrideMap, depth: 0);
            }
        }

        var codeScopes = new Dictionary<string, PermissionScope>(StringComparer.OrdinalIgnoreCase);
        foreach (var permission in context.Permissions.Values)
        {
            if (codeScopes.ContainsKey(permission.Name)) continue;
            var group = FindOwningGroup(permission, context);
            codeScopes[permission.Name] = group != null && groupScopes.TryGetValue(group, out var scope)
                ? scope
                : DefaultFor(permissionOwners.GetValueOrDefault(permission.Name));
        }

        return new PermissionScopeRegistry(groupScopes, codeScopes);
    }

    private static PermissionScope ResolveGroup(string name, PermissionDefinitionContext context, Dictionary<string, string?> owners, Dictionary<string, PermissionScope> overrides, int depth)
    {
        if (overrides.TryGetValue(name, out var overridden)) return overridden;

        var group = context.Groups[name];
        if (group.Scope is { } declared) return declared;

        if (depth < MaxParentDepth
            && !string.IsNullOrEmpty(group.ParentName)
            && !string.Equals(group.ParentName, name, StringComparison.Ordinal)
            && context.Groups.ContainsKey(group.ParentName))
        {
            return ResolveGroup(group.ParentName, context, owners, overrides, depth + 1);
        }

        return DefaultFor(owners.GetValueOrDefault(name));
    }

    /// <summary>Nearest declared group up the code's <c>parentName</c> chain (parents may be groups or other codes).</summary>
    private static string? FindOwningGroup(PermissionDefinition permission, PermissionDefinitionContext context)
    {
        var parent = permission.ParentName;
        for (var depth = 0; depth < MaxParentDepth && !string.IsNullOrEmpty(parent); depth++)
        {
            if (context.Groups.ContainsKey(parent)) return parent;
            if (!context.Permissions.TryGetValue(parent, out var next)) return null;
            parent = next.ParentName;
        }
        return null;
    }

    private static PermissionScope DefaultFor(string? owner)
        => IsFrameworkAssembly(owner) ? PermissionScope.Platform : PermissionScope.Application;
}
