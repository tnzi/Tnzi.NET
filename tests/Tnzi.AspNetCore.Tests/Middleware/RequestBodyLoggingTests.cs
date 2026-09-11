using Microsoft.Extensions.DependencyInjection;
using System.Text;

namespace Tnzi.AspNetCore.Tests.Middleware;

/// <summary>
/// 请求体日志的脱敏与截断<b>顺序</b>。
///
/// 守的是一条只在「体够长」时才出现的线：脱敏器按 JSON 解析，
/// 先截断会把 JSON 截成非法串，于是脱敏器原样返回 —— 长请求体一个字段都没被掩掉，
/// 而短请求体一切正常。日志里因此只有超过 <c>MaxRequestBodyLength</c> 的那些请求泄漏凭据，
/// 症状与「脱敏没接上」完全不同，抽查短请求永远看不出来。
/// 响应体那条路径一直是对的（先脱敏再截断），两条路径必须同序。
/// </summary>
public class RequestBodyLoggingTests
{
    private sealed class StaticMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    /// <summary>捕获中间件记下的那一条 <see cref="RequestLogEntry"/>。</summary>
    private sealed class CapturingLogger : ILogger<RequestTrackingMiddleware>
    {
        public List<RequestLogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (state is IReadOnlyList<KeyValuePair<string, object?>> values)
            {
                foreach (var pair in values)
                {
                    if (pair.Value is RequestLogEntry entry)
                    {
                        Entries.Add(entry);
                    }
                }
            }
        }
    }

    /// <summary>跑一次中间件，返回它记下的那条日志。</summary>
    private static async Task<RequestLogEntry> LogOfAsync(string body, RequestTrackingOptions options)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        // 刻意不走 /auth/ ——那条路径整条不采集体，会把这个用例变成永远绿。
        context.Request.Path = "/api/profile";
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Request.ContentLength = context.Request.Body.Length;
        context.RequestServices = new ServiceCollection().BuildServiceProvider();
        context.Response.Body = new MemoryStream();

        var logger = new CapturingLogger();
        var middleware = new RequestTrackingMiddleware(
            _ => Task.CompletedTask,
            logger,
            new StaticMonitor<AspNetCoreOptions>(new AspNetCoreOptions()),
            new StaticMonitor<RequestTrackingOptions>(options));

        await middleware.InvokeAsync(context);

        return Assert.Single(logger.Entries);
    }

    /// <summary>一段合法 JSON：凭据在前，填充在后，总长超过 <paramref name="minLength"/>。</summary>
    private static string BodyWithCredential(string credential, int minLength)
    {
        var padding = new string('x', minLength);
        return $$"""{"userName":"alice","password":"{{credential}}","note":"{{padding}}"}""";
    }

    [Fact]
    public async Task ALongRequestBody_StillHasItsCredentialsRedacted()
    {
        // 1024 是 MaxRequestBodyLength 的默认值：这条体超过它，而 password 落在截断点之前。
        var options = new RequestTrackingOptions { LogRequestBody = true };
        var body = BodyWithCredential("hunter2", options.MaxRequestBodyLength + 200);

        var entry = await LogOfAsync(body, options);

        Assert.DoesNotContain("hunter2", entry.RequestBody);
        Assert.Contains(Tnzi.Security.RequestBodyRedactor.RedactedValue, entry.RequestBody);
    }

    [Fact]
    public async Task AShortRequestBody_IsRedactedToo()
    {
        // 防锈：上一条若因为「体压根没被采集」而通过，这一条会一起变绿而不是变红。
        // 两条同时绿才说明采集是通的、脱敏也是通的。
        var options = new RequestTrackingOptions { LogRequestBody = true };

        var entry = await LogOfAsync("""{"password":"hunter2"}""", options);

        Assert.DoesNotContain("hunter2", entry.RequestBody);
        Assert.Contains(Tnzi.Security.RequestBodyRedactor.RedactedValue, entry.RequestBody);
    }

    [Fact]
    public async Task ALongRequestBody_IsStillTruncated()
    {
        // 脱敏顺序改对了，截断这件事不能跟着丢 —— 否则日志会被整条请求体撑爆。
        var options = new RequestTrackingOptions { LogRequestBody = true, MaxRequestBodyLength = 64 };
        var body = BodyWithCredential("hunter2", 500);

        var entry = await LogOfAsync(body, options);

        Assert.NotNull(entry.RequestBody);
        Assert.Contains("(truncated)", entry.RequestBody);
        Assert.DoesNotContain("hunter2", entry.RequestBody);
    }

    [Fact]
    public async Task TheDownstreamPipeline_StillSeesTheWholeBody()
    {
        // 采集是旁路，不能吃掉请求体：读完必须把流位置退回去，
        // 否则模型绑定拿到的是空体，而日志一切正常。
        var options = new RequestTrackingOptions { LogRequestBody = true };
        const string body = """{"userName":"alice","password":"hunter2"}""";

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/profile";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.RequestServices = new ServiceCollection().BuildServiceProvider();

        string? seenByDownstream = null;
        var middleware = new RequestTrackingMiddleware(
            async ctx =>
            {
                using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
                seenByDownstream = await reader.ReadToEndAsync();
            },
            new CapturingLogger(),
            new StaticMonitor<AspNetCoreOptions>(new AspNetCoreOptions()),
            new StaticMonitor<RequestTrackingOptions>(options));

        await middleware.InvokeAsync(context);

        Assert.Equal(body, seenByDownstream);
    }
}
