using System.Text;
using Tnzi.Audit.Tests.TestSupport;

namespace Tnzi.Audit.Tests.Middleware;

/// <summary>
/// 查询串与表单里的凭据不得原样落进审计表。
/// </summary>
/// <remarks>
/// <para>
/// <c>Audit_Operation.Url</c> 存 Path + QueryString，<c>RequestParameters</c> 存整个 Query 或表单。
/// 框架自己就会把凭据放进查询串：邮件里的重置 / 确认链接 <c>?token=</c>、passkey 注册的
/// <c>?enrollmentToken=</c>、签名文件链接 <c>?sig=</c>、分享链接口令 <c>?password=</c>、
/// SignalR 的 <c>?access_token=</c>。此前 <c>SensitiveFields</c> 只作用于请求体，
/// 路径排除只盖住 <c>/hubs</c>，其余端点的令牌原值一行行入表 —— 而那些端点的请求本身正是要审计的操作，
/// 排除路径等于不审计它们。
/// </para>
/// </remarks>
public class AuditMiddlewareRedactionTests
{
    private const string Secret = "S3CR3T-VALUE-NEVER-STORED";

    private sealed class CapturingAuditSender : IAuditSender
    {
        public List<AuditOperation> Captured { get; } = [];

        public Task SendAsync(AuditOperation operation)
        {
            Captured.Add(operation);
            return Task.CompletedTask;
        }
    }

    private static async Task<AuditOperation> CaptureAsync(DefaultHttpContext context, AuditOptions? options = null)
    {
        var sender = new CapturingAuditSender();
        var middleware = new AuditMiddleware(
            _ => Task.CompletedTask,
            NullLogger<AuditMiddleware>.Instance,
            sender,
            new StaticOptionsMonitor<AuditOptions>(options ?? new AuditOptions()),
            new RequestBodyRedactor());

        await middleware.InvokeAsync(context, new Mock<ICurrentUser>().Object, new EntityAuditCollector());
        return sender.Captured.ShouldHaveSingleItem();
    }

    private static DefaultHttpContext GetRequest(string path, string query)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        return context;
    }

    [Theory]
    [InlineData("/api/auth/confirm-email", "?userId=8c1f&token=" + Secret + "&returnUrl=%2Fhome", "token")]
    [InlineData("/api/auth/reset-password-page", "?email=a%40b.c&token=" + Secret, "token")]
    [InlineData("/api/auth/passkey/register/begin", "?enrollmentToken=" + Secret, "enrollmentToken")]
    [InlineData("/api/files/abc/download", "?sig=" + Secret, "sig")]
    [InlineData("/api/files/share/xyz", "?password=" + Secret, "password")]
    [InlineData("/api/events/stream", "?access_token=" + Secret, "access_token")]
    public async Task ACredentialInTheQueryString_NeverReachesUrlOrRequestParameters(string path, string query, string key)
    {
        var operation = await CaptureAsync(GetRequest(path, query));

        operation.Url.ShouldNotBeNull();
        operation.Url.ShouldNotContain(Secret);
        operation.Url.ShouldContain($"{key}=***");
        operation.Url.ShouldStartWith(path);

        operation.RequestParameters.ShouldNotBeNull();
        operation.RequestParameters.ShouldNotContain(Secret);
        operation.RequestParameters!.ShouldContain($"\"{key}\":\"***\"");
    }

    [Fact]
    public async Task TheOtherQueryParameters_SurviveIntact()
    {
        // 脱敏不该把审计行变成一团 *** —— 「谁确认了哪个账号的邮箱」仍然要答得出来。
        var operation = await CaptureAsync(GetRequest("/api/auth/confirm-email", "?userId=8c1f&token=" + Secret));

        operation.Url.ShouldBe("/api/auth/confirm-email?userId=8c1f&token=***");
        operation.RequestParameters!.ShouldContain("\"userId\":\"8c1f\"");
    }

    [Fact]
    public async Task AQueryWithoutCredentials_IsStoredVerbatim()
    {
        var operation = await CaptureAsync(GetRequest("/api/products", "?pageIndex=1&pageSize=20"));

        operation.Url.ShouldBe("/api/products?pageIndex=1&pageSize=20");
        operation.RequestParameters!.ShouldContain("\"pageSize\":\"20\"");
    }

    [Fact]
    public async Task ADeploymentCanSupplyItsOwnQueryKeyList()
    {
        var options = new AuditOptions { SensitiveQueryKeys = new HashSet<string>(["apiKey"], StringComparer.OrdinalIgnoreCase) };

        var operation = await CaptureAsync(GetRequest("/api/hooks", "?apiKey=" + Secret + "&token=visible"), options);

        // 自定义名单**替换**默认名单，不是叠加 —— 部署说了算，与请求日志同一口径。
        operation.Url.ShouldBe("/api/hooks?apiKey=***&token=visible");
    }

    [Fact]
    public async Task ASensitiveFormField_IsRedactedInRequestParameters()
    {
        // 表单字段就是请求体字段：按 SensitiveFields（password / token / ...）脱敏，
        // 此前整个表单原样序列化，x-www-form-urlencoded 的登录表单会把口令写进审计表。
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/auth/login";
        context.Request.ContentType = "application/x-www-form-urlencoded";
        var body = Encoding.UTF8.GetBytes("userName=alice&password=" + Secret);
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentLength = body.Length;

        var operation = await CaptureAsync(context);

        operation.RequestParameters.ShouldNotBeNull();
        operation.RequestParameters.ShouldNotContain(Secret);
        operation.RequestParameters!.ShouldContain("\"userName\":\"alice\"");
        operation.RequestParameters!.ShouldContain($"\"password\":\"{RequestBodyRedactor.RedactedValue}\"");
    }

    [Fact]
    public void TheDefaultQueryKeyList_IsTheSharedCoreList()
    {
        // 名单同源：请求日志与审计表各维护一份的结果是「某个流程比别处多露出一个键」，而那不报错。
        var options = new AuditOptions();

        options.SensitiveQueryKeys.ShouldBe(QueryStringRedactor.DefaultSensitiveKeys, ignoreOrder: true);
        options.SensitiveQueryKeys.Contains("ACCESS_TOKEN").ShouldBeTrue();
    }
}
