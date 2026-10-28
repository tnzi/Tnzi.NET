using System.Text;
using static Tnzi.AspNetCore.Tests.Middleware.RequestTrackingTestSupport;

namespace Tnzi.AspNetCore.Tests.Middleware;

/// <summary>
/// 打开请求 / 响应体日志后，框架自己端点上的凭据不得进日志。
/// </summary>
/// <remarks>
/// 两个开关都是配置中心可热开的排障开关。打开之后：每个绑定身份验证器的用户把 TOTP 种子写进应用日志
/// （读日志的人就能生成他的第二因子）、MCP 服务器注册把访问令牌写进日志、加密设置的明文原值写进日志。
/// 前两种按字段名就能认出来；加密设置的值叫 <c>value</c>，名字认不出，只能按端点整条不采集。
/// </remarks>
public class FrameworkCredentialBodyLoggingTests
{
    private const string Secret = "S3CR3T-NEVER-LOGGED";

    private static async Task<RequestLogEntry> RunAsync(string path, string method, string? requestJson, string? responseJson)
    {
        var context = NewContext();
        context.Request.Path = path;
        context.Request.Method = method;
        if (requestJson != null)
        {
            context.Request.ContentType = "application/json";
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestJson));
            context.Request.ContentLength = context.Request.Body.Length;
        }

        var logger = new CapturingLogger();
        var middleware = NewMiddleware(
            async ctx =>
            {
                if (responseJson != null)
                {
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync(responseJson);
                }
            },
            logger,
            new RequestTrackingOptions { LogRequestBody = true, LogResponseBody = true });

        await middleware.InvokeAsync(context);
        return Assert.Single(logger.Entries);
    }

    [Fact]
    public async Task TotpSetupResponse_NeverLogsTheSeed()
    {
        var response = $$$"""{"success":true,"data":{"sharedKey":"{{{Secret}}}","authenticatorUri":"otpauth://totp/Shop:alice?secret={{{Secret}}}&issuer=Shop"}}""";

        var entry = await RunAsync("/api/users/profile/two-factor/totp/setup", "POST", null, response);

        Assert.DoesNotContain(Secret, entry.ResponseBody ?? string.Empty);
    }

    [Fact]
    public async Task EncryptedSettingWrite_NeverLogsThePlaintextValue()
    {
        var entry = await RunAsync("/api/admin/settings/payment/stripe-key/encrypted", "PUT", $$"""{"value":"{{Secret}}","description":"key"}""", null);

        Assert.DoesNotContain(Secret, entry.RequestBody ?? string.Empty);
    }

    [Fact]
    public async Task McpServerRegistration_RedactsTheAuthToken_ButKeepsTheRest()
    {
        var entry = await RunAsync("/api/admin/mcp/client", "POST", $$"""{"name":"docs","serverUrl":"https://mcp.example.com","authToken":"{{Secret}}"}""", null);

        Assert.DoesNotContain(Secret, entry.RequestBody);
        Assert.Contains("\"name\":\"docs\"", entry.RequestBody);
    }

    [Theory]
    [InlineData("/api/notifications/devices", """{"deviceId":"d-1","deviceKey":"S3CR3T-NEVER-LOGGED"}""")]
    [InlineData("/api/payments", """{"paymentId":"p-1","clientSecret":"S3CR3T-NEVER-LOGGED"}""")]
    public async Task CredentialInAResponse_IsRedacted(string path, string response)
    {
        var entry = await RunAsync(path, "POST", null, response);

        Assert.DoesNotContain(Secret, entry.ResponseBody);
        Assert.Contains(Tnzi.Security.RequestBodyRedactor.RedactedValue, entry.ResponseBody);
    }

    [Fact]
    public async Task SettingsCenterSave_RedactsSecretConfigPaths_ButKeepsOrdinaryOnes()
    {
        var entry = await RunAsync("/api/admin/settings-center/payment", "PUT", $$"""{"Stripe:SecretKey":"{{Secret}}","App:Name":"Shop"}""", null);

        Assert.DoesNotContain(Secret, entry.RequestBody);
        Assert.Contains("\"App:Name\":\"Shop\"", entry.RequestBody);
    }
}
