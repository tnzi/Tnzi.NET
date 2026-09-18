using Microsoft.AspNetCore.Http;
using Tnzi.AI.Mcp.Server;
using McpServerOptions = Tnzi.AI.Mcp.Options.McpServerOptions;

namespace Tnzi.AI.Tests.Mcp;

/// <summary>
/// 工具调用的限流桶按<b>调用方</b>分区。
/// </summary>
/// <remarks>
/// ★ 此前每次 <c>tools/call</c> 消耗两个桶，其中第二个的键是 <c>agent:{agentId}</c> /
/// <c>tool:{name}</c> —— 不带任何调用方维度，而它与 HTTP 请求桶共用同一个
/// <c>McpServerSecurityMiddleware</c> 单例、同一个字典、同一个 <c>RateLimitPerMinute</c> 阈值。
/// 于是客户端 A 打满某个 agent 的桶之后，客户端 B（哪怕来自另一个租户、拿着另一把 API Key）
/// 对同一个 agent 的调用一律 429：一个普通客户端就能拒绝掉所有人对某个 agent 的访问。
/// </remarks>
public class McpToolRateLimitPartitionTests
{
    private const int Limit = 2;

    private readonly Mock<IAgentService> _agentService = new();
    private readonly IServiceProvider _serviceProvider;
    private readonly McpServerSecurityMiddleware _security;
    private readonly HttpContextAccessorStub _accessor = new();
    private readonly McpServerHost _host;

    public McpToolRateLimitPartitionTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => _agentService.Object);
        _serviceProvider = services.BuildServiceProvider();

        // 一个 security 单例 = 一个共享的限流字典，与生产一致。
        _security = new McpServerSecurityMiddleware(
            new StaticOptionsMonitor<McpServerOptions>(new McpServerOptions { RateLimitPerMinute = Limit }),
            NullLogger<McpServerSecurityMiddleware>.Instance,
            new ServiceCollection().BuildServiceProvider());

        _host = new McpServerHost(
            _serviceProvider,
            new StaticOptionsMonitor<McpServerOptions>(new McpServerOptions { Enabled = true, RequireAuthentication = false }),
            NullLogger<McpServerHost>.Instance,
            _security,
            _accessor);
    }

    private void ExposeCustomTool(string name)
        => _host.ExposeTool(name, "probe", _ => Task.FromResult("ok"));

    private void ActAs(string callerHash, string? tenant = null)
    {
        var context = new DefaultHttpContext();
        context.Items[McpServerSecurityMiddleware.CallerHashItemKey] = callerHash;
        if (tenant is not null)
        {
            // 客户端自报的租户头：既不进 Items 也不进限流键，这里放进去只为证明它被无视
            context.Items["X-Tenant-Id"] = tenant;
        }
        _accessor.HttpContext = context;
    }

    private async Task<bool> CallAsync(string tool)
    {
        var result = await _host.CallToolAsync(tool, null);
        // 限流命中时守卫抛 RateLimitException，被统一映射成 IsError 的文本结果。
        return result.IsError != true;
    }

    [Fact]
    public async Task OneClientExhaustingATool_DoesNotBlockAnother()
    {
        ExposeCustomTool("probe");

        ActAs("clientA");
        (await CallAsync("probe")).ShouldBeTrue();
        (await CallAsync("probe")).ShouldBeTrue();
        // A 已到上限
        (await CallAsync("probe")).ShouldBeFalse();

        // B 是另一个调用方，必须不受影响。
        ActAs("clientB");
        (await CallAsync("probe")).ShouldBeTrue();
        (await CallAsync("probe")).ShouldBeTrue();
    }

    [Fact]
    public async Task ToolBucket_IgnoresTenantItem()
    {
        // 同一个调用方换一个租户头不能换来一个新桶：工具桶与 HTTP 桶一样只按调用方分区
        ExposeCustomTool("probe");

        ActAs("sameHash", tenant: "tenant-a");
        (await CallAsync("probe")).ShouldBeTrue();
        (await CallAsync("probe")).ShouldBeTrue();
        (await CallAsync("probe")).ShouldBeFalse();

        ActAs("sameHash", tenant: "tenant-b");
        (await CallAsync("probe")).ShouldBeFalse();
    }

    [Fact]
    public async Task SameClient_StillHitsItsOwnLimit()
    {
        // 对照组：分区不能把限流本身取消掉。
        ExposeCustomTool("probe");
        ActAs("clientA");

        (await CallAsync("probe")).ShouldBeTrue();
        (await CallAsync("probe")).ShouldBeTrue();
        (await CallAsync("probe")).ShouldBeFalse();
    }

    [Fact]
    public async Task DifferentTools_HaveTheirOwnBuckets_ForTheSameClient()
    {
        ExposeCustomTool("probe");
        ExposeCustomTool("other");
        ActAs("clientA");

        (await CallAsync("probe")).ShouldBeTrue();
        (await CallAsync("probe")).ShouldBeTrue();
        (await CallAsync("probe")).ShouldBeFalse();

        (await CallAsync("other")).ShouldBeTrue();
    }

    private sealed class HttpContextAccessorStub : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }
}
