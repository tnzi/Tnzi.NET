using System.Text;
using static Tnzi.AspNetCore.Tests.Middleware.RequestTrackingTestSupport;

namespace Tnzi.AspNetCore.Tests.Middleware;

/// <summary>
/// 响应体日志的采集必须<b>有上界</b>且<b>不改变流式语义</b>。
///
/// 此前 <c>LogResponseBody</c> 把 <c>Response.Body</c> 换成一个空的 <c>MemoryStream</c>，
/// 等 action 跑完再 <c>ReadToEndAsync()</c> 成 string，<c>MaxResponseBodyLength</c> 只裁剪那个 string，
/// 最后才把整块缓冲拷回连接。于是：①一次 100 MB 的文件下载在内存里存三份（缓冲 + UTF-16 string + 拷回），
/// 几个并发即 OOM；②SSE / 分块流式端点变成「等全部生成完再一次性吐出」，客户端长时间无响应然后突然全到，
/// 症状像后端卡死而不像一个日志开关。而它是配置中心可热开的开关，最容易在生产排障当下被打开。
///
/// 正确形态是<b>直通采集</b>：每次写入立刻转发给原始流，只留前 <c>MaxCapturedBodyBytes</c> 字节；
/// 超过就不记体、只记标记（截断过的 JSON 无法脱敏，所以「超过」等于「不记」，不是「记一半」）。
/// </summary>
public class ResponseBodyLoggingTests
{
    private static async Task<(RequestLogEntry Entry, MemoryStream Original)> RunAsync(
        RequestDelegate action, RequestTrackingOptions options)
    {
        var context = NewContext();
        var original = (MemoryStream)context.Response.Body;

        var logger = new CapturingLogger();
        var middleware = NewMiddleware(action, logger, options);

        await middleware.InvokeAsync(context);

        Assert.Same(original, context.Response.Body);
        return (Assert.Single(logger.Entries), original);
    }

    private static Task WriteJsonAsync(HttpContext ctx, string json)
    {
        ctx.Response.ContentType = "application/json";
        return ctx.Response.WriteAsync(json);
    }

    [Fact]
    public async Task SmallJsonResponse_IsRedactedThenTruncated()
    {
        // 防锈：证明下面几条不是因为「采集压根没接上」才绿。
        var options = new RequestTrackingOptions { LogResponseBody = true, MaxResponseBodyLength = 48 };
        var json = $$"""{"accessToken":"secret-token","note":"{{new string('x', 200)}}"}""";

        var (entry, original) = await RunAsync(ctx => WriteJsonAsync(ctx, json), options);

        Assert.DoesNotContain("secret-token", entry.ResponseBody);
        Assert.Contains(Tnzi.Security.RequestBodyRedactor.RedactedValue, entry.ResponseBody);
        Assert.Contains("(truncated)", entry.ResponseBody);
        Assert.Equal(json, Encoding.UTF8.GetString(original.ToArray()));
    }

    [Fact]
    public async Task LargeResponse_IsForwardedInFull_ButNotCaptured()
    {
        var options = new RequestTrackingOptions { LogResponseBody = true, MaxCapturedBodyBytes = 4096 };
        var payload = new string('y', 2 * 1024 * 1024);
        var json = $$"""{"accessToken":"secret-token","blob":"{{payload}}"}""";

        var (entry, original) = await RunAsync(ctx => WriteJsonAsync(ctx, json), options);

        // 客户端一个字节都不能少。
        Assert.Equal(json.Length, original.Length);
        // 日志里只有标记，没有体（也就没有被截断成非法串、脱敏不了的半个 JSON）。
        Assert.Contains("exceeded capture limit", entry.ResponseBody);
        Assert.DoesNotContain("secret-token", entry.ResponseBody);
        Assert.DoesNotContain("yyyy", entry.ResponseBody);
    }

