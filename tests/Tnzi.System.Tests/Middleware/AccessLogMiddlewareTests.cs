using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.Security.Claims;
using Tnzi.System.Middleware;

namespace Tnzi.System.Tests.Middleware;

/// <summary>
/// <see cref="AccessLogMiddleware"/>：<c>Sys_AccessLog</c> 的第一个生产者。
/// </summary>
/// <remarks>
/// <para>
/// ★ 此前这张表<b>没有任何生产者</b>：8 个查询端点、后台富化、管理页与仪表盘 KPI 全围着一张恒空的表，
/// 唯一的写入口是要 <c>system.accessLog.create</c> 的管理端手工 POST，而文档写着「自动记录 API 访问日志」。
/// 采集是 <b>opt-in</b>（`System:AccessLog:Enabled` 默认 false）：每个宿主凭空多一张高频写入表不是默认该有的。
/// </para>
/// <para>
/// 与 <c>AuditMiddlewareGateTests</c> 同形：直接构造中间件、直接调 <c>InvokeAsync</c>，与数据库无关。
/// </para>
/// </remarks>
public class AccessLogMiddlewareTests
{
    private sealed class StaticOptionsMonitor(AccessLogOptions value) : IOptionsMonitor<AccessLogOptions>
    {
        public AccessLogOptions CurrentValue => value;
        public AccessLogOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<AccessLogOptions, string?> listener) => null;
    }

    private sealed class CapturingSender : IAccessLogSender
    {
        public List<AccessLogDto> Captured { get; } = [];

        public Task SendAsync(AccessLogDto log)
        {
            Captured.Add(log);
            return Task.CompletedTask;
        }
    }

    private static AccessLogOptions Enabled() => new() { Enabled = true };

    private static DefaultHttpContext CreateHttpContext(string path = "/api/orders", string method = "GET")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.Headers.UserAgent = "UnitTest/1.0";
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        return context;
    }

    private static async Task<(CapturingSender Sender, bool NextInvoked)> RunAsync(
        DefaultHttpContext context,
        AccessLogOptions? options = null,
        Func<HttpContext, Task>? next = null,
        ICurrentUser? currentUser = null)
    {
        var sender = new CapturingSender();
        var nextInvoked = false;
        var middleware = new AccessLogMiddleware(
            async ctx =>
            {
                nextInvoked = true;
                if (next != null) await next(ctx);
            },
            sender,
            new StaticOptionsMonitor(options ?? new AccessLogOptions()),
            NullLogger<AccessLogMiddleware>.Instance);

        await middleware.InvokeAsync(context, currentUser ?? new Mock<ICurrentUser>().Object);
        return (sender, nextInvoked);
    }

    /// <summary>默认关闭：不采集，请求照常放行。</summary>
    [Fact]
    public async Task Disabled_ByDefault_EnqueuesNothing()
    {
        new AccessLogOptions().Enabled.ShouldBeFalse("access log capture must be opt-in");

        var (sender, nextInvoked) = await RunAsync(CreateHttpContext());

        nextInvoked.ShouldBeTrue();
        sender.Captured.ShouldBeEmpty();
    }

    [Fact]
    public async Task Enabled_EnqueuesOneEntryPerRequest()
    {
        var userId = Guid.NewGuid();
        var user = new Mock<ICurrentUser>();
        user.SetupGet(u => u.Id).Returns(userId);
        user.SetupGet(u => u.UserName).Returns("alice");

        var (sender, nextInvoked) = await RunAsync(
            CreateHttpContext("/api/orders/42", "PUT"),
            Enabled(),
            next: ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            },
            currentUser: user.Object);

        nextInvoked.ShouldBeTrue();
        var log = sender.Captured.ShouldHaveSingleItem();
        log.Path.ShouldBe("/api/orders/42");
        log.Method.ShouldBe("PUT");
        log.StatusCode.ShouldBe(204);
        log.IpAddress.ShouldBe("203.0.113.7");
        log.UserAgent.ShouldBe("UnitTest/1.0");
        log.UserId.ShouldBe(userId);
        log.UserName.ShouldBe("alice");
        log.ResponseTime.ShouldBeGreaterThanOrEqualTo(0);
        log.CreationTime.ShouldBeInRange(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
    }

    /// <summary>
    /// 路径里的凭据不得进 <c>Sys_AccessLog</c>：访问日志页给持查看权限的管理员看，
    /// 而签署 / 分享链接的 <c>{token}</c> 本身就是能用的凭据。判定只看路由参数名（<c>{token}</c>、<c>{tempToken}</c>），
    /// 用例里的路径是合成的形状，不对应框架的某个端点。
    /// </summary>
    [Theory]
    [InlineData("/api/auth/pending-actions/S3CR3T-TOKEN", "tempToken", "/api/auth/pending-actions/***")]
    [InlineData("/api/invitations/S3CR3T-TOKEN", "token", "/api/invitations/***")]
    public async Task ACredentialInThePath_IsRedacted(string path, string key, string expected)
    {
        var (sender, _) = await RunAsync(
            CreateHttpContext(path),
            Enabled(),
            next: ctx =>
            {
                // 路由在本中间件内侧匹配。
                ctx.Request.RouteValues[key] = "S3CR3T-TOKEN";
                return Task.CompletedTask;
            });

        sender.Captured.ShouldHaveSingleItem().Path.ShouldBe(expected);
    }

    [Fact]
    public async Task ExcludedPath_IsSkipped()
    {
        var (sender, nextInvoked) = await RunAsync(CreateHttpContext("/hubs/chat"), Enabled());

        nextInvoked.ShouldBeTrue();
        sender.Captured.ShouldBeEmpty();
    }

    /// <summary>排除是按路径段匹配的：<c>/healthcheck</c> 不是 <c>/health</c>。</summary>
    [Fact]
    public async Task ExcludedPath_MatchesSegments_NotPrefixes()
    {
        var (sender, _) = await RunAsync(CreateHttpContext("/healthcheck"), Enabled());

        sender.Captured.ShouldHaveSingleItem();
    }

    /// <summary>下游抛异常：请求仍失败（异常原样上抛），但这一条按 500 记下来。</summary>
    [Fact]
    public async Task DownstreamException_IsRecordedAs500_AndRethrown()
    {
        var sender = new CapturingSender();
        var middleware = new AccessLogMiddleware(
            _ => throw new InvalidOperationException("boom"),
            sender,
            new StaticOptionsMonitor(Enabled()),
            NullLogger<AccessLogMiddleware>.Instance);

        await Should.ThrowAsync<InvalidOperationException>(
            () => middleware.InvokeAsync(CreateHttpContext(), new Mock<ICurrentUser>().Object));

        sender.Captured.ShouldHaveSingleItem().StatusCode.ShouldBe(500);
    }

    /// <summary>
    /// 业务异常按客户端实际会收到的码记：服务层抛 <c>ForbiddenException</c> 是 403，外层异常处理中间件也会这么答。
    /// 一律记 500 会让「服务器错误」的统计被业务拒绝灌满。
    /// </summary>
    [Theory]
    [InlineData("forbidden", 403)]
    [InlineData("not-found", 404)]
    public async Task DownstreamBusinessException_IsRecordedWithItsOwnStatusCode(string kind, int expected)
    {
        BusinessException failure = kind == "forbidden" ? new ForbiddenException() : new ResourceNotFoundException("Order", 42);
        var sender = new CapturingSender();
        var middleware = new AccessLogMiddleware(
            _ => throw failure,
            sender,
            new StaticOptionsMonitor(Enabled()),
            NullLogger<AccessLogMiddleware>.Instance);

        await Should.ThrowAsync<BusinessException>(
            () => middleware.InvokeAsync(CreateHttpContext(), new Mock<ICurrentUser>().Object));

        sender.Captured.ShouldHaveSingleItem().StatusCode.ShouldBe(expected);
    }

    /// <summary>
    /// 客户端断开引起的取消记 499，与异常处理中间件的处置一致：记成 500 会让「服务器错误」里混进关页面与导航。
    /// </summary>
    [Fact]
    public async Task ClientAbort_IsRecordedAs499()
    {
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();
        var context = CreateHttpContext();
        context.RequestAborted = aborted.Token;
        var sender = new CapturingSender();
        var middleware = new AccessLogMiddleware(
            _ => throw new OperationCanceledException(aborted.Token),
            sender,
            new StaticOptionsMonitor(Enabled()),
            NullLogger<AccessLogMiddleware>.Instance);

        await Should.ThrowAsync<OperationCanceledException>(
            () => middleware.InvokeAsync(context, new Mock<ICurrentUser>().Object));

        sender.Captured.ShouldHaveSingleItem().StatusCode.ShouldBe(StatusCodes.Status499ClientClosedRequest);
    }

    /// <summary>队列那头出问题不能让请求失败：访问日志是旁路。</summary>
    [Fact]
    public async Task SenderFailure_DoesNotFailTheRequest()
    {
        var sender = new Mock<IAccessLogSender>();
        sender.Setup(s => s.SendAsync(It.IsAny<AccessLogDto>())).ThrowsAsync(new InvalidOperationException("queue down"));
        var middleware = new AccessLogMiddleware(
            _ => Task.CompletedTask,
            sender.Object,
            new StaticOptionsMonitor(Enabled()),
            NullLogger<AccessLogMiddleware>.Instance);

        await Should.NotThrowAsync(() => middleware.InvokeAsync(CreateHttpContext(), new Mock<ICurrentUser>().Object));
    }

    /// <summary>
    /// 入队失败的那条告警同样写进日志文件（<c>admin/logs</c> 给只持查看权限的人看），
    /// 路径里的一次性凭据与入表时一样记为掩码。
    /// </summary>
    [Fact]
    public async Task SenderFailure_WarningCarriesTheRedactedPath()
    {
        var sender = new Mock<IAccessLogSender>();
        sender.Setup(s => s.SendAsync(It.IsAny<AccessLogDto>())).ThrowsAsync(new InvalidOperationException("queue down"));
        var logger = new CapturingLogger();
        var middleware = new AccessLogMiddleware(
            ctx =>
            {
                ctx.Request.RouteValues["tempToken"] = "S3CR3T-TOKEN";
                return Task.CompletedTask;
            },
            sender.Object,
            new StaticOptionsMonitor(Enabled()),
            logger);

        await middleware.InvokeAsync(CreateHttpContext("/api/auth/pending-actions/S3CR3T-TOKEN"), new Mock<ICurrentUser>().Object);

        var warning = logger.Entries.ShouldHaveSingleItem();
        warning.Message.ShouldContain("/api/auth/pending-actions/***");
        warning.Message.ShouldNotContain("S3CR3T-TOKEN");
    }

    private sealed class CapturingLogger : ILogger<AccessLogMiddleware>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    /// <summary>路径收敛到列宽：一个超长 URL 不能让整批 InsertMany 失败（09-04 审计表踩过同一个坑）。</summary>
    [Fact]
    public async Task Path_IsTruncatedToTheColumnWidth()
    {
        var longPath = "/api/" + new string('x', 700);

        var (sender, _) = await RunAsync(CreateHttpContext(longPath), Enabled());

        sender.Captured.ShouldHaveSingleItem().Path.Length.ShouldBe(AccessLogMiddleware.MaxPathLength);
    }

    /// <summary>
    /// 列宽正好落在 emoji 的两个码元之间：少截一个字符，不留孤立的高代理项。
    /// PostgreSQL 的严格 UTF-8 编码会拒绝那一行，整批访问日志随之丢弃；SQLite 照收，所以要在这里断言。
    /// </summary>
    [Fact]
    public async Task Path_CutInsideASurrogatePair_DropsTheWholeCharacter()
    {
        var path = "/" + new string('x', AccessLogMiddleware.MaxPathLength - 2) + "\U0001F600tail";

        var (sender, _) = await RunAsync(CreateHttpContext(path), Enabled());

        var captured = sender.Captured.ShouldHaveSingleItem().Path;
        captured.Length.ShouldBe(AccessLogMiddleware.MaxPathLength - 1);
        char.IsHighSurrogate(captured[^1]).ShouldBeFalse();
    }

    /// <summary>后台落库前的第二道裁剪（<see cref="AccessLogColumns.Fit"/>）同样不切开代理对。</summary>
    [Fact]
    public void AccessLogColumnsFit_CutInsideASurrogatePair_DropsTheWholeCharacter()
    {
        var value = new string('c', AccessLogColumns.IpCityMaxLength - 1) + "\U0001F600";

        var fitted = AccessLogColumns.Fit(value, AccessLogColumns.IpCityMaxLength).ShouldNotBeNull();

        fitted.Length.ShouldBe(AccessLogColumns.IpCityMaxLength - 1);
        char.IsHighSurrogate(fitted[^1]).ShouldBeFalse();
    }

    /// <summary>
    /// 每一条都记下调用者的租户：读取面按它收口。不记的话多租户部署里所有租户的访问日志混在一张表里，
    /// 租户管理员要么看得到别家的用户名、IP 与路径，要么（收口之后）一条都看不到自己的。
    /// </summary>
    [Fact]
    public async Task Entry_CarriesTheCallersTenant()
    {
        var tenantId = Guid.NewGuid();
        var user = new Mock<ICurrentUser>();
        user.SetupGet(u => u.Id).Returns(Guid.NewGuid());
        user.SetupGet(u => u.TenantId).Returns(tenantId);

        var (sender, _) = await RunAsync(CreateHttpContext(), Enabled(), currentUser: user.Object);

        sender.Captured.ShouldHaveSingleItem().TenantId.ShouldBe(tenantId);
    }

    /// <summary>匿名请求与宿主用户没有租户：记成 null（宿主才看得到），而不是猜一个。</summary>
    [Fact]
    public async Task AnonymousEntry_HasNoTenant()
    {
        var (sender, _) = await RunAsync(CreateHttpContext(), Enabled());

        sender.Captured.ShouldHaveSingleItem().TenantId.ShouldBeNull();
    }
}
