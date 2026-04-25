using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Tnzi.AI.Tools.Approval;
using Tnzi.AI.Tools.Approval.Sse;
using Xunit;

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
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAIToolApprovalSse();

        var act = () =>
        {
            using var provider = services.BuildServiceProvider(validateScopes: true);
            _ = provider.GetRequiredService<IToolApprovalHandler>();
        };

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
