using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Tnzi.AI.Mcp.Options;
using Tnzi.AI.Mcp.Server;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Mcp;

public class McpServerSecurityMiddlewareTests
{
    // ─── ExtractApiKey — source priority ─────────────────────────────────────

    [Fact]
    public void ExtractApiKey_XApiKeyHeader_WinsOverAuthorizationBearer()
    {
        var middleware = CreateMiddleware(new McpServerOptions());
        var context = new DefaultHttpContext();
        context.Request.Headers[McpServerSecurityMiddleware.ApiKeyHeaderName] = "header-key";
        context.Request.Headers.Authorization = "Bearer bearer-key";

        middleware.ExtractApiKey(context.Request).ShouldBe("header-key");
    }

    [Fact]
    public void ExtractApiKey_AuthorizationBearer_UsedWhenNoApiKeyHeader()
    {
        var middleware = CreateMiddleware(new McpServerOptions());
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer  bearer-key  ";

        // Bearer 前缀大小写不敏感且会 Trim
        middleware.ExtractApiKey(context.Request).ShouldBe("bearer-key");
    }

    [Fact]
    public void ExtractApiKey_AuthorizationBearer_CaseInsensitivePrefix()
    {
        var middleware = CreateMiddleware(new McpServerOptions());
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "bearer lower-key";

        middleware.ExtractApiKey(context.Request).ShouldBe("lower-key");
    }

    [Fact]
    public void ExtractApiKey_NonBearerAuthorization_Ignored()
    {
        var middleware = CreateMiddleware(new McpServerOptions());
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Basic dXNlcjpwYXNz";

        middleware.ExtractApiKey(context.Request).ShouldBeNull();
    }

    [Fact]
    public void ExtractApiKey_WhitespaceHeader_FallsThroughToBearer()
    {
        var middleware = CreateMiddleware(new McpServerOptions());
        var context = new DefaultHttpContext();
        context.Request.Headers[McpServerSecurityMiddleware.ApiKeyHeaderName] = "   ";
        context.Request.Headers.Authorization = "Bearer bearer-key";

        middleware.ExtractApiKey(context.Request).ShouldBe("bearer-key");
    }

    [Fact]
    public void ExtractApiKey_QueryString_IgnoredByDefault()
    {
        // AllowApiKeyInQuery 默认 false — query 凭据会泄漏到日志/代理，默认必须拒收
        var options = new McpServerOptions();
        options.AllowApiKeyInQuery.ShouldBeFalse();

        var middleware = CreateMiddleware(options);
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?apiKey=query-key");

        middleware.ExtractApiKey(context.Request).ShouldBeNull();
    }

    [Fact]
    public void ExtractApiKey_QueryString_UsedWhenOptedIn()
    {
        var middleware = CreateMiddleware(new McpServerOptions { AllowApiKeyInQuery = true });
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?apiKey=query-key");

        middleware.ExtractApiKey(context.Request).ShouldBe("query-key");
    }

    [Fact]
    public void ExtractApiKey_LegacyQueryParameter_UsedWhenOptedIn()
    {
        var middleware = CreateMiddleware(new McpServerOptions { AllowApiKeyInQuery = true });
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?apikey=legacy-key");

        middleware.ExtractApiKey(context.Request).ShouldBe("legacy-key");
    }

    [Fact]
    public void ExtractApiKey_HeaderWinsOverQuery_EvenWhenOptedIn()
    {
        var middleware = CreateMiddleware(new McpServerOptions { AllowApiKeyInQuery = true });
        var context = new DefaultHttpContext();
        context.Request.Headers[McpServerSecurityMiddleware.ApiKeyHeaderName] = "header-key";
        context.Request.QueryString = new QueryString("?apiKey=query-key");

        middleware.ExtractApiKey(context.Request).ShouldBe("header-key");
    }

    // ─── ExtractTenantId — header-only untrusted hint ────────────────────────

    [Fact]
    public void ExtractTenantId_Header_Returned()
    {
        var middleware = CreateMiddleware(new McpServerOptions());
        var context = new DefaultHttpContext();
        context.Request.Headers[McpServerSecurityMiddleware.TenantHeaderName] = "tenant-a";

        middleware.ExtractTenantId(context.Request).ShouldBe("tenant-a");
    }

