using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.AspNetCore.Http;
using Tnzi.Http;
using Tnzi.Security;

namespace Tnzi.AspNetCore.Tests.Http;

/// <summary>
/// 传输加密：请求解密与<b>响应加密</b>。
///
/// 守的是一条最坏形态的线 —— **配了就以为加密了**。响应加密此前在
/// `await next` 之后才动手，那时 Response.Body 已经是 Kestrel 的只写流：
/// 回读抛 NotSupportedException，赋一个新的 MemoryStream 也送不出去，
/// 外层异常中间件看到 HasStarted 只记一条 "Response has already started"。
/// 于是配置里写着加密开着，响应逐字明文发出，日志里只有一行看不出意思的 Warning。
/// </summary>
public class HostHttpCryptoTests
{
    private sealed record Party(string PrivateKey, string PublicKey);

    private static Party NewParty()
    {
        var rsa = new RsaHelper();
        return new Party(rsa.PrivateKey, rsa.PublicKey);
    }

    private static HostHttpCrypto CryptoFor(Party host)
        => new(
            Microsoft.Extensions.Options.Options.Create(new AspNetCoreOptions
            {
                HttpEncrypt = new HttpEncryptOptions
                {
                    Enabled = true,
                    HostPrivateKey = host.PrivateKey,
                    HostPublicKey = host.PublicKey
                }
            }),
            NullLogger<HostHttpCrypto>.Instance);

    /// <summary>跑一次中间件，返回真正写到连接上的那串字节。</summary>
    private static async Task<(string Wire, int StatusCode)> RunAsync(
        HostHttpCrypto crypto,
        HttpContext context,
        Func<HttpContext, Task> next)
    {
        var wire = new MemoryStream();
        context.Response.Body = wire;

        var middleware = new HostHttpCryptoMiddleware(crypto);
        await middleware.InvokeAsync(context, ctx => next(ctx));

        return (Encoding.UTF8.GetString(wire.ToArray()), context.Response.StatusCode);
    }

    private static HttpContext RequestFrom(Party client, string method, string? body = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = "/api/things";
        context.Request.Headers[HttpHeaderNames.ClientPublicKey] = client.PublicKey;

        if (body != null)
        {
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            context.Request.ContentLength = context.Request.Body.Length;
        }

        return context;
    }

    // ---------------------------------------------------------------
    // 响应加密
    // ---------------------------------------------------------------

    [Fact]
    public async Task ASuccessfulResponse_LeavesTheProcessEncrypted()
    {
        var host = NewParty();
        var client = NewParty();
        var clientSide = new TransmissionEncryptor(client.PrivateKey, host.PublicKey);

        const string plaintext = """{"succeeded":true,"data":{"secret":"tell-no-one"}}""";
        var context = RequestFrom(client, HttpMethods.Post, body: new TransmissionEncryptor(
            client.PrivateKey, host.PublicKey).EncryptData("""{"q":"x"}"""));

        var (wire, _) = await RunAsync(CryptoFor(host), context, async ctx =>
        {
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(plaintext);
        });

        Assert.DoesNotContain("tell-no-one", wire);
        Assert.Equal(plaintext, clientSide.DecryptAndVerifyData(wire));
    }

    [Fact]
    public async Task AGetResponse_IsEncryptedToo()
    {
        // GET 没有请求体可解密，但**响应照样要加密** —— 客户端处理器对每个成功响应
        // 都会尝试解密，服务端这边留一条明文出口，那个 GET 只会以 500 收场。
        var host = NewParty();
        var client = NewParty();
        var clientSide = new TransmissionEncryptor(client.PrivateKey, host.PublicKey);

        const string plaintext = """{"succeeded":true,"data":[1,2,3]}""";
        var context = RequestFrom(client, HttpMethods.Get);

        var (wire, _) = await RunAsync(CryptoFor(host), context,
            ctx => ctx.Response.WriteAsync(plaintext));

        Assert.NotEqual(plaintext, wire);
        Assert.Equal(plaintext, clientSide.DecryptAndVerifyData(wire));
    }

    [Fact]
    public async Task AClientThatDeclaredNoKey_GetsItsResponseVerbatim()
    {
        // 没协商就没有加密可言 —— 但响应必须原样送出，不能因为缓冲而丢字节。
        var host = NewParty();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;

        const string plaintext = """{"succeeded":true}""";

        var (wire, _) = await RunAsync(CryptoFor(host), context,
            ctx => ctx.Response.WriteAsync(plaintext));

        Assert.Equal(plaintext, wire);
    }

