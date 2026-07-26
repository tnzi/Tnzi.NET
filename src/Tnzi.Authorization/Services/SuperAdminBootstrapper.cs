namespace Tnzi.Authorization.Services;

/// <summary>
/// Startup bootstrap for the "first super admin": assigns the users listed in
/// <c>Authorization:BootstrapSuperAdminUsers</c> to the first existing
/// super-admin role, but ONLY while every configured super-admin role has zero
/// members. A zero-member super-admin role means the system has no recovery
/// account at all (fresh install or accidental lockout), so re-applying on
/// restart is deliberate recovery behaviour - once any member exists the
/// bootstrap is a no-op. Never creates users, never touches passwords.
/// </summary>
/// <remarks>
/// Role membership goes through <see cref="IUserService.AssignRolesAsync"/> so
/// the regular <c>UserRolesChangedEvent</c> fires and the Authorization
/// permission cache drops the affected user (a stale shared-cache entry from a
/// previous deployment would otherwise keep the fresh super admin powerless
/// until the TTL expires). The delegation guardrail inside that path skips
/// automatically because startup has no current user.
/// Identity dependencies are optional-injected in the same defensive style as
/// <see cref="FunctionAuthorizationService"/>: a host without full Identity
/// wiring logs a warning and skips instead of failing startup.
/// </remarks>
public class SuperAdminBootstrapper
{
    private readonly ILogger<SuperAdminBootstrapper> _logger;
    private readonly IUserService? _userService;
    private readonly IUserRoleService? _userRoleService;
    private readonly IRepository<Tnzi.Identity.Entities.Role, Guid>? _roleRepository;
    private readonly IRepository<Tnzi.Identity.Entities.User, Guid>? _userRepository;

    public SuperAdminBootstrapper(
        ILogger<SuperAdminBootstrapper> logger,
        IUserService? userService = null,
        IUserRoleService? userRoleService = null,
        IRepository<Tnzi.Identity.Entities.Role, Guid>? roleRepository = null,
        IRepository<Tnzi.Identity.Entities.User, Guid>? userRepository = null)
    {
        _logger = Check.NotNull(logger);
        _userService = userService;
        _userRoleService = userRoleService;
        _roleRepository = roleRepository;
        _userRepository = userRepository;
    }

    /// <summary>
    /// Runs the bootstrap. Returns the number of users actually assigned
    /// (0 when skipped for any reason - missing wiring, existing members,
    /// unknown user names).
    /// </summary>
    public async Task<int> BootstrapAsync(IReadOnlyList<string> superAdminRoleNames, IReadOnlyList<string> userNames)
    {
        var roleNames = Normalize(superAdminRoleNames);
        var names = Normalize(userNames);
        if (roleNames.Count == 0 || names.Count == 0)
        {
            return 0;
        }

        if (_userService == null || _userRoleService == null || _roleRepository == null || _userRepository == null)
        {
            _logger.LogWarning(
                "Authorization.BootstrapSuperAdminUsers is configured but the Identity services are unavailable; skipping bootstrap.");
            return 0;
        }

        // Resolve the configured super-admin roles that actually exist
        // (SeedBuiltInAdminRolesAsync ran just before this, so normally all
        // of them do).
        var normalizedRoleNames = roleNames.Select(n => n.ToUpperInvariant()).ToList();
        var roles = await _roleRepository.ToListAsync(r =>
            r.NormalizedName != null && normalizedRoleNames.Contains(r.NormalizedName));
        if (roles.Count == 0)
        {
            _logger.LogWarning(
                "Authorization.BootstrapSuperAdminUsers is configured but none of the super-admin roles ({Roles}) exist; " +
                "enable SeedBuiltInAdminRoles or create the role first.",
                string.Join(", ", roleNames));
            return 0;
        }

        // Zero-member gate across ALL existing super-admin roles: any member
        // anywhere means the system already has a super admin and the
        // bootstrap must not touch role membership again.
        foreach (var role in roles)
        {
            var memberIds = await _userRoleService.GetRoleUserIdsAsync(role.Id);
            if (memberIds.Any())
            {
                _logger.LogInformation(
                    "Super-admin bootstrap skipped: role '{Role}' already has members.", role.Name);
                return 0;
            }
        }

        // Target = the first configured role name that exists, preserving the
        // deployment's configured order.
        var rolesByNormalizedName = roles.ToDictionary(r => r.NormalizedName!, StringComparer.OrdinalIgnoreCase);
        var targetRole = normalizedRoleNames
            .Select(n => rolesByNormalizedName.GetValueOrDefault(n))
            .First(r => r != null)!;

        var assigned = 0;
        foreach (var name in names)
        {
            var normalized = name.ToUpperInvariant();
            var user = (await _userRepository.ToListAsync(u => u.NormalizedUserName == normalized)).FirstOrDefault();
            if (user == null)
            {
                _logger.LogWarning(
                    "Super-admin bootstrap: user '{User}' does not exist; create the user first (bootstrap never creates accounts).",
                    name);
                continue;
            }

            var result = await _userService.AssignRolesAsync(user.Id, [targetRole.Id]);
            if (result.Succeeded)
            {
                assigned++;
                _logger.LogInformation(
                    "Super-admin bootstrap: assigned user '{User}' to role '{Role}'.", name, targetRole.Name);
            }
            else
            {
                _logger.LogWarning(
                    "Super-admin bootstrap: assigning user '{User}' to role '{Role}' failed: {Message}",
                    name, targetRole.Name, result.Message);
            }
        }

        return assigned;
    }

    private static List<string> Normalize(IReadOnlyList<string> values)
        => values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
