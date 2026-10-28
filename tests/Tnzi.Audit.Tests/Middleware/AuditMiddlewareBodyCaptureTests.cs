using System.Text;
using Tnzi.Audit.Tests.TestSupport;

namespace Tnzi.Audit.Tests.Middleware;

/// <summary>
/// 打开请求体采集后，<c>Audit_Operation.RequestBody</c> 里只能出现脱敏过的内容或一个标记，绝不能是明文凭据：
/// 请求体先按<b>完整</b>文档脱敏，再截断。
/// </summary>
/// <remarks>
/// <para>
/// ★ 脱敏要解析整段 JSON，而截断过的 JSON 是非法串，脱敏器对非法 JSON 只能原样返回。采集若先按
/// <c>MaxRequestBodySize</c> 截取前缀再脱敏，任何超过上限的 JSON 体（批量建用户、带长描述的集成配置保存）
/// 都会连同其中的口令、API 密钥一起原样入表；表单编码的体（<c>password=...</c>）无论大小都不是 JSON，
/// 同样原样入表。持 <c>audit.operation.view</c> 的人都读得到、导得出。
/// </para>
/// <para>
/// 同理，脱敏不了的体（既不是 JSON 也不是表单编码）只记标记：无法确认其中没有凭据的内容不入表。
/// </para>
/// </remarks>
public class AuditMiddlewareBodyCaptureTests
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

    /// <summary>不可定位的请求体（Kestrel 的原始形态）：中间件要读它就得先开缓冲，可以据此看出它开没开。</summary>
    private sealed class NonSeekableStream(byte[] content) : Stream
    {
        private readonly MemoryStream _inner = new(content);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static AuditOptions CaptureOn(int maxRequestBodySize = 4096) => new()
    {
        EnableRequestBodyCapture = true,
        MaxRequestBodySize = maxRequestBodySize
    };

    private static DefaultHttpContext Post(string path, string contentType, string body, bool seekable = true, long? declaredLength = null)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = path;
        context.Request.ContentType = contentType;
        context.Request.Body = seekable ? new MemoryStream(bytes) : new NonSeekableStream(bytes);
        context.Request.ContentLength = declaredLength ?? bytes.Length;
        return context;
    }

    private static async Task<(AuditOperation Operation, string? DownstreamBody)> CaptureAsync(DefaultHttpContext context, AuditOptions options)
    {
        var sender = new CapturingAuditSender();
        string? downstreamBody = null;
        var middleware = new AuditMiddleware(
            async ctx =>
            {
                // 下游（模型绑定）必须照常拿到整条体。
                using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8, leaveOpen: true);
                downstreamBody = await reader.ReadToEndAsync();
            },
            NullLogger<AuditMiddleware>.Instance,
            sender,
            new StaticOptionsMonitor<AuditOptions>(options),
            new RequestBodyRedactor());

        await middleware.InvokeAsync(context, new Mock<ICurrentUser>().Object, new EntityAuditCollector());
        return (sender.Captured.ShouldHaveSingleItem(), downstreamBody);
    }

    /// <summary>一段比 <paramref name="minLength"/> 长的合法 JSON：口令在最前面，截取任何前缀都会带上它。</summary>
    private static string LargeJsonWithSecretFirst(int minLength)
    {
        var filler = new string('x', minLength);
        return $"{{\"userName\":\"alice\",\"password\":\"{Secret}\",\"notes\":\"{filler}\"}}";
    }

    [Fact]
    public async Task AJsonBodyLargerThanTheRecordLimit_IsRedactedBeforeItIsTruncated()
    {
        var body = LargeJsonWithSecretFirst(6000);

        var (operation, downstream) = await CaptureAsync(Post("/api/admin/ai/providers", "application/json", body), CaptureOn());

        operation.RequestBody.ShouldNotBeNull();
        operation.RequestBody.ShouldNotContain(Secret);
        operation.RequestBody.ShouldContain("\"userName\":\"alice\"");
        operation.RequestBody.ShouldContain($"\"password\":\"{RequestBodyRedactor.RedactedValue}\"");
        // 仍按配置截断，并标明截过：记录的长度与配置同一量级，而不是整条 6 KB。
        operation.RequestBody.ShouldEndWith("(truncated)");
        operation.RequestBody!.Length.ShouldBeLessThan(4096 + 64);
        downstream.ShouldBe(body);
    }

    [Fact]
    public async Task ABodyLongerThanItsDeclaredLength_IsNotCapturedAsAPrefix()
    {
        // 声明长度只是闸门的第一道；按声明长度截取前缀，同样会得到一段非法 JSON。
        var body = LargeJsonWithSecretFirst(6000);

        var (operation, _) = await CaptureAsync(Post("/api/admin/users/batch", "application/json", body, declaredLength: 100), CaptureOn());

        (operation.RequestBody ?? string.Empty).ShouldNotContain(Secret);
    }

    [Fact]
    public async Task ARaisedLimit_CapturesTheWholeBody_RedactsIt_ThenCutsItToTheColumn()
    {
        var body = LargeJsonWithSecretFirst(20000);

        var (operation, _) = await CaptureAsync(Post("/api/admin/users/batch", "application/json", body), CaptureOn(AuditOptions.MaxRequestBodySizeLimit));

        operation.RequestBody.ShouldNotBeNull();
        operation.RequestBody.ShouldNotContain(Secret);
        operation.RequestBody.ShouldContain($"\"password\":\"{RequestBodyRedactor.RedactedValue}\"");
        operation.RequestBody.Length.ShouldBeLessThanOrEqualTo(AuditOperationColumns.RequestBodyMaxLength);
    }

    [Fact]
    public async Task AFormUrlEncodedBody_IsRedactedByKey()
    {
        var body = $"userName=alice&password={Secret}&api_key={Secret}&remember=true";

        var (operation, downstream) = await CaptureAsync(Post("/api/admin/integrations", "application/x-www-form-urlencoded", body), CaptureOn());

        operation.RequestBody.ShouldNotBeNull();
        operation.RequestBody.ShouldNotContain(Secret);
        operation.RequestBody.ShouldContain("userName=alice");
        operation.RequestBody.ShouldContain("remember=true");
        downstream.ShouldBe(body);
    }

    /// <summary>
    /// 按 <c>MaxRequestBodySize</c> 截断的那一刀正好落在一个增补平面字符的两个码元之间：结果里不能留下孤立的高代理项。
    /// 截断后还要接上省略号后缀，孤立项因此在串的中间，列宽那一刀只看结尾、清不掉它；
    /// PostgreSQL（Npgsql 的严格 UTF-8 编码）会拒绝这一行，整批审计随之丢弃，SQLite 照收不误。
    /// 表单体在没有敏感键命中时原样保留，原始字节里的 emoji 就这样走到截断点。
    /// </summary>
    [Fact]
    public async Task ABodyCutInsideASurrogatePair_DropsTheWholeCharacter()
    {
        var options = CaptureOn();
        var prefix = "note=" + new string('a', options.MaxRequestBodySize - "note=".Length - 1);
        var form = prefix + "\U0001F600tail";

        var (operation, _) = await CaptureAsync(Post("/api/admin/users/batch", "application/x-www-form-urlencoded", form), options);

        operation.RequestBody.ShouldBe(prefix + "... (truncated)");
    }

    [Theory]
    [InlineData("text/plain", "password: " + Secret)]
    [InlineData("application/xml", "<login><password>" + Secret + "</password></login>")]
    [InlineData("application/json", "{\"password\":\"" + Secret + "\"")]
    [InlineData("application/json", "{\"password\":\"" + Secret + "\", oops")]
    public async Task ABodyThatCannotBeRedacted_IsRecordedAsAMarker(string contentType, string body)
    {
        var (operation, _) = await CaptureAsync(Post("/api/orders", contentType, body), CaptureOn());

        operation.RequestBody.ShouldNotBeNull();
        operation.RequestBody.ShouldNotContain(Secret);
        operation.RequestBody.ShouldStartWith("[request body");
    }

    [Fact]
    public async Task ABodyLargerThanTheCaptureBound_LeavesOnlyAMarker()
    {
        var body = LargeJsonWithSecretFirst(AuditOptions.MaxRequestBodySizeLimit + 10);

        var (operation, downstream) = await CaptureAsync(Post("/api/imports", "application/json", body), CaptureOn());

        operation.RequestBody.ShouldNotBeNull();
        operation.RequestBody.ShouldNotContain(Secret);
        operation.RequestBody.ShouldStartWith("[");
        downstream.ShouldBe(body);
    }

    [Fact]
    public async Task AMultipartUpload_IsNotBufferedAndLeavesOnlyAMarker()
    {
        var body = "--b\r\nContent-Disposition: form-data; name=\"password\"\r\n\r\n" + Secret + "\r\n--b--";
        var context = Post("/api/files", "multipart/form-data; boundary=b", body, seekable: false);
        var original = context.Request.Body;

        var sender = new CapturingAuditSender();
        var middleware = new AuditMiddleware(
            _ => Task.CompletedTask,
            NullLogger<AuditMiddleware>.Instance,
            sender,
            new StaticOptionsMonitor<AuditOptions>(CaptureOn()),
            new RequestBodyRedactor());
        await middleware.InvokeAsync(context, new Mock<ICurrentUser>().Object, new EntityAuditCollector());

        var operation = sender.Captured.ShouldHaveSingleItem();
        operation.RequestBody.ShouldNotBeNull();
        operation.RequestBody.ShouldNotContain(Secret);
        operation.RequestBody.ShouldContain("multipart/form-data");
        // 闸门在开缓冲之前：一个上传不该为了「记 4 KB 二进制」整条落一份临时文件。
        context.Request.Body.ShouldBeSameAs(original);
    }

    [Fact]
    public async Task ACredentialEndpoint_HasNoBodyCaptured()
    {
        // 认证端点上「哪个字段是凭据」猜不全（验证码、一次性码、临时令牌），与请求日志同一条规则：整条不采。
        var body = $"{{\"userName\":\"alice\",\"code\":\"{Secret}\"}}";

        var (operation, downstream) = await CaptureAsync(Post("/api/auth/two-factor/verify", "application/json", body), CaptureOn());

        operation.RequestBody.ShouldBeNull();
        downstream.ShouldBe(body);
    }

    /// <summary>对照：普通的小 JSON 体照常记下，只掩敏感字段，下游照常拿到原文。</summary>
    [Fact]
    public async Task ASmallJsonBody_IsStoredWithOnlyTheSensitiveFieldsMasked()
    {
        var body = $"{{\"name\":\"Widget\",\"apiKey\":\"{Secret}\"}}";

        var (operation, downstream) = await CaptureAsync(Post("/api/products", "application/json; charset=utf-8", body), CaptureOn());

        operation.RequestBody.ShouldBe($"{{\"name\":\"Widget\",\"apiKey\":\"{RequestBodyRedactor.RedactedValue}\"}}");
        downstream.ShouldBe(body);
    }

    /// <summary>对照：开关关着时不读体。</summary>
    [Fact]
    public async Task CaptureOff_StoresNoBody()
    {
        var (operation, _) = await CaptureAsync(
            Post("/api/products", "application/json", $"{{\"apiKey\":\"{Secret}\"}}"),
            new AuditOptions { EnableRequestBodyCapture = false });

        operation.RequestBody.ShouldBeNull();
    }

    [Fact]
    public async Task ASensitiveFormParameter_FromEitherList_IsRedactedInRequestParameters()
    {
        // 表单里两种写法都有：请求体字段名（camelCase）与查询参数名（snake_case）。只按其中一份掩，
        // 另一份的键（api_key / access_token）就原样进 RequestParameters。
        var body = $"name=Widget&api_key={Secret}&access_token={Secret}";
        var context = Post("/api/hooks", "application/x-www-form-urlencoded", body);

        var sender = new CapturingAuditSender();
        var middleware = new AuditMiddleware(
            async ctx => await ctx.Request.ReadFormAsync(), // 模型绑定读表单，结果缓存在请求特性上
            NullLogger<AuditMiddleware>.Instance,
            sender,
            new StaticOptionsMonitor<AuditOptions>(new AuditOptions()),
            new RequestBodyRedactor());
        await middleware.InvokeAsync(context, new Mock<ICurrentUser>().Object, new EntityAuditCollector());

        var operation = sender.Captured.ShouldHaveSingleItem();
        operation.RequestParameters.ShouldNotBeNull();
        operation.RequestParameters.ShouldNotContain(Secret);
        operation.RequestParameters.ShouldContain("\"name\":\"Widget\"");
    }
}
