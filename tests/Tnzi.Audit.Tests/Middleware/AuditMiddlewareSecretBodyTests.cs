using System.Text;
using Tnzi.Audit.Tests.TestSupport;

namespace Tnzi.Audit.Tests.Middleware;

/// <summary>
/// 打开请求体采集后，框架自己端点上的凭据不得落进 <c>Audit_Operation.RequestBody</c>。
/// </summary>
/// <remarks>
/// 加密设置的写入体是 <c>{"value": 明文}</c>：字段名认不出，只能按端点整条不采集；
/// MCP 服务器注册的 <c>authToken</c> 与配置中心以配置路径为键的保存体靠默认名单与按末段判定挡住。
/// </remarks>
public class AuditMiddlewareSecretBodyTests
{
    private const string Secret = "S3CR3T-NEVER-STORED";

    private sealed class CapturingAuditSender : IAuditSender
    {
        public List<AuditOperation> Captured { get; } = [];

        public Task SendAsync(AuditOperation operation)
        {
            Captured.Add(operation);
            return Task.CompletedTask;
        }
    }

    private static async Task<AuditOperation> CaptureAsync(string method, string path, string json)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.ContentType = "application/json";
        var body = Encoding.UTF8.GetBytes(json);
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentLength = body.Length;

        var sender = new CapturingAuditSender();
        var middleware = new AuditMiddleware(
            _ => Task.CompletedTask,
            NullLogger<AuditMiddleware>.Instance,
            sender,
            new StaticOptionsMonitor<AuditOptions>(new AuditOptions { EnableRequestBodyCapture = true }),
            new RequestBodyRedactor());

        await middleware.InvokeAsync(context, new Mock<ICurrentUser>().Object, new EntityAuditCollector());
        return sender.Captured.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task EncryptedSettingWrite_BodyIsNotCaptured()
    {
        var operation = await CaptureAsync("PUT", "/api/admin/settings/payment/stripe-key/encrypted", $$"""{"value":"{{Secret}}"}""");

        (operation.RequestBody ?? string.Empty).ShouldNotContain(Secret);
    }

    [Fact]
    public async Task McpServerRegistration_AuthTokenIsRedacted_RestIsKept()
    {
        var operation = await CaptureAsync("POST", "/api/admin/mcp/client", $$"""{"name":"docs","authToken":"{{Secret}}"}""");

        operation.RequestBody.ShouldNotBeNull();
        operation.RequestBody.ShouldNotContain(Secret);
        operation.RequestBody.ShouldContain("\"name\":\"docs\"");
    }

    [Fact]
    public async Task SettingsCenterSave_SecretConfigPathIsRedacted()
    {
        var operation = await CaptureAsync("PUT", "/api/admin/settings-center/payment", $$"""{"Stripe:SecretKey":"{{Secret}}","App:Name":"Shop"}""");

        operation.RequestBody.ShouldNotBeNull();
        operation.RequestBody.ShouldNotContain(Secret);
        operation.RequestBody.ShouldContain("\"App:Name\":\"Shop\"");
    }
}
