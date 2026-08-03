namespace Tnzi.AI.Tools.Approval.Sse;

/// <summary>
/// Store for pending tool-call approval requests awaiting a user decision.
/// Default impl is in-memory (single-process); replace via DI to support multi-instance
/// deployments (e.g. a Redis-backed implementation).
/// </summary>
public interface IPendingApprovalStore
{
    /// <summary>
    /// Registers a new pending approval request.
    /// </summary>
    /// <param name="request">The request - <see cref="PendingApprovalRequest.UserId"/> MUST be non-empty.</param>
    /// <param name="ttl">Time after which the request is auto-resolved as a fail-closed timeout (default 5 minutes).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The registered request ID.</returns>
    Task<Guid> RegisterAsync(PendingApprovalRequest request, TimeSpan? ttl = null, CancellationToken ct = default);

    /// <summary>
    /// Returns the request if it exists and the supplied user owns it (or <paramref name="currentUserId"/> is null
    /// - admin contexts can pass null to bypass authz).
    /// </summary>
    Task<PendingApprovalRequest?> GetAsync(Guid id, string? currentUserId = null, CancellationToken ct = default);

    /// <summary>
    /// Resolves a pending request with a user decision.
    /// Returns <see cref="ResolveResult.NotAuthorized"/> when <paramref name="currentUserId"/> does not match the
    /// request owner - never silently grants other users' approvals.
    /// </summary>
    Task<ResolveResult> ResolveAsync(
        Guid id,
        ApprovalDecision decision,
        string? currentUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves a request without authorization checks - for trusted system callers
    /// (HTTP disconnect cleanup, retention sweeps, shutdown). MUST NOT be exposed to
    /// unauthenticated user code; the framework only invokes this from infrastructure
    /// hooks that have already established the authority to bypass the user check.
    /// Implementations should treat a missing entry as a no-op.
    /// </summary>
    Task ResolveAsSystemAsync(Guid id, ApprovalDecision decision, CancellationToken ct = default);

    /// <summary>
    /// Lists all pending requests. When <paramref name="currentUserId"/> is non-null, only returns
    /// requests owned by that user.
    /// </summary>
    Task<IReadOnlyList<PendingApprovalRequest>> ListPendingAsync(
        string? currentUserId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Awaits a decision for the given request id. Completes when:
    /// <list type="bullet">
    ///   <item>A user resolves it via <see cref="ResolveAsync"/></item>
    ///   <item>The TTL expires (returns a system-generated <see cref="ApprovalDecision"/> with Approved=false)</item>
    ///   <item><paramref name="ct"/> is cancelled (throws <see cref="OperationCanceledException"/>)</item>
    /// </list>
    /// </summary>
    Task<ApprovalDecision> AwaitDecisionAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// Result of an attempted resolve. <see cref="bool"/> would conflate "not found" with "not authorized" -
/// the explicit enum lets the controller return precise error messages and metrics.
/// </summary>
public enum ResolveResult
{
    /// <summary>The decision was accepted and the waiter completed.</summary>
    Resolved,
    /// <summary>No pending entry exists for this id (already resolved, expired, or never registered).</summary>
    NotFound,
    /// <summary>The supplied user does not own this approval request.</summary>
    NotAuthorized
}

/// <summary>
/// Represents a tool-call awaiting user approval.
/// </summary>
/// <param name="Id">Unique identifier for this request.</param>
/// <param name="ToolName">The name of the tool being invoked.</param>
/// <param name="Arguments">Tool arguments as a JSON-serialized string.</param>
/// <param name="CreatedAt">When the request was registered.</param>
/// <param name="UserId">The user who owns this approval request - MUST be non-empty.</param>
public sealed record PendingApprovalRequest(
    Guid Id,
    string ToolName,
    string Arguments,
    DateTimeOffset CreatedAt,
    string UserId);

public sealed record ApprovalDecision(bool Approved, string? Reason, string? DecidedBy);
