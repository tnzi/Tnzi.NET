namespace Tnzi.AI.Tools.Sql;

/// <summary>
/// Default <see cref="IReadOnlySqlPermissionCheck"/> implementation that rejects every SQL
/// execution request. Registered by AIModule so applications must explicitly opt into a
/// permissive implementation (e.g. <see cref="FrameworkPermissionSqlCheck"/>) before AI
/// agents can run SQL. This is fail-secure by design — when in doubt, deny.
/// </summary>
public sealed class DenyAllSqlPermissionCheck : IReadOnlySqlPermissionCheck
{
    public Task<SqlPermissionResult> CheckAsync(string sql, CancellationToken ct = default)
        => Task.FromResult(new SqlPermissionResult(false,
            "AI SQL execution is not permitted: register a custom IReadOnlySqlPermissionCheck (e.g. FrameworkPermissionSqlCheck) to enable."));
}