    [Fact]
    public async Task AFailedResponse_StaysReadable()
    {
        // 错误信封刻意不加密：客户端在解密失败与业务失败之间必须分得清，
        // 而它握有的密钥恰恰是在请求被拒时最可能出问题的东西。
        var host = NewParty();
        var client = NewParty();
        const string envelope = """{"succeeded":false,"code":404,"message":"Not found"}""";

        var context = RequestFrom(client, HttpMethods.Get);
        var (wire, status) = await RunAsync(CryptoFor(host), context, async ctx =>
        {
            ctx.Response.StatusCode = 404;
            await ctx.Response.WriteAsync(envelope);
        });

        Assert.Equal(404, status);
        Assert.Equal(envelope, wire);
    }

    [Fact]
    public async Task AnEmptyResponse_StaysEmpty()
    {
        var host = NewParty();
        var client = NewParty();
        var context = RequestFrom(client, HttpMethods.Get);

        var (wire, status) = await RunAsync(CryptoFor(host), context, ctx =>
        {
            ctx.Response.StatusCode = 204;
            return Task.CompletedTask;
        });

        Assert.Equal(204, status);
        Assert.Equal(string.Empty, wire);
    }

    [Fact]
    public async Task WhenTheHandlerThrows_TheHalfWrittenBodyIsDropped()
    {
        // ★ 下游写了一半才抛，缓冲里那几个字节是一个**不完整**的载荷。
        //   把它送出去只会和外层异常中间件随后写的错误信封拼在一起，
        //   客户端两样都解析不了 —— 所以丢掉它，只留那个信封。
        //   （缓冲的副作用是 HasStarted 仍为 false，那个信封确实写得出去。）
        var host = NewParty();
        var client = NewParty();
        var context = RequestFrom(client, HttpMethods.Get);
        var wire = new MemoryStream();
        context.Response.Body = wire;

        var middleware = new HostHttpCryptoMiddleware(CryptoFor(host));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            middleware.InvokeAsync(context, async ctx =>
            {
                await ctx.Response.WriteAsync("""{"data":"half of a payl""");
                throw new InvalidOperationException("boom");
            }));

        Assert.Empty(wire.ToArray());
        // 真实流必须已经被交还，异常中间件才写得进去。
        Assert.Same(wire, context.Response.Body);
        Assert.False(context.Response.HasStarted);
    }

    /// <summary>只写、不可定位的连接流 —— Kestrel 的 <c>Response.Body</c> 就是这个形状。</summary>
    private sealed class WriteOnlyStream(Stream sink) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => sink.Flush();
        public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
        public override long Seek(long o, SeekOrigin r) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => sink.Write(b, o, c);
    }

    [Fact]
    public async Task ItWorksAgainstAWriteOnlyConnectionStream()
    {
        // ★ 这一条才是那个缺陷的真实形态。用 MemoryStream 当连接流时，
        //   坏掉的实现「只是」把加密结果丢在一个没人读的流上；
        //   而生产里的 Response.Body 既不可读也不可定位，回读直接抛。
        //   夹具比生产宽松，一个从未生效的特性就能一路绿到线上。
        var host = NewParty();
        var client = NewParty();
        var clientSide = new TransmissionEncryptor(client.PrivateKey, host.PublicKey);
        var sink = new MemoryStream();

        var context = RequestFrom(client, HttpMethods.Get);
        context.Response.Body = new WriteOnlyStream(sink);

        const string plaintext = """{"succeeded":true,"data":"visible"}""";
        var middleware = new HostHttpCryptoMiddleware(CryptoFor(host));
        await middleware.InvokeAsync(context, ctx => ctx.Response.WriteAsync(plaintext));

        var wire = Encoding.UTF8.GetString(sink.ToArray());
        Assert.DoesNotContain("visible", wire);
        Assert.Equal(plaintext, clientSide.DecryptAndVerifyData(wire));
    }

    // ---------------------------------------------------------------
    // 请求解密（既有行为，防锈）
    // ---------------------------------------------------------------

    [Fact]
    public async Task AnEncryptedRequestBody_ReachesTheHandlerInClear()
    {
        var host = NewParty();
        var client = NewParty();
        const string plaintext = """{"userName":"alice"}""";

        var context = RequestFrom(client, HttpMethods.Post,
            body: new TransmissionEncryptor(client.PrivateKey, host.PublicKey).EncryptData(plaintext));

        string? seen = null;
        await RunAsync(CryptoFor(host), context, async ctx =>
        {
            using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
            seen = await reader.ReadToEndAsync();
            await ctx.Response.WriteAsync("{}");
        });

        Assert.Equal(plaintext, seen);
    }
}
