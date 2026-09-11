using System.Text;
using Tnzi.Json;

namespace Tnzi.AspNetCore.Tests.Middleware;

/// <summary>
/// 中间件写出的失败信封，字段必须与控制器那条路径逐字相同。
///
/// 守的是一条没有任何症状的线：手拼一个匿名对象时，业务错误码很自然会被叫成
/// `error`，而标准信封里它叫 `errorCode`。序列化成功、状态码正确、
/// 前端照常显示 message —— 只有那句按错误码分支的判断**永远取不到值**，
/// 于是「租户不匹配」和「租户已停用」在前端是同一种错误。
/// </summary>
public class MiddlewareErrorEnvelopeTests
{
    /// <summary>反射调用 internal 的写出口：它是中间件内部实现，不该为测试挪到公开面上。</summary>
    private static async Task<JsonElement> WriteAsync(int statusCode, string message, string? errorCode)
    {
        var context = new DefaultHttpContext();
        var wire = new MemoryStream();
        context.Response.Body = wire;

        var type = typeof(RateLimitingMiddleware).Assembly
            .GetType("Tnzi.AspNetCore.Middleware.MiddlewareResults")!;
        var method = type.GetMethod("WriteErrorAsync", BindingFlags.NonPublic | BindingFlags.Static)!;

        await (Task)method.Invoke(null, [context, statusCode, message, errorCode])!;

        return JsonDocument.Parse(Encoding.UTF8.GetString(wire.ToArray())).RootElement.Clone();
    }

    [Fact]
    public async Task TheBusinessCodeIsCalledErrorCode()
    {
        // 这是那个 bug 的全部内容：字段名。
        var body = await WriteAsync(403, "The requested tenant does not match.", "TENANT_MISMATCH");

        Assert.Equal("TENANT_MISMATCH", body.GetProperty("errorCode").GetString());
        Assert.False(body.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task TheEnvelopeCarriesTheStandardFields()
    {
        var body = await WriteAsync(400, "Invalid or inactive tenant.", "INVALID_TENANT");

        Assert.False(body.GetProperty("succeeded").GetBoolean());
        Assert.Equal(400, body.GetProperty("code").GetInt32());
        Assert.Equal("Invalid or inactive tenant.", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task TheFieldNamesMatchTheControllerPath()
    {
        // ★ 防锈：不去手写一份「应该长这样」的名单，而是拿控制器真会序列化的那个信封对账。
        //   名单会和 ApiResult 各自漂移，对账不会。
        var fromMiddleware = await WriteAsync(404, "Not found", "NOT_FOUND");

        var fromController = JsonDocument
            .Parse(JsonSerializer.Serialize(ApiResult.Error("Not found", 404, "NOT_FOUND"), TnziJsonDefaults.Options))
            .RootElement;

        Assert.Equal(
            fromController.EnumerateObject().Select(p => p.Name).Order(),
            fromMiddleware.EnumerateObject().Select(p => p.Name).Order());
    }
}
