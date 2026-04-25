using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tnzi.AI.Tools.Approval.Sse;

/// <summary>
/// DI convenience extensions for the SSE (Server-Sent Events) tool approval flow.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the full SSE-based tool approval pipeline. Replaces the default
    /// <c>AutoApprovalHandler</c> with <see cref="SseToolApprovalHandler"/>; emits pending requests
    /// to a per-request <see cref="ApprovalRequestCollector"/> (which a consumer wires to an HTTP
    /// SSE stream) and waits for an explicit decision via <see cref="IPendingApprovalStore"/>.
    /// </summary>
    /// <remarks>
    /// All registrations are explicit per framework rule #1 (no marker-interface auto-registration
    /// in framework assemblies). The registered components are:
    /// <list type="bullet">
    ///   <item><see cref="IHttpContextAccessor"/> (Singleton) — for resolving per-call request scope</item>
    ///   <item><see cref="InMemoryPendingApprovalStore"/> (Singleton) + <see cref="IPendingApprovalStore"/> alias</item>
    ///   <item><see cref="ApprovalRequestCollector"/> (Scoped) — per-request channel</item>
    ///   <item><see cref="IToolApprovalHandler"/> (Singleton) — replaces any prior registration</item>
    /// </list>
    /// </remarks>
    public static IServiceCollection AddAIToolApprovalSse(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();

        services.TryAddSingleton<InMemoryPendingApprovalStore>();
        services.TryAddSingleton<IPendingApprovalStore>(sp => sp.GetRequiredService<InMemoryPendingApprovalStore>());
        services.AddScoped<ApprovalRequestCollector>();

        // Replace any earlier registered IToolApprovalHandler (e.g. AutoApprovalHandler via TryAdd)
        // so the SSE handler takes precedence.
        services.RemoveAll<IToolApprovalHandler>();
        services.AddSingleton<IToolApprovalHandler, SseToolApprovalHandler>();

        return services;
    }
}
