namespace Tnzi.Authorization.Services;

/// <summary>
/// Which permission codes the super-admin bypass covers
/// (<c>Authorization:SuperAdminBypassScope</c>): the single answer every
/// decision path asks, so the endpoint gate, in-service checks, the access
/// profile, the holder list and the permission picture cannot disagree.
/// </summary>
/// <remarks>
/// <para>
/// This only answers "does the bypass reach this code". Whether a user is a
/// super admin at all stays <see cref="FunctionAuthorizationService.IsSuperAdminAsync"/>,
/// and its other callers (target-protection guards, hiding super admins from
/// business directories, role delegation) ask about identity, not access, so
/// they are deliberately not narrowed.
/// </para>
/// <para>
/// In <see cref="Options.SuperAdminBypassScope.All"/> mode the scope registry is never built.
/// </para>
/// </remarks>
public class SuperAdminBypassPolicy
{
    private readonly Lazy<PermissionScopeRegistry> _registry;

    /// <summary>DI constructor: reads the options and resolves the providers on first use.</summary>
    public SuperAdminBypassPolicy(IOptions<Options.AuthorizationOptions> options, IServiceScopeFactory scopeFactory, ILogger<SuperAdminBypassPolicy>? logger = null)
    {
        Check.NotNull(options);
        Check.NotNull(scopeFactory);
        var value = options.Value;
        Scope = value.SuperAdminBypassScope;
        var overrides = value.PermissionScopeOverrides;
        _registry = new Lazy<PermissionScopeRegistry>(() =>
        {
            using var scope = scopeFactory.CreateScope();
            var providers = scope.ServiceProvider.GetServices<IPermissionDefinitionProvider>().ToList();
            return PermissionScopeRegistry.Build(providers, overrides, logger);
        });
    }

    private SuperAdminBypassPolicy(Options.SuperAdminBypassScope scope, PermissionScopeRegistry registry)
    {
        Scope = scope;
        _registry = new Lazy<PermissionScopeRegistry>(registry);
    }

    /// <summary>A policy over an already-built registry (tests and hosts that compose services by hand).</summary>
    public static SuperAdminBypassPolicy Create(Options.SuperAdminBypassScope scope, PermissionScopeRegistry registry)
        => new(scope, Check.NotNull(registry));

    /// <summary>The configured bypass scope.</summary>
    public Options.SuperAdminBypassScope Scope { get; }

    /// <summary>True when the bypass covers every code, declared or not (today's behaviour).</summary>
    public bool CoversEveryCode => Scope == Options.SuperAdminBypassScope.All;

    /// <summary>The scope registry; built on first access.</summary>
    public PermissionScopeRegistry Registry => _registry.Value;

    /// <summary>
    /// Whether a super admin passes <paramref name="permissionName"/> without an explicit grant.
    /// In <see cref="Options.SuperAdminBypassScope.Platform"/> mode only declared platform codes qualify;
    /// application codes and undeclared codes do not (fail closed).
    /// </summary>
    public bool Covers(string permissionName)
        => CoversEveryCode || Registry.GetScope(permissionName) == PermissionScope.Platform;
}
