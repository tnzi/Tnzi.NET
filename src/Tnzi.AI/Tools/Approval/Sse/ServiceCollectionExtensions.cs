using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tnzi.AI.Tools.Approval;

namespace Tnzi.AI.Tools.Approval.Sse;

/// <summary>
/// DI convenience extensions for the SSE (Server-Sent Events) tool approval flow.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SSE-based tool approval handler. Replaces the default <c>AutoApprovalHandler</c>
    /// so that any tool-call approval requests are emitted to a scoped <see cref="ApprovalRequestCollector"/>
    /// (which a consumer wires to an HTTP SSE stream) and wait for an explicit decision before resolving.
    /// </summary>
    /// <remarks>
    /// This method also ensures <see cref="IHttpContextAccessor"/> is registered, since the handler
    /// uses it to resolve the per-request <see cref="ApprovalRequestCollector"/> from the active HTTP scope.
    /// The rest of the SSE approval machinery (<see cref="IPendingApprovalStore"/>, the store implementation,
    /// the handler itself, and the collector) is auto-registered via the framework's
    /// <c>IScopedDependency</c> / <c>ISingletonDependency</c> marker interfaces.
    /// </remarks>
    public static IServiceCollection AddAIToolApprovalSse(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();

        // Replace any earlier registered IToolApprovalHandler (e.g. AutoApprovalHandler via TryAdd)
        // so the SSE handler takes precedence.
        services.RemoveAll<IToolApprovalHandler>();
        services.AddSingleton<IToolApprovalHandler, SseToolApprovalHandler>();

        return services;
    }
}