    [Fact]
    public void ExtractTenantId_QueryTenantId_Ignored()
    {
        // 回归：query string 租户提取已删除 — 构造 URL 即可污染他租户限流分区的攻击面
        var middleware = CreateMiddleware(new McpServerOptions());
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?tenantId=spoofed");

        middleware.ExtractTenantId(context.Request).ShouldBeNull();
    }

    [Fact]
    public void ExtractTenantId_LegacyQueryTenant_Ignored()
    {
        var middleware = CreateMiddleware(new McpServerOptions());
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?tenant=spoofed");

        middleware.ExtractTenantId(context.Request).ShouldBeNull();
    }

    [Fact]
    public void ExtractTenantId_WhitespaceHeader_ReturnsNull()
    {
        var middleware = CreateMiddleware(new McpServerOptions());
        var context = new DefaultHttpContext();
        context.Request.Headers[McpServerSecurityMiddleware.TenantHeaderName] = "  ";

        middleware.ExtractTenantId(context.Request).ShouldBeNull();
    }

    // ─── ValidateApiKey ──────────────────────────────────────────────────────

    [Fact]
    public void ValidateApiKey_AuthDisabled_AlwaysPasses()
    {
        var middleware = CreateMiddleware(new McpServerOptions { RequireAuthentication = false });

        middleware.ValidateApiKey(null).ShouldBeTrue();
        middleware.ValidateApiKey("anything").ShouldBeTrue();
    }

    [Fact]
    public void ValidateApiKey_MissingKey_Fails()
    {
        var middleware = CreateMiddleware(new McpServerOptions
        {
            RequireAuthentication = true,
            AllowedApiKeys = ["secret"]
        });

        middleware.ValidateApiKey(null).ShouldBeFalse();
        middleware.ValidateApiKey("  ").ShouldBeFalse();
    }

    [Fact]
    public void ValidateApiKey_NoConfiguredKeys_AlwaysFails()
    {
        var middleware = CreateMiddleware(new McpServerOptions
        {
            RequireAuthentication = true,
            AllowedApiKeys = []
        });

        middleware.ValidateApiKey("secret").ShouldBeFalse();
    }

    [Fact]
    public void ValidateApiKey_OrdinalComparison()
    {
        var middleware = CreateMiddleware(new McpServerOptions
        {
            RequireAuthentication = true,
            AllowedApiKeys = ["Secret"]
        });

        middleware.ValidateApiKey("Secret").ShouldBeTrue();
        // API key 比较必须区分大小写（Ordinal）
        middleware.ValidateApiKey("secret").ShouldBeFalse();
    }

    // ─── BuildClientKey — partition logic ────────────────────────────────────

    [Fact]
    public void BuildClientKey_PerTenantOn_HeaderTenant_PartitionsByTenant()
    {
        var middleware = CreateMiddleware(new McpServerOptions { RateLimitPerTenant = true });
        var context = new DefaultHttpContext();
        context.Request.Headers[McpServerSecurityMiddleware.TenantHeaderName] = "tenant-a";

        var key = middleware.BuildClientKey(context, "secret");

        key.ShouldBe($"tenant-a:{ExpectedHash("secret")}");
    }

    [Fact]
    public void BuildClientKey_PerTenantOn_NoTenant_UsesPublicSegment()
    {
        var middleware = CreateMiddleware(new McpServerOptions { RateLimitPerTenant = true });
        var context = new DefaultHttpContext();

        var key = middleware.BuildClientKey(context, "secret");

        key.ShouldBe($"public:{ExpectedHash("secret")}");
    }

    [Fact]
    public void BuildClientKey_PerTenantOn_QueryTenant_DoesNotPartition()
    {
        // 回归：query 租户已不可污染限流分区
        var middleware = CreateMiddleware(new McpServerOptions { RateLimitPerTenant = true });
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?tenantId=spoofed");

        var key = middleware.BuildClientKey(context, "secret");

        key.ShouldStartWith("public:");
    }

    [Fact]
    public void BuildClientKey_PerTenantOff_UsesSharedSegment()
    {
        var middleware = CreateMiddleware(new McpServerOptions { RateLimitPerTenant = false });
        var context = new DefaultHttpContext();
        context.Request.Headers[McpServerSecurityMiddleware.TenantHeaderName] = "tenant-a";

        var key = middleware.BuildClientKey(context, "secret");

        key.ShouldBe($"shared:{ExpectedHash("secret")}");
    }

