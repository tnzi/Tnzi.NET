using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.AI.Mcp.Server;
using Tnzi.AI.Mcp.Options;
using Tnzi.AI.Mcp.Services.Interfaces;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Mcp;

/// <summary>
/// C4: AuditLogAsync must record the hashed caller key so UniqueCallers reflects
/// distinct callers rather than always counting null as a single "caller".
/// </summary>
public class McpToolAnalyticsCallerHashTests
{
    private static McpServerSecurityMiddleware BuildSecurity(IServiceProvider sp, McpServerOptions? opts = null)
    {
        opts ??= new McpServerOptions
        {
            Enabled = true,
            RequireAuthentication = false,
            EnableAuditLog = true
        };
        return new McpServerSecurityMiddleware(
            MsOptions.Create(opts),
            NullLogger<McpServerSecurityMiddleware>.Instance,
            sp);
    }

    [Fact]
    public async Task AuditLogAsync_WithCallerApiKeyId_PassesHashToAnalyticsService()
    {
        var capturedCallerIds = new List<string?>();
        var analyticsService = new Mock<IMcpToolAnalyticsService>();
        analyticsService
            .Setup(x => x.RecordUsageAsync(
                It.IsAny<string>(), It.IsAny<long>(), It.IsAny<bool>(),
                It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<string, long, bool, string?, string?>((_, _, _, _, callerId) => capturedCallerIds.Add(callerId))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(analyticsService.Object);
        using var sp = services.BuildServiceProvider();

        var security = BuildSecurity(sp);

        const string callerHash1 = "abc123hash01";
        const string callerHash2 = "xyz789hash02";

        await security.AuditLogAsync("tool-a", agentId: null, 10, isSuccess: true, callerApiKeyId: callerHash1);
        await security.AuditLogAsync("tool-a", agentId: null, 12, isSuccess: true, callerApiKeyId: callerHash2);

        analyticsService.Verify(
            x => x.RecordUsageAsync("tool-a", It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Exactly(2));

        capturedCallerIds.ShouldContain(callerHash1);
        capturedCallerIds.ShouldContain(callerHash2);
        capturedCallerIds.Distinct().Count().ShouldBe(2, "two distinct caller hashes must be recorded");
    }

    [Fact]
    public void BuildClientKey_ExtractsHashedCallerSegment_NotRawKey()
    {
        const string rawKey = "super-secret-api-key-12345";
        var services = new ServiceCollection();
        using var sp = services.BuildServiceProvider();

        var security = BuildSecurity(sp, new McpServerOptions
        {
            Enabled = true,
            RequireAuthentication = true,
            AllowedApiKeys = [rawKey]
        });

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = rawKey;
        context.Connection.RemoteIpAddress = null;

        var clientKey = security.BuildClientKey(context, rawKey);

        clientKey.Contains(rawKey).ShouldBeFalse("raw key must not appear in client key");
        // The hash segment is 16 hex chars (8 bytes of SHA-256)
        var parts = clientKey.Split(':');
        parts.Length.ShouldBe(2);
        parts[1].Length.ShouldBe(16);
        parts[1].All(c => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F')).ShouldBeTrue("hash segment must be uppercase hex");
    }

    /// <summary>
    /// P1 decoupling: per-tool analytics must keep recording even when the audit log
    /// (IUsageLogService) is disabled — the two are gated independently.
    /// </summary>
    [Fact]
    public async Task AuditLogAsync_AuditDisabledAnalyticsEnabled_StillRecordsAnalytics()
    {
        var analyticsService = new Mock<IMcpToolAnalyticsService>();
        analyticsService
            .Setup(x => x.RecordUsageAsync(
                It.IsAny<string>(), It.IsAny<long>(), It.IsAny<bool>(),
                It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        var usageLogService = new Mock<IUsageLogService>();

        var services = new ServiceCollection();
        services.AddSingleton(analyticsService.Object);
        services.AddSingleton(usageLogService.Object);
        using var sp = services.BuildServiceProvider();

        var security = BuildSecurity(sp, new McpServerOptions
        {
            Enabled = true,
            RequireAuthentication = false,
            EnableAuditLog = false,     // audit OFF
            EnableToolAnalytics = true  // analytics ON
        });

        await security.AuditLogAsync("tool-a", agentId: null, 10, isSuccess: true, callerApiKeyId: "hash01");

        // Analytics recorded, but audit log NOT written
        analyticsService.Verify(
            x => x.RecordUsageAsync("tool-a", 10, true, null, "hash01"),
            Times.Once);
        usageLogService.Verify(
            x => x.LogUsageAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<long>(), It.IsAny<bool>(),
                It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// P1 decoupling: disabling analytics while keeping audit on must skip analytics only.
    /// </summary>
    [Fact]
    public async Task AuditLogAsync_AnalyticsDisabledAuditEnabled_SkipsAnalyticsOnly()
    {
        var analyticsService = new Mock<IMcpToolAnalyticsService>();
        var usageLogService = new Mock<IUsageLogService>();

        var services = new ServiceCollection();
        services.AddSingleton(analyticsService.Object);
        services.AddSingleton(usageLogService.Object);
        using var sp = services.BuildServiceProvider();

        var security = BuildSecurity(sp, new McpServerOptions
        {
            Enabled = true,
            RequireAuthentication = false,
            EnableAuditLog = true,       // audit ON
            EnableToolAnalytics = false  // analytics OFF
        });

        await security.AuditLogAsync("tool-a", agentId: null, 10, isSuccess: true, callerApiKeyId: "hash01");

        analyticsService.Verify(
            x => x.RecordUsageAsync(
                It.IsAny<string>(), It.IsAny<long>(), It.IsAny<bool>(),
                It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never);
        usageLogService.Verify(
            x => x.LogUsageAsync(
                "McpToolCall", "mcp-server", "tool-a",
                0, 0, 10L, true, null, (Guid?)null, (Guid?)null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Both gates off: no scope work at all.
    /// </summary>
    [Fact]
    public async Task AuditLogAsync_BothDisabled_RecordsNothing()
    {
        var analyticsService = new Mock<IMcpToolAnalyticsService>();
        var usageLogService = new Mock<IUsageLogService>();

        var services = new ServiceCollection();
        services.AddSingleton(analyticsService.Object);
        services.AddSingleton(usageLogService.Object);
        using var sp = services.BuildServiceProvider();

        var security = BuildSecurity(sp, new McpServerOptions
        {
            Enabled = true,
            RequireAuthentication = false,
            EnableAuditLog = false,
            EnableToolAnalytics = false
        });

        await security.AuditLogAsync("tool-a", agentId: null, 10, isSuccess: true);

        analyticsService.Verify(
            x => x.RecordUsageAsync(
                It.IsAny<string>(), It.IsAny<long>(), It.IsAny<bool>(),
                It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never);
        usageLogService.Verify(
            x => x.LogUsageAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<long>(), It.IsAny<bool>(),
                It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// C4 end-to-end: McpServerHttpSecurityMiddleware stores the caller hash in context.Items
    /// so downstream code can retrieve it without touching the raw key.
    /// </summary>
    [Fact]
    public async Task HttpSecurityMiddleware_StoresCallerHash_InContextItems()
    {
        const string rawKey = "my-api-key-abcdef";
        var services = new ServiceCollection();
        using var sp = services.BuildServiceProvider();

        var options = new McpServerOptions
        {
            Enabled = true,
            RequireAuthentication = true,
            AllowedApiKeys = [rawKey],
            RateLimitPerMinute = 0
        };

        var security = BuildSecurity(sp, options);
        string? capturedHash = null;

        RequestDelegate next = ctx =>
        {
            capturedHash = ctx.Items[McpServerSecurityMiddleware.CallerHashItemKey] as string;
            return Task.CompletedTask;
        };

        var middleware = new McpServerHttpSecurityMiddleware(
            next,
            MsOptions.Create(options),
            security);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Headers["X-Api-Key"] = rawKey;

        await middleware.InvokeAsync(context);

        capturedHash.ShouldNotBeNull("caller hash must be stored in context.Items");
        capturedHash!.Contains(rawKey).ShouldBeFalse("raw key must not appear in stored hash");
        capturedHash.Length.ShouldBe(16);
    }
}
