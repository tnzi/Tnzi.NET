
namespace Tnzi.AI.Tools.Sql;

/// <summary>
/// <see cref="IReadOnlySqlPermissionCheck"/> backed by the framework <see cref="IPermissionChecker"/>.
/// Requires the caller to hold the <c>ai.sql.execute</c> permission. This permission is registered
/// as a framework permission definition (Tnzi.Authorization <c>FrameworkPermissions</c>, group <c>ai</c>),
/// so it can be granted to roles via the standard permission UI. Backend permission checks are
/// case-insensitive. Applications that need finer-grained per-table or per-schema control should compose
/// this with their own check or implement <see cref="IReadOnlySqlPermissionCheck"/> directly.
/// </summary>
public sealed class FrameworkPermissionSqlCheck : IReadOnlySqlPermissionCheck
{
    public const string ExecutePermission = "ai.sql.execute";

    private readonly IPermissionChecker _permissionChecker;

    public FrameworkPermissionSqlCheck(IPermissionChecker permissionChecker)
    {
        _permissionChecker = Check.NotNull(permissionChecker);
    }

    public async Task<SqlPermissionResult> CheckAsync(string sql, CancellationToken ct = default)
    {
        var allowed = await _permissionChecker.IsGrantedAsync(ExecutePermission);
        return allowed
            ? new SqlPermissionResult(true, null)
            : new SqlPermissionResult(false, $"Permission '{ExecutePermission}' is required to execute AI SQL queries.");
    }
}
