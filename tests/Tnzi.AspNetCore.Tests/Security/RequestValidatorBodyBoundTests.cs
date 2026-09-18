using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tnzi.AspNetCore.Tests.Security;

/// <summary>
/// 签名校验要读请求体，而读发生在比对签名之前：任何未认证调用方都能让这段代码跑到底。
/// 有 Content-Length 的请求在读之前按 1 MB 上限拒绝；没有 Content-Length（chunked）的请求此前是
/// 「先 ReadToEndAsync 再查大小」—— 体有多大就分配多大的 UTF-16 string，与 RequestTrackingMiddleware
/// 已修掉的形状逐字相同。现在 chunked 也只读到上限 + 1 字节就停。
/// </summary>
public class RequestValidatorBodyBoundTests
{
    private const string Secret = "unit-test-secret";
    private const int MaxBodyBytes = 1024 * 1024;

    /// <summary>按需产出字节、记录被读走了多少的请求体：读到哪里就是证据。</summary>
    private sealed class CountingStream(long length) : Stream
    {
        private long _position;

        public long TotalRead { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var remaining = length - _position;
            var n = (int)Math.Min(count, remaining);
            Array.Fill(buffer, (byte)'a', offset, n);
            _position += n;
            TotalRead += n;
            return n;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static RequestValidator CreateValidator()
    {
        var options = new AspNetCoreOptions
        {
            RequestValidation = new RequestValidationOptions
            {
                Enabled = true,
                RequireSignature = true,
                RequireNonce = false,
                SignatureSecretKey = Secret,
            },
        };
        return new RequestValidator(Microsoft.Extensions.Options.Options.Create(options), Mock.Of<ICache>(), NullLogger<RequestValidator>.Instance);
    }

    private static DefaultHttpContext ChunkedPost(Stream body, string signature = "deadbeef")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/orders";
        context.Request.Body = body;
        context.Request.ContentLength = null;
        context.Request.Headers["X-Timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        context.Request.Headers["X-Signature"] = signature;
        return context;
    }

    private static string Sign(HttpContext context, string body)
    {
        var timestamp = context.Request.Headers["X-Timestamp"].ToString();
        var signString = $"{timestamp}{context.Request.Method}{context.Request.Path.Value}{body}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signString))).ToLowerInvariant();
    }

    [Fact]
    public async Task ChunkedBodyOverTheLimit_IsRejectedWithoutReadingItToTheEnd()
    {
        var body = new CountingStream(8L * 1024 * 1024);
        var context = ChunkedPost(body);

        var error = await CreateValidator().ValidateAsync(context);

        Assert.Equal("Request body too large for signature validation", error);
        Assert.True(body.TotalRead <= MaxBodyBytes + 64 * 1024,
            $"validator read {body.TotalRead} bytes of a chunked body; the cap is {MaxBodyBytes} + 1");
    }

    [Fact]
    public async Task ChunkedBodyWithinTheLimit_IsStillSigned()
    {
        // 收紧上限不能把合法的 chunked 请求一起挡掉：体在上限内时照旧参与签名并能通过
        const string payload = "{\"id\":1}";
        var context = ChunkedPost(new MemoryStream(Encoding.UTF8.GetBytes(payload)));
        context.Request.Headers["X-Signature"] = Sign(context, payload);

        var error = await CreateValidator().ValidateAsync(context);

        Assert.Null(error);
        // 下游还要读同一份体：位置必须回到起点
        context.Request.Body.Position = 0;
        Assert.Equal(payload, await new StreamReader(context.Request.Body).ReadToEndAsync());
    }

    [Fact]
    public async Task DeclaredContentLengthOverTheLimit_IsRejectedBeforeAnyRead()
    {
        var body = new CountingStream(2L * 1024 * 1024);
        var context = ChunkedPost(body);
        context.Request.ContentLength = body.Length;

        var error = await CreateValidator().ValidateAsync(context);

        Assert.Equal("Request body too large for signature validation", error);
        Assert.Equal(0, body.TotalRead);
    }
}
