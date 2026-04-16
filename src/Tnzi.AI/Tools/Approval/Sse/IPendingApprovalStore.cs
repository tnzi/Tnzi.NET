using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Tnzi.AI.Tools.Approval.Sse;

/// <summary>
/// Store for pending tool-call approval requests awaiting a user decision.
/// Default impl is in-memory; can be replaced (e.g., Redis) by registering a different implementation.
/// </summary>
public interface IPendingApprovalStore
{
    Task<Guid> RegisterAsync(PendingApprovalRequest request, CancellationToken ct = default);
    Task<PendingApprovalRequest?> GetAsync(Guid id, CancellationToken ct = default);
    Task<bool> ResolveAsync(Guid id, ApprovalDecision decision, CancellationToken ct = default);
    Task<IReadOnlyList<PendingApprovalRequest>> ListPendingAsync(CancellationToken ct = default);
}

/// <summary>
/// Represents a tool-call awaiting user approval.
/// </summary>
/// <param name="Id">Unique identifier for this request.</param>
/// <param name="ToolName">The name of the tool being invoked.</param>
/// <param name="Arguments">Tool arguments as a JSON-serialized string.</param>
/// <param name="CreatedAt">When the request was registered.</param>
/// <param name="UserId">Optional user identifier (for audit / multi-tenant scenarios).</param>
public sealed record PendingApprovalRequest(
    Guid Id,
    string ToolName,
    string Arguments,
    DateTimeOffset CreatedAt,
    string? UserId);

public sealed record ApprovalDecision(bool Approved, string? Reason, string? DecidedBy);
