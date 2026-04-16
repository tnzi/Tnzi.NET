using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tnzi.AI.Tools.Approval;
using Tnzi.AI.Tools.Approval.Sse;

namespace Tnzi.AI.Tests.Tools;

/// <summary>
/// Verifies that <see cref="SseToolApprovalHandler"/> is registered as Singleton and does not
/// cause a captive-dependency violation when <see cref="ApprovalRequestCollector"/> (Scoped)
/// is also in the container — the handler must NOT directly inject the collector.
/// </summary>
public class SseToolApprovalHandlerLifetimeTests
{
    [Fact]
    public void SseToolApprovalHandler_RegisteredAsSingleton_WithScopedCollector_NoCaptiveDependencyException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();

        // The default (auto-approve) handler — matches AIModule registration
        services.AddSingleton<IToolApprovalHandler, AutoApprovalHandler>();

        // SSE-specific registrations
        services.AddSingleton<InMemoryPendingApprovalStore>();
        services.AddScoped<ApprovalRequestCollector>();

        // Override with SSE handler (simulates what Fabrikam does)
        services.AddSingleton<SseToolApprovalHandler>();

        // Act — BuildServiceProvider with validateScopes:true throws if a Singleton
        // captures a Scoped dependency directly.
        var act = () =>
        {
            var provider = services.BuildServiceProvider(validateScopes: true);
            // Resolve from root scope (simulates Singleton resolution path)
            _ = provider.GetRequiredService<SseToolApprovalHandler>();
        };

        // Assert — must not throw InvalidOperationException about captive dependency
        act.ShouldNotThrow();
    }

    [Fact]
    public void SseToolApprovalHandler_IsAssignableFrom_IToolApprovalHandler()
    {
        typeof(SseToolApprovalHandler)
            .IsAssignableTo(typeof(IToolApprovalHandler))
            .ShouldBeTrue();
    }
}