    [Fact]
    public void BuildClientKey_NoApiKey_FallsBackToRemoteIp()
    {
        var middleware = CreateMiddleware(new McpServerOptions { RateLimitPerTenant = false });
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        middleware.BuildClientKey(context, apiKey: null).ShouldBe("shared:203.0.113.7");
    }

    [Fact]
    public void BuildClientKey_NoApiKeyNoIp_FallsBackToAnonymous()
    {
        var middleware = CreateMiddleware(new McpServerOptions { RateLimitPerTenant = false });
        var context = new DefaultHttpContext();

        middleware.BuildClientKey(context, apiKey: null).ShouldBe("shared:anonymous");
    }

    [Fact]
    public void BuildClientKey_NeverContainsRawApiKey()
    {
        var middleware = CreateMiddleware(new McpServerOptions());
        var context = new DefaultHttpContext();

        var key = middleware.BuildClientKey(context, "super-secret-raw-key");

        key.ShouldNotContain("super-secret-raw-key");
    }

    // ─── CheckRateLimit — sliding window ─────────────────────────────────────

    [Fact]
    public void CheckRateLimit_UnderLimit_Allows_OverLimit_Rejects()
    {
        var middleware = CreateMiddleware(new McpServerOptions { RateLimitPerMinute = 5 });

        for (var i = 0; i < 5; i++)
        {
            middleware.CheckRateLimit("client-x").ShouldBeTrue();
        }

        middleware.CheckRateLimit("client-x").ShouldBeFalse();
    }

    [Fact]
    public void CheckRateLimit_ZeroLimit_DisablesRateLimiting()
    {
        var middleware = CreateMiddleware(new McpServerOptions { RateLimitPerMinute = 0 });

        for (var i = 0; i < 100; i++)
        {
            middleware.CheckRateLimit("client-x").ShouldBeTrue();
        }
    }

    [Fact]
    public void CheckRateLimit_DistinctClients_HaveIndependentWindows()
    {
        var middleware = CreateMiddleware(new McpServerOptions { RateLimitPerMinute = 1 });

        middleware.CheckRateLimit("client-a").ShouldBeTrue();
        middleware.CheckRateLimit("client-a").ShouldBeFalse();
        middleware.CheckRateLimit("client-b").ShouldBeTrue();
    }

    [Fact]
    public async Task CheckRateLimit_ConcurrentCalls_AllowExactlyLimit()
    {
        // 滑动窗口计数器内部加锁 — 并发下不允许超发
        const int limit = 50;
        const int attempts = 200;
        var middleware = CreateMiddleware(new McpServerOptions { RateLimitPerMinute = limit });

        var successCount = 0;
        var tasks = Enumerable.Range(0, attempts)
            .Select(_ => Task.Run(() =>
            {
                if (middleware.CheckRateLimit("client-concurrent"))
                {
                    Interlocked.Increment(ref successCount);
                }
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        successCount.ShouldBe(limit);
    }

    [Fact]
    public void CheckRateLimit_TrackingTableFull_RejectsNewClients_AllowsExisting()
    {
        var middleware = CreateMiddleware(new McpServerOptions
        {
            RateLimitPerMinute = 5,
            RateLimitTrackingMaxEntries = 2
        });

        middleware.CheckRateLimit("client-a").ShouldBeTrue();
        middleware.CheckRateLimit("client-b").ShouldBeTrue();

        // 表满且无可驱逐的过期条目 → 新 client 被防御性拒绝
        middleware.CheckRateLimit("client-c").ShouldBeFalse();

        // 已跟踪的 client 不受影响
        middleware.CheckRateLimit("client-a").ShouldBeTrue();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static McpServerSecurityMiddleware CreateMiddleware(McpServerOptions options)
        => new(
            new StaticOptionsMonitor<McpServerOptions>(options),
            NullLogger<McpServerSecurityMiddleware>.Instance,
            new ServiceCollection().BuildServiceProvider());

    private static string ExpectedHash(string apiKey)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)))[..16];
}
