using System.Reflection;
using Tnzi.Audit.Tests.Integration;

namespace Tnzi.Audit.Tests.Middleware;

/// <summary>
/// 采集侧写进 <see cref="AuditOperation"/> 的每个用户可控字符串都必须装得进它的列。
/// </summary>
/// <remarks>
/// <para>
/// 这不是格式问题而是<b>可用性攻击面</b>：后台服务把一批（默认 100 条）审计操作用一条
/// <c>InsertMany</c> 落库，任何一行超列宽，SQL Server / PostgreSQL 会拒绝整条 INSERT，
/// 基类记一行日志后<b>整批丢弃</b>。于是一个匿名客户端只要发一个 600 字节的 <c>User-Agent</c>，
/// 就能连带抹掉同一时间窗里其他所有人的审计记录，而且可以一直重复。
/// </para>
/// <para>
/// SQLite 不检查长度上限，所以「插得进 SQLite」证明不了任何事。下面的断言直接对着
/// EF 模型里的 <c>MaxLength</c> 比长度：列宽改了、字段加了，测试跟着变。
/// </para>
/// </remarks>
public class AuditMiddlewareColumnLengthTests
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

    private static (AuditMiddleware Middleware, CapturingAuditSender Sender) CreateMiddleware(RequestDelegate next)
    {
        var sender = new CapturingAuditSender();
        var middleware = new AuditMiddleware(
            next,
            NullLogger<AuditMiddleware>.Instance,
            sender,
            new Tnzi.Audit.Tests.TestSupport.StaticOptionsMonitor<AuditOptions>(new AuditOptions()),
            new RequestBodyRedactor());
        return (middleware, sender);
    }

    /// <summary>每个用户可控输入都远超它的列宽。</summary>
    private static DefaultHttpContext CreateOversizedRequest()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = new string('M', 40);
        context.Request.Path = "/" + new string('p', 600);
        context.Request.QueryString = new QueryString("?q=" + new string('x', 3000));
        context.Request.Headers["User-Agent"] = new string('u', 1200);
        return context;
    }

    private static async Task<AuditOperation> CaptureAsync(DefaultHttpContext context, RequestDelegate next)
    {
        var (middleware, sender) = CreateMiddleware(next);
        try
        {
            await middleware.InvokeAsync(context, new Mock<ICurrentUser>().Object, new EntityAuditCollector());
        }
        catch (InvalidOperationException)
        {
            // 被测请求故意抛异常：中间件记录后原样重抛，这里只关心它记了什么。
        }

        return sender.Captured.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task EveryMappedStringColumn_ReceivesAValueThatFitsItsMaxLength()
    {
        var oversizedMessage = new string('e', 6000);
        var operation = await CaptureAsync(
            CreateOversizedRequest(),
            _ => throw new InvalidOperationException(oversizedMessage));

        var options = new DbContextOptionsBuilder<AuditTestDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var dbContext = new AuditTestDbContext(options, new Mock<ICurrentUser>().Object);
        var entityType = dbContext.Model.FindEntityType(typeof(AuditOperation)).ShouldNotBeNull();

        var checkedColumns = 0;
        foreach (var property in entityType.GetProperties().Where(p => p.ClrType == typeof(string)))
        {
            var maxLength = property.GetMaxLength();
            if (maxLength is null)
            {
                continue;
            }

            var value = (string?)typeof(AuditOperation)
                .GetProperty(property.Name, BindingFlags.Public | BindingFlags.Instance)!
                .GetValue(operation);

            (value?.Length ?? 0).ShouldBeLessThanOrEqualTo(maxLength.Value,
                $"{property.Name} exceeds its column width ({maxLength})");
            checkedColumns++;
        }

        // 守住测试自己：模型里至少要有这几列带长度，否则上面的循环一条都没比。
        checkedColumns.ShouldBeGreaterThanOrEqualTo(5);
    }

    [Fact]
    public async Task OversizedUserAgent_IsCutToTheColumnWidth_NotDropped()
    {
        var operation = await CaptureAsync(CreateOversizedRequest(), _ => Task.CompletedTask);

        operation.UserAgent.ShouldNotBeNull();
        operation.UserAgent.Length.ShouldBe(AuditOperationColumns.UserAgentMaxLength);
        operation.UserAgent.ShouldStartWith("uuuu");
    }

    [Fact]
    public async Task OversizedUrl_KeepsThePathPrefix_AndFitsTheColumn()
    {
        var operation = await CaptureAsync(CreateOversizedRequest(), _ => Task.CompletedTask);

        operation.Url.ShouldNotBeNull();
        operation.Url.Length.ShouldBe(AuditOperationColumns.UrlMaxLength);
        operation.Url.ShouldStartWith("/ppp");
    }

    [Fact]
    public async Task OversizedExceptionMessage_IsCut_WhileTheUnboundedExceptionColumnKeepsEverything()
    {
        var message = new string('e', 6000);
        var operation = await CaptureAsync(
            CreateOversizedRequest(),
            _ => throw new InvalidOperationException(message));

        operation.Message.ShouldNotBeNull();
        operation.Message.Length.ShouldBe(AuditOperationColumns.MessageMaxLength);
        // Exception 列没有长度上限：完整堆栈与消息仍然留在那里，截断只发生在有上限的列。
        operation.Exception.ShouldNotBeNull();
        operation.Exception.ShouldContain(message);
    }

    [Fact]
    public async Task FallbackFunctionName_WithoutRouteData_FitsTheColumn()
    {
        var operation = await CaptureAsync(CreateOversizedRequest(), _ => Task.CompletedTask);

        operation.FunctionName.Length.ShouldBe(AuditOperationColumns.FunctionNameMaxLength);
    }

    [Fact]
    public async Task OrdinarySizedValues_AreLeftUntouched()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "PUT";
        context.Request.Path = "/test/resource";
        context.Request.QueryString = new QueryString("?page=1");
        context.Request.Headers["User-Agent"] = "Mozilla/5.0";

        var operation = await CaptureAsync(context, _ => Task.CompletedTask);

        operation.UserAgent.ShouldBe("Mozilla/5.0");
        operation.Url.ShouldBe("/test/resource?page=1");
        operation.FunctionName.ShouldBe("PUT /test/resource");
        operation.HttpMethod.ShouldBe("PUT");
    }
}
