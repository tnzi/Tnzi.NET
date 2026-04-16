using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Tnzi.AI.Tools.Approval;
using Tnzi.AI.Tools.Approval.Sse;
using Xunit;

namespace Tnzi.AI.Tests.Tools;

/// <summary>
/// Verifies the full request → emit → resolve → return flow of <see cref="SseToolApprovalHandler"/>
/// using the <see cref="ServiceCollectionExtensions.AddAIToolApprovalSse"/> extension method.
/// </summary>
public class SseToolApprovalHandlerFunctionalTests
{
    [Fact]
    public async Task RequestApproval_EmitsToCollector_ThenResolvesWithApprovedDecision()
    {
        // Arrange: build DI using AddAIToolApprovalSse (the extension under test)
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddLogging();

        // Register SSE-specific singletons/scoped services manually
        // (in production the framework auto-scanner picks these up via ISingletonDependency / IScopedDependency)
        services.AddSingleton<InMemoryPendingApprovalStore>();
        services.AddSingleton<IPendingApprovalStore>(sp => sp.GetRequiredService<InMemoryPendingApprovalStore>());
        services.AddScoped<ApprovalRequestCollector>();

        // The extension method under test — registers IHttpContextAccessor + IToolApprovalHandler
        services.AddAIToolApprovalSse();

        var provider = services.BuildServiceProvider(validateScopes: true);

        // Create a request scope and wire it as the ambient HttpContext so the handler
        // can resolve ApprovalRequestCollector from HttpContext.RequestServices
        using var scope = provider.CreateScope();
        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = httpContext;

        var collector = scope.ServiceProvider.GetRequiredService<ApprovalRequestCollector>();
        var store = provider.GetRequiredService<InMemoryPendingApprovalStore>();
        var handler = provider.GetRequiredService<IToolApprovalHandler>();

        var request = new ToolApprovalRequest
        {
            ToolName = "create_contract",
            Arguments = new() { ["amount"] = 100 },
            Context = new() { ["userId"] = "test-user" }
        };

        // Act: kick off approval request — will block until a decision is resolved
        var approvalTask = handler.RequestApprovalAsync(request);

        // Wait for the handler to emit the pending request to the collector channel
        PendingApprovalRequest emitted;
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            emitted = await collector.Reader.ReadAsync(cts.Token);
        }

        emitted.ToolName.ShouldBe("create_contract");

        // Resolve with Approved=true via the store
        await store.ResolveAsync(emitted.Id, new ApprovalDecision(Approved: true, Reason: "OK", DecidedBy: "test-user"));

        // Assert: the approval task completes with Approved=true
        var result = await approvalTask.WaitAsync(TimeSpan.FromSeconds(5));
        result.Approved.ShouldBeTrue();
    }
}