    [Fact]
    public async Task StreamingResponse_ReachesClientBeforeActionCompletes()
    {
        // action 写出第一块后停在一个 TCS 上；那一刻原始流里必须已经有那一块。
        // 此前的 MemoryStream 换流会让原始流在 action 返回前一直是空的 —— 这条用例在那个实现上必红。
        var options = new RequestTrackingOptions { LogResponseBody = true };
        var firstChunkWritten = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var context = NewContext();
        var original = (MemoryStream)context.Response.Body;
        var middleware = NewMiddleware(
            async ctx =>
            {
                ctx.Response.ContentType = "text/event-stream";
                await ctx.Response.WriteAsync("data: first\n\n");
                await ctx.Response.Body.FlushAsync();
                firstChunkWritten.SetResult();
                await release.Task;
                await ctx.Response.WriteAsync("data: second\n\n");
            },
            new CapturingLogger(),
            options);

        var invocation = middleware.InvokeAsync(context);
        await firstChunkWritten.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("data: first\n\n", Encoding.UTF8.GetString(original.ToArray()));

        release.SetResult();
        await invocation;

        Assert.Equal("data: first\n\ndata: second\n\n", Encoding.UTF8.GetString(original.ToArray()));
    }

    [Theory]
    [InlineData("application/octet-stream")]
    [InlineData("image/png")]
    [InlineData("text/event-stream")]
    public async Task NonTextOrStreamingContentType_IsNotCaptured(string contentType)
    {
        var options = new RequestTrackingOptions { LogResponseBody = true };
        var bytes = Encoding.UTF8.GetBytes("payload-that-must-reach-the-client");

        var (entry, original) = await RunAsync(
            async ctx =>
            {
                ctx.Response.ContentType = contentType;
                await ctx.Response.Body.WriteAsync(bytes);
            },
            options);

        Assert.Equal(bytes, original.ToArray());
        Assert.Contains("not captured", entry.ResponseBody);
        Assert.DoesNotContain("payload-that-must-reach-the-client", entry.ResponseBody);
    }

    [Theory]
    [InlineData("gzip")]
    [InlineData("br")]
    [InlineData("deflate, gzip")]
    public async Task EncodedResponse_IsNotCaptured(string contentEncoding)
    {
        // 响应压缩中间件在本中间件内侧：写进来的是压缩后的字节而 Content-Type 仍是 application/json。
        // 采了只能得到乱码，脱敏器对它无效 —— 「先脱敏再截断」的不变量在这条路径上不成立，所以整条不采。
        var options = new RequestTrackingOptions { LogResponseBody = true };
        var bytes = Encoding.UTF8.GetBytes("pretend-this-is-gzip{\"accessToken\":\"secret-token\"}");

        var (entry, original) = await RunAsync(
            async ctx =>
            {
                ctx.Response.ContentType = "application/json";
                ctx.Response.Headers.ContentEncoding = contentEncoding;
                await ctx.Response.Body.WriteAsync(bytes);
            },
            options);

        Assert.Equal(bytes, original.ToArray());
        Assert.Contains("not captured", entry.ResponseBody);
        Assert.Contains(contentEncoding, entry.ResponseBody);
        Assert.DoesNotContain("secret-token", entry.ResponseBody);
    }

    [Fact]
    public async Task IdentityEncodedResponse_IsStillCaptured()
    {
        var options = new RequestTrackingOptions { LogResponseBody = true };

        var (entry, _) = await RunAsync(
            async ctx =>
            {
                ctx.Response.Headers.ContentEncoding = "identity";
                await WriteJsonAsync(ctx, "{\"note\":\"visible\"}");
            },
            options);

        Assert.Contains("visible", entry.ResponseBody);
    }

    [Fact]
    public async Task EmptyResponse_LogsAnEmptyBody()
    {
        // 204 之类没有体的响应：不写标记，也不抛。
        var options = new RequestTrackingOptions { LogResponseBody = true };

        var (entry, original) = await RunAsync(ctx => { ctx.Response.StatusCode = 204; return Task.CompletedTask; }, options);

        Assert.Equal(0, original.Length);
        Assert.True(string.IsNullOrEmpty(entry.ResponseBody), entry.ResponseBody);
    }
}
