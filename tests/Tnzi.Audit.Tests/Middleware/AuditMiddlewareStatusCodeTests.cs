using Tnzi.Exceptions;

namespace Tnzi.Audit.Tests.Middleware;

/// <summary>
/// 审计行的 <see cref="AuditOperation.HttpStatusCode"/> 必须是客户端实际收到的码。
/// </summary>
/// <remarks>
/// 审计中间件挂在异常处理中间件<b>之内</b>：异常穿过它时响应还没写，<c>Response.StatusCode</c> 仍是默认 200。
/// 直接读它，服务层抛的 <see cref="ForbiddenException"/> 就会以「失败 + 200」入表，按 403 筛越权尝试的查询一条都找不到。
/// </remarks>
public class AuditMiddlewareStatusCodeTests
{
    private sealed class CapturingAuditSender : IAuditSender
    {
        public List<AuditOperation> Captured { get; } = [];

        public Task SendAsync(AuditOperation operation)
        {
            Captured.Add(operation);
            return Task.CompletedTask;
        }
    }

    private static async Task<AuditOperation> CaptureAsync(RequestDelegate next, Action<HttpContext>? arrange = null)
    {
        var sender = new CapturingAuditSender();
        var middleware = new AuditMiddleware(
            next,
            NullLogger<AuditMiddleware>.Instance,
            sender,
            new Tnzi.Audit.Tests.TestSupport.StaticOptionsMonitor<AuditOptions>(new AuditOptions()),
            new RequestBodyRedactor());

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/orders";
        arrange?.Invoke(context);

        try
        {
            await middleware.InvokeAsync(context, new Mock<ICurrentUser>().Object, new EntityAuditCollector());
        }
        catch (Exception)
        {
            // 中间件记录后原样重抛；这里只关心它记了什么。
        }

        return sender.Captured.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ForbiddenException_IsRecordedAs403()
    {
        var operation = await CaptureAsync(_ => throw new ForbiddenException());

        operation.ResultType.ShouldBe(AuditResultType.Failed);
        operation.HttpStatusCode.ShouldBe(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task ResourceNotFoundException_IsRecordedAs404()
    {
        var operation = await CaptureAsync(_ => throw new ResourceNotFoundException("Order"));

        operation.HttpStatusCode.ShouldBe(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task UnhandledException_IsRecordedAs500()
    {
        var operation = await CaptureAsync(_ => throw new InvalidOperationException("boom"));

        operation.HttpStatusCode.ShouldBe(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task ClientDisconnect_IsRecordedAs499_NotAsAServerError()
    {
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();

        var operation = await CaptureAsync(_ => throw new OperationCanceledException(aborted.Token), ctx => ctx.RequestAborted = aborted.Token);

        operation.HttpStatusCode.ShouldBe(StatusCodes.Status499ClientClosedRequest);
    }

    [Fact]
    public async Task WithoutException_TheWrittenStatusCodeIsKept()
    {
        var operation = await CaptureAsync(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status409Conflict;
            return Task.CompletedTask;
        });

        operation.ResultType.ShouldBe(AuditResultType.Failed);
        operation.HttpStatusCode.ShouldBe(StatusCodes.Status409Conflict);
    }
}
