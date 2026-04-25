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
        const string userId = "test-user";
        var (provider, scope, store, handler, collector) = BuildSse();

        try
        {
            var request = new ToolApprovalRequest
            {
                ToolName = "create_contract",
                Arguments = new() { ["amount"] = 100 },
                Context = new() { ["userId"] = userId }
            };

            var approvalTask = handler.RequestApprovalAsync(request);

            PendingApprovalRequest emitted;
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            {
                emitted = await collector.Reader.ReadAsync(cts.Token);
            }

            emitted.ToolName.ShouldBe("create_contract");
            emitted.UserId.ShouldBe(userId);

            var resolveResult = await store.ResolveAsync(
                emitted.Id,
                new ApprovalDecision(Approved: true, Reason: "OK", DecidedBy: userId),
                currentUserId: userId);

            resolveResult.ShouldBe(ResolveResult.Resolved);

            var result = await approvalTask.WaitAsync(TimeSpan.FromSeconds(5));
            result.Approved.ShouldBeTrue();
        }
        finally
        {
            scope.Dispose();
            provider.Dispose();
        }
    }

    private static (ServiceProvider provider, IServiceScope scope, InMemoryPendingApprovalStore store,
                    IToolApprovalHandler handler, ApprovalRequestCollector collector) BuildSse()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddLogging();

        // The extension method registers store / collector / handler / IHttpContextAccessor.
        services.AddAIToolApprovalSse();

        var provider = services.BuildServiceProvider(validateScopes: true);
        var scope = provider.CreateScope();
        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = httpContext;

        return (
            provider,
            scope,
            provider.GetRequiredService<InMemoryPendingApprovalStore>(),
            provider.GetRequiredService<IToolApprovalHandler>(),
            scope.ServiceProvider.GetRequiredService<ApprovalRequestCollector>()
        );
    }
}
