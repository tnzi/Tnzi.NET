using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.Security.Claims;

namespace Tnzi.AI.Tools.Approval.Sse;

/// <summary>
/// Tool approval handler that emits pending requests to a request-scoped <see cref="ApprovalRequestCollector"/>
/// (typically wired to an SSE stream) and awaits a decision via <see cref="IPendingApprovalStore"/>.
///
/// Flow:
///   1. Framework calls <see cref="RequestApprovalAsync"/> just before executing a write-group tool.
///   2. The handler resolves the request-scoped <see cref="ApprovalRequestCollector"/> and the current
///      user from the ambient HTTP context.
///   3. A <see cref="PendingApprovalRequest"/> bound to the current user id is emitted to the channel.
///   4. The SSE controller detects the channel item and emits an <c>approval_request</c> event to the frontend.
///   5. The request is registered with a TTL in <see cref="IPendingApprovalStore"/> only after successful emit.
///   6. <see cref="HttpContext.RequestAborted"/> is wired to fail-closed cleanup if the client disconnects.
///   7. Execution blocks on the store waiter until the user responds, the TTL expires, or cancellation occurs.
///   8. User clicks Approve/Reject → the approval endpoint calls <see cref="IPendingApprovalStore.ResolveAsync"/>
///      with the user id (the store rejects mismatched users with <see cref="ResolveResult.NotAuthorized"/>).
///
/// Registered as Singleton — captive-dependency safe because the scoped <see cref="ApprovalRequestCollector"/>
/// and <see cref="ICurrentUser"/> are resolved per-call from the ambient HTTP request scope via
/// <see cref="IHttpContextAccessor"/>.
/// </summary>
public sealed class SseToolApprovalHandler : IToolApprovalHandler
{
    private readonly IPendingApprovalStore _store;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<SseToolApprovalHandler> _logger;

    public SseToolApprovalHandler(
        IPendingApprovalStore store,
        IHttpContextAccessor httpContextAccessor,
        ILogger<SseToolApprovalHandler> logger)
    {
        _store = Check.NotNull(store);
        _httpContextAccessor = Check.NotNull(httpContextAccessor);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task<ToolApprovalResult> RequestApprovalAsync(
        ToolApprovalRequest request,
        CancellationToken ct = default)
    {
        Check.NotNull(request);

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            _logger.LogWarning(
                "No HTTP context available; cannot emit SSE approval request for tool={Tool}. Rejecting.",
                request.ToolName);
            return ToolApprovalResult.Reject("No active HTTP context for SSE approval.", rejectedBy: "system");
        }

        // Bind the request to the current authenticated user so only they can resolve it.
        var currentUser = httpContext.RequestServices.GetService<ICurrentUser>();
        var userId = ResolveUserId(currentUser, request);
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning(
                "Cannot determine user identity for SSE approval (tool={Tool}). Rejecting.",
                request.ToolName);
            return ToolApprovalResult.Reject("Anonymous tool approval is not allowed.", rejectedBy: "system");
        }

        var collector = httpContext.RequestServices.GetService<ApprovalRequestCollector>();
        if (collector is null)
        {
            _logger.LogWarning(
                "ApprovalRequestCollector not in current scope; cannot emit SSE approval for tool={Tool}.",
                request.ToolName);
            return ToolApprovalResult.Reject("SSE collector not available.", rejectedBy: "system");
        }

        var id = Guid.NewGuid();
        var pending = new PendingApprovalRequest(
            Id: id,
            ToolName: request.ToolName,
            Arguments: SerializeArguments(request),
            CreatedAt: request.RequestedAt,
            UserId: userId);

        // Emit BEFORE registering — a failed emit must not leave an orphan entry in the store.
        try
        {
            await collector.WriteAsync(pending, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SSE approval emit failed for tool={Tool}; no store entry registered.",
                request.ToolName);
            return ToolApprovalResult.Reject("Approval emit failed: " + ex.Message, rejectedBy: "system");
        }

        await _store.RegisterAsync(pending, ttl: null, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Tool approval required: {ToolName} (group: {ToolGroup}), id: {Id}, userId: {UserId}",
            request.ToolName, request.ToolGroup, id, userId);

        // Cleanup on client disconnect — without this the entry would linger until TTL.
        // Fire-and-forget against the store; ResolveAsSystemAsync is a no-op if the entry
        // is already gone (user resolved first or sweep won the race).
        var disconnectRegistration = httpContext.RequestAborted.Register(() =>
        {
            try
            {
                _store.ResolveAsSystemAsync(id, new ApprovalDecision(
                    Approved: false,
                    Reason: "Client disconnected before responding to approval prompt.",
                    DecidedBy: "system"));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Approval disconnect cleanup failed for id={Id}", id);
            }
        });

        try
        {
            var decision = await _store.AwaitDecisionAsync(id, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Tool approval resolved: approved={Approved} for {ToolName}, id: {Id}, decidedBy: {DecidedBy}",
                decision.Approved, request.ToolName, id, decision.DecidedBy);

            return decision.Approved
                ? ToolApprovalResult.Approve(decision.DecidedBy)
                : ToolApprovalResult.Reject(decision.Reason, decision.DecidedBy);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "Tool approval cancelled for {ToolName}, id: {Id}",
                request.ToolName, id);
            throw;
        }
        finally
        {
            await disconnectRegistration.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static string? ResolveUserId(ICurrentUser? currentUser, ToolApprovalRequest request)
    {
        if (currentUser is { IsAuthenticated: true, Id: { } guid })
            return guid.ToString();

        // Fallback: caller-supplied context (e.g. CLI scenarios that pass userId explicitly).
        return request.Context.TryGetValue("userId", out var ctxUserId) && !string.IsNullOrEmpty(ctxUserId)
            ? ctxUserId
            : null;
    }

    private string SerializeArguments(ToolApprovalRequest request)
    {
        if (request.Arguments is not { Count: > 0 })
            return "{}";

        try
        {
            return System.Text.Json.JsonSerializer.Serialize(request.Arguments);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException || ex is NotSupportedException)
        {
            _logger.LogWarning(ex,
                "Failed to serialize approval arguments for tool {Tool}; emitting empty payload to UI.",
                request.ToolName);
            return "{}";
        }
    }
}
