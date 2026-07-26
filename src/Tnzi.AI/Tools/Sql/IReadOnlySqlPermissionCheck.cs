namespace Tnzi.AI.Tools.Sql;

/// <summary>
/// Permission check applied before <see cref="IReadOnlySqlExecutor"/> runs a query.
/// AIModule registers <see cref="DenyAllSqlPermissionCheck"/> as the fail-secure default -
/// applications must explicitly register a permissive implementation
/// (e.g. <see cref="FrameworkPermissionSqlCheck"/> or a custom check)
/// before AI agents can execute SQL.
/// </summary>
public interface IReadOnlySqlPermissionCheck
{
    Task<SqlPermissionResult> CheckAsync(string sql, CancellationToken ct = default);
}

public sealed record SqlPermissionResult(bool Allowed, string? DenialReason);
