using Tnzi.Security.Authorization;

namespace Tnzi.AI.Tools.Sql;

/// <summary>
/// <see cref="IReadOnlySqlPermissionCheck"/> backed by the framework <see cref="IPermissionChecker"/>.
/// Requires the caller to hold the <c>AI.Sql.Execute</c> permission. Applications that need
/// finer-grained per-table or per-schema control should compose this with their own check or
/// implement <see cref="IReadOnlySqlPermissionCheck"/> directly.
/// </summary>
public sealed class FrameworkPermissionSqlCheck : IReadOnlySqlPermissionCheck
{
    public const string ExecutePermission = "AI.Sql.Execute";

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
