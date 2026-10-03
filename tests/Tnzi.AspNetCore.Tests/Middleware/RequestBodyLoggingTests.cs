using System.Text;
using static Tnzi.AspNetCore.Tests.Middleware.RequestTrackingTestSupport;

namespace Tnzi.AspNetCore.Tests.Middleware;

/// <summary>
/// 请求体日志的脱敏与截断<b>顺序</b>，以及采集本身的<b>上界</b>。
///
/// 顺序守的是一条只在「体够长」时才出现的线：脱敏器按 JSON 解析，
/// 先截断会把 JSON 截成非法串，于是脱敏器原样返回 —— 长请求体一个字段都没被掩掉，
/// 而短请求体一切正常。日志里因此只有超过 <c>MaxRequestBodyLength</c> 的那些请求泄漏凭据，
/// 症状与「脱敏没接上」完全不同，抽查短请求永远看不出来。
/// 响应体那条路径一直是对的（先脱敏再截断），两条路径必须同序。
///
/// 上界守的是另一条线：<c>MaxRequestBodyLength</c> 只裁剪<b>已经在内存里</b>的那个 string，
/// 读侧此前是 <c>ReadToEndAsync()</c> —— 打开 <c>LogRequestBody</c>（配置中心一点就开）之后，
/// 一次 100 MB 的 multipart 上传会被整条读成 ~200 MB 的 UTF-16 string 再交给 JSON 脱敏器，
/// 几个并发上传即 OOM。采集必须在读之前就按 Content-Length / Content-Type 闸住，
/// 超过 <c>MaxCapturedBodyBytes</c> 的体一个字节都不读进 string。
/// </summary>
public class RequestBodyLoggingTests
{
    /// <summary>跑一次中间件，返回它记下的那条日志。</summary>
    private static async Task<RequestLogEntry> LogOfAsync(
        string body, RequestTrackingOptions options, string contentType = "application/json", bool declareLength = true)
    {
        var context = NewContext();
        context.Request.ContentType = contentType;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        if (declareLength)
        {
            context.Request.ContentLength = context.Request.Body.Length;
        }

        var logger = new CapturingLogger();
        var middleware = NewMiddleware(_ => Task.CompletedTask, logger, options);

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
    public async Task AFormEncodedRequestBody_HasItsCredentialsRedacted()
    {
        // 表单体不是 JSON，按 JSON 脱敏会原样返回；它必须按键脱敏（两种命名写法都要挡住）。
        var options = new RequestTrackingOptions { LogRequestBody = true };

        var entry = await LogOfAsync("userName=alice&password=hunter2&client_secret=s3cr3t", options, "application/x-www-form-urlencoded");

        Assert.Contains("userName=alice", entry.RequestBody);
        Assert.DoesNotContain("hunter2", entry.RequestBody);
        Assert.DoesNotContain("s3cr3t", entry.RequestBody);
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

        var context = NewContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        string? seenByDownstream = null;
        var middleware = NewMiddleware(
            async ctx =>
            {
                using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
                seenByDownstream = await reader.ReadToEndAsync();
            },
            new CapturingLogger(),
            options);

        await middleware.InvokeAsync(context);

        Assert.Equal(body, seenByDownstream);
    }

    // ---------------------------------------------------------------
    // 采集上界：超过闸门的体一个字节都不进 string
    // ---------------------------------------------------------------

    [Fact]
    public async Task OversizedBody_IsNotCaptured_AndDownstreamStillReadsFullBody()
    {
        // Content-Length 已经告诉我们它超了：不读、只记一个标记，下游照样从 0 读到整条体。
        var options = new RequestTrackingOptions { LogRequestBody = true, MaxCapturedBodyBytes = 256 };
        var body = BodyWithCredential("hunter2", 1000);

        var context = NewContext();
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Request.ContentLength = context.Request.Body.Length;

        string? seenByDownstream = null;
        var logger = new CapturingLogger();
        var middleware = NewMiddleware(
            async ctx =>
            {
                using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
                seenByDownstream = await reader.ReadToEndAsync();
            },
            logger,
            options);

        await middleware.InvokeAsync(context);

        var entry = Assert.Single(logger.Entries);
        Assert.Contains("exceeded capture limit", entry.RequestBody);
        Assert.DoesNotContain("hunter2", entry.RequestBody);
        Assert.DoesNotContain("xxxx", entry.RequestBody);
        Assert.Equal(body, seenByDownstream);
    }

    [Fact]
    public async Task ChunkedOversizedBody_IsNotCaptured()
    {
        // 没有 Content-Length（分块传输）：只能边读边数，读到闸门 + 1 就停手并丢掉已读的部分。
        var options = new RequestTrackingOptions { LogRequestBody = true, MaxCapturedBodyBytes = 256 };
        var body = BodyWithCredential("hunter2", 1000);

        var entry = await LogOfAsync(body, options, declareLength: false);

        Assert.Contains("exceeded capture limit", entry.RequestBody);
        Assert.DoesNotContain("hunter2", entry.RequestBody);
    }

    [Theory]
    [InlineData("multipart/form-data; boundary=----x")]
    [InlineData("application/octet-stream")]
    [InlineData("image/png")]
    public async Task NonTextBody_IsNotCaptured(string contentType)
    {
        // 文件上传与二进制体既不可脱敏也没有排障价值，整条不采集。
        var options = new RequestTrackingOptions { LogRequestBody = true };

        var entry = await LogOfAsync("------x\r\nContent-Disposition: form-data; name=\"f\"\r\n\r\nhunter2\r\n------x--", options, contentType);

        Assert.Contains("not captured", entry.RequestBody);
        Assert.DoesNotContain("hunter2", entry.RequestBody);
    }

    [Theory]
    [InlineData("application/json; charset=utf-8")]
    [InlineData("application/problem+json")]
    [InlineData("text/plain")]
    [InlineData("application/xml")]
    [InlineData("application/x-www-form-urlencoded")]
    public async Task TextBody_UnderTheCap_IsStillCaptured(string contentType)
    {
        // 防锈：闸门只挡该挡的。文本型、没超上界的体照旧采集（并脱敏）。
        var options = new RequestTrackingOptions { LogRequestBody = true };

        var entry = await LogOfAsync("""{"password":"hunter2","note":"visible"}""", options, contentType);

        Assert.Contains("visible", entry.RequestBody);
        Assert.DoesNotContain("hunter2", entry.RequestBody);
    }

    /// <summary>不可定位的请求体：Kestrel 给的就是这种。被 EnableBuffering 换掉即等于整条落盘一份。</summary>
    private sealed class ForwardOnlyStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData(true, false, "multipart/form-data; boundary=x", null)]
    [InlineData(true, false, "application/json", 1024L * 1024)]
    [InlineData(false, true, "application/json", 16L)]
    public async Task ABodyTheGateRefuses_IsNeverBuffered(bool logRequest, bool logResponse, string contentType, long? contentLength)
    {
        // 闸门决定「不读」，那就也不该缓冲：EnableBuffering 把 Body 换成 FileBufferingReadStream，
        // 下游每读一次都被复制进缓冲、超过 30 KB 溢出到临时文件 —— 一次 100 MB 的 multipart 上传
        // 会在磁盘上多写一份。只开 LogResponseBody 时请求体更是一个字节都用不着。
        var options = new RequestTrackingOptions { LogRequestBody = logRequest, LogResponseBody = logResponse };
        var context = NewContext();
        context.Request.ContentType = contentType;
        context.Request.ContentLength = contentLength;
        var original = new ForwardOnlyStream(Encoding.UTF8.GetBytes("{\"password\":\"hunter2\"}"));
        context.Request.Body = original;

        var middleware = NewMiddleware(_ => Task.CompletedTask, new CapturingLogger(), options);
        await middleware.InvokeAsync(context);

        Assert.Same(original, context.Request.Body);
    }

    [Fact]
    public async Task ABodyTheGateAccepts_IsBufferedSoDownstreamCanReadIt()
    {
        // 防锈：闸门放行的体仍然要缓冲，下游才读得到同一份
        var options = new RequestTrackingOptions { LogRequestBody = true };
        var context = NewContext();
        context.Request.ContentType = "application/json";
        var original = new ForwardOnlyStream(Encoding.UTF8.GetBytes("{\"note\":\"visible\"}"));
        context.Request.Body = original;

        string? downstream = null;
        var middleware = NewMiddleware(async ctx => downstream = await new StreamReader(ctx.Request.Body).ReadToEndAsync(), new CapturingLogger(), options);
        await middleware.InvokeAsync(context);

        Assert.NotSame(original, context.Request.Body);
        Assert.Equal("{\"note\":\"visible\"}", downstream);
    }
}
