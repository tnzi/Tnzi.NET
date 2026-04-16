using System.Threading;
using System.Threading.Tasks;

namespace Tnzi.AI.Tools.Sql;

/// <summary>
/// Extension point for consumers to apply custom permission rules
/// (e.g. RBAC, tenant-scoped access) before a query executes.
/// </summary>
public interface IReadOnlySqlPermissionCheck
{
    Task<SqlPermissionResult> CheckAsync(string sql, CancellationToken ct = default);
}

public sealed record SqlPermissionResult(bool Allowed, string? DenialReason);
