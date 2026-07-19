namespace Tnzi.Audit.Tests.Middleware;

/// <summary>
/// AuditMiddleware 请求门（AuditOperationGate）行为测试：
/// 总开关关闭 / 命中排除路径 / [AuditDisabled] 端点时不入队审计,但请求正常放行。
/// </summary>
public class AuditMiddlewareGateTests
{
    private sealed class StaticOptionsMonitor(AuditOptions value) : IOptionsMonitor<AuditOptions>
    {
        public AuditOptions CurrentValue => value;
        public AuditOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<AuditOptions, string?> listener) => null;
    }

    private sealed class CapturingAuditSender : IAuditSender
    {
        public List<AuditOperation> Captured { get; } = [];

        public Task SendAsync(AuditOperation operation)
        {
            Captured.Add(operation);
            return Task.CompletedTask;
        }
    }

    private static async Task<(CapturingAuditSender Sender, bool NextInvoked)> RunAsync(
        DefaultHttpContext context, AuditOptions? options = null)
    {
        var sender = new CapturingAuditSender();
        var nextInvoked = false;
        var middleware = new AuditMiddleware(
            _ => { nextInvoked = true; return Task.CompletedTask; },
            NullLogger<AuditMiddleware>.Instance,
            sender,
            new StaticOptionsMonitor(options ?? new AuditOptions()),
            new RequestBodyRedactor());

        await middleware.InvokeAsync(context, new Mock<ICurrentUser>().Object, new EntityAuditCollector());
        return (sender, nextInvoked);
    }

    private static DefaultHttpContext CreateHttpContext(string path = "/test/resource")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "PUT";
        context.Request.Path = path;
        return context;
    }

    [Fact]
    public async Task OperationAuditDisabled_SkipsAudit_ButInvokesNext()
    {
        var (sender, nextInvoked) = await RunAsync(
            CreateHttpContext(), new AuditOptions { EnableOperationAudit = false });

        nextInvoked.ShouldBeTrue();
        sender.Captured.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExcludedPath_SkipsAudit_ButInvokesNext()
    {
        var (sender, nextInvoked) = await RunAsync(CreateHttpContext("/hubs/chat"));

        nextInvoked.ShouldBeTrue();
        sender.Captured.ShouldBeEmpty();
    }

    [Fact]
    public async Task AuditDisabledEndpoint_SkipsAudit_ButInvokesNext()
    {
        var context = CreateHttpContext();
        context.SetEndpoint(new Endpoint(
            null, new EndpointMetadataCollection(new AuditDisabledAttribute()), "audit-disabled-endpoint"));

        var (sender, nextInvoked) = await RunAsync(context);

        nextInvoked.ShouldBeTrue();
        sender.Captured.ShouldBeEmpty();
    }

    [Fact]
    public async Task NormalRequest_PassesGate_AndEnqueuesAudit()
    {
        var (sender, nextInvoked) = await RunAsync(CreateHttpContext());

        nextInvoked.ShouldBeTrue();
        sender.Captured.ShouldHaveSingleItem();
    }
}
