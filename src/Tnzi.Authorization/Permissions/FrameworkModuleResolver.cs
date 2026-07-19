namespace Tnzi.Authorization.Permissions;

/// <summary>
/// Resolves which permission modules are FRAMEWORK built-ins from the running
/// module graph. A framework business module is a <see cref="TnziApplicationModule"/>
/// whose assembly starts with <c>Tnzi.</c>; its short name (assembly minus that
/// prefix — <c>Identity</c>, <c>AI</c>, <c>Finance</c>) equals the permission
/// group code it owns (<c>identity</c>, <c>ai</c>, <c>finance</c>), so
/// classification is a plain case-insensitive lookup with no hardcoded map and
/// it auto-adapts when framework modules are added or removed.
/// </summary>
/// <remarks>
/// Two callers must agree on this set:
/// <list type="bullet">
///   <item>each module's own <c>{Module}Permissions</c> provider (e.g.
///     <c>IdentityPermissions</c>, <c>AuthorizationPermissions</c>) declares a
///     group only when its module is loaded, so an app that never
///     <c>[DependsOn]</c>s Finance/AI/Chat does not seed those permissions at
///     all.</item>
///   <item>the module admin read path flags each module row <c>IsBuiltIn</c> so
///     the role-permission matrix can list a consumer application's own
///     permissions first and separate the built-in framework catalogue.</item>
/// </list>
/// </remarks>
internal static class FrameworkModuleResolver
{
    private const string Prefix = "Tnzi.";

    /// <summary>
    /// Short names (assembly minus the <c>Tnzi.</c> prefix) of the loaded
    /// framework business modules — the <see cref="TnziApplicationModule"/>s in
    /// <c>Tnzi.*</c> assemblies. Case-insensitive so a lookup against a lowercase
    /// group code (<c>identity</c>) matches the short name (<c>Identity</c>).
    /// </summary>
    public static HashSet<string> GetLoadedFrameworkModuleCodes(ITnziApplication application)
    {
        Check.NotNull(application);
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in application.Modules)
        {
            if (module.Instance is not TnziApplicationModule) continue;
            var name = module.Assembly.GetName().Name;
            if (string.IsNullOrEmpty(name) || !name.StartsWith(Prefix, StringComparison.Ordinal)) continue;
            set.Add(name.Substring(Prefix.Length));
        }
        return set;
    }
}
