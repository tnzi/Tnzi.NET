using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tnzi.DependencyInjection;

namespace Tnzi.AI.Tools.Approval.Sse;

/// <summary>
/// Tool approval handler that emits pending requests to a scoped <see cref="ApprovalRequestCollector"/>
/// (typically wired to an SSE stream) and awaits a decision via <see cref="InMemoryPendingApprovalStore"/>.
///
/// Flow:
///   1. Framework calls <see cref="RequestApprovalAsync"/> just before executing a write-group tool.
///   2. The handler resolves the request-scoped <see cref="ApprovalRequestCollector"/> from the current HTTP context.
///   3. A <see cref="PendingApprovalRequest"/> is emitted to the channel (fail-fast: no entry registered on emit failure).
///   4. The SSE controller detects the channel item and emits an <c>approval_request</c> event to the frontend.
///   5. The request is registered in <see cref="InMemoryPendingApprovalStore"/> (singleton) only after successful emit.
///   6. Execution blocks on the TCS until the user responds or cancellation occurs.
///   7. User clicks Approve/Reject → the approval endpoint calls <see cref="IPendingApprovalStore.ResolveAsync"/>.
///   8. <see cref="InMemoryPendingApprovalStore"/> completes the TCS and we return the result.
///
/// Registered as <see cref="ISingletonDependency"/> to avoid captive-dependency issues: <c>McpToolProvider</c>
/// is a Singleton that injects <c>IToolApprovalHandler?</c>. The scoped <see cref="ApprovalRequestCollector"/>
/// is resolved per-call from the ambient HTTP request scope via <see cref="IHttpContextAccessor"/>.
/// </summary>
public sealed class SseToolApprovalHandler : IToolApprovalHandler, ISingletonDependency
{
    private readonly InMemoryPendingApprovalStore _store;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<SseToolApprovalHandler> _logger;

    public SseToolApprovalHandler(
        InMemoryPendingApprovalStore store,
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

        var id = Guid.NewGuid();
        var userId = (request.Context.TryGetValue("userId", out var uidStr) && uidStr is { Length: > 0 })
            ? uidStr
            : null;

        var pending = new PendingApprovalRequest(
            Id: id,
            ToolName: request.ToolName,
            Arguments: SerializeArguments(request),
            CreatedAt: request.RequestedAt,
            UserId: userId);

        // Resolve the request-scoped collector from the ambient HTTP context so that
        // the same instance the SSE endpoint is reading from receives the pending request.
        var collector = ResolveCollector();
        if (collector is null)
        {
            _logger.LogWarning(
                "No HTTP context available; cannot emit SSE approval request for tool={Tool}. Rejecting.",
                request.ToolName);
            return ToolApprovalResult.Reject("No active HTTP context for SSE approval.", rejectedBy: "system");
        }

        // Issue #2 fix: emit BEFORE registering — a failed emit must not leave an orphan entry in the store.
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

        // Emit succeeded — register so the resolve endpoint can complete the TCS.
        await _store.RegisterAsync(pending, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Tool approval required: {ToolName} (group: {ToolGroup}), id: {Id}",
            request.ToolName, request.ToolGroup, id);

        try
        {
            var decision = await _store.AwaitDecisionAsync(id, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Tool approval resolved: approved={Approved} for {ToolName}, id: {Id}",
                decision.Approved, request.ToolName, id);

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
    }

    private ApprovalRequestCollector? ResolveCollector()
    {
        var context = _httpContextAccessor.HttpContext;
        return context?.RequestServices.GetService<ApprovalRequestCollector>();
    }

    private static string SerializeArguments(ToolApprovalRequest request)
    {
        if (request.Arguments is not { Count: > 0 })
        {
            return "{}";
        }

        try
        {
            return System.Text.Json.JsonSerializer.Serialize(request.Arguments);
        }
        catch
        {
            return "{}";
        }
    }
}
