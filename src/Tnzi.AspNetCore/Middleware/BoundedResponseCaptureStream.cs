namespace Tnzi.AspNetCore.Middleware;

/// <summary>
/// 直通式响应体采集流：每次写入<b>立刻</b>转发给原始响应流，旁路只留前 N 字节。
/// </summary>
/// <remarks>
/// <para>
/// 与「换成 MemoryStream、action 跑完再拷回」的区别就是这一句「立刻」：流式端点（SSE、分块）的每一块
/// 照常到达客户端，大文件下载不在内存里多存一份。旁路缓冲最多 <c>capacity + 1</c> 字节 ——
/// 多出的一个字节只为判断「超了」，超了就把已缓冲的丢掉（不记一半，见 <see cref="BodyCapturePolicy"/>）。
/// </para>
/// <para>
/// 采不采在<b>第一次写入</b>那一刻决定：那时 <c>Response.ContentType</c> 已由结果执行器设好，
/// 而在 action 之前它还是空的。<c>Content-Encoding</c> 也在那一刻看：响应压缩（与主机端 HTTP 加密）
/// 中间件在本流内侧，写进来的是压缩 / 密文字节而 Content-Type 仍是 <c>application/json</c> ——
/// 采了只能得到乱码，脱敏器对它无效，「先脱敏再截断」的不变量不成立，所以编码过的体整条不采。
/// </para>
/// </remarks>
internal sealed class BoundedResponseCaptureStream : Stream
{
    private readonly Stream _inner;
    private readonly HttpResponse _response;
    private readonly int _capacity;
    private MemoryStream? _buffer;
    private bool _decided;

    public BoundedResponseCaptureStream(Stream inner, HttpResponse response, int capacity)
    {
        _inner = Check.NotNull(inner);
        _response = Check.NotNull(response);
        _capacity = Math.Max(0, capacity);
    }

    /// <summary>体超过了采集上界（已缓冲的部分已丢弃）。</summary>
    public bool Exceeded { get; private set; }

    /// <summary>按 Content-Type / Content-Encoding 判定为不采集。</summary>
    public bool Skipped { get; private set; }

    /// <summary>第一次写入时看到的 Content-Type。</summary>
    public string? ContentType { get; private set; }

    /// <summary>第一次写入时看到的 Content-Encoding（空或 identity 视为未编码）。</summary>
    public string? ContentEncoding { get; private set; }

    /// <summary>已缓冲的字节（未超界、未跳过时才有内容）。</summary>
    public ReadOnlyMemory<byte> Captured
        => _buffer == null ? ReadOnlyMemory<byte>.Empty : new ReadOnlyMemory<byte>(_buffer.GetBuffer(), 0, (int)_buffer.Length);

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => _inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        _inner.Write(buffer, offset, count);
        Capture(new ReadOnlySpan<byte>(buffer, offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _inner.Write(buffer);
        Capture(buffer);
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await _inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
        Capture(new ReadOnlySpan<byte>(buffer, offset, count));
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await _inner.WriteAsync(buffer, cancellationToken);
        Capture(buffer.Span);
    }

    private void Capture(ReadOnlySpan<byte> chunk)
    {
        if (!_decided)
        {
            _decided = true;
            ContentType = _response.ContentType;
            ContentEncoding = _response.Headers.ContentEncoding.ToString();
            if (!BodyCapturePolicy.IsCapturable(ContentType) || BodyCapturePolicy.IsEncoded(ContentEncoding))
            {
                Skipped = true;
            }
            else if (_response.ContentLength is { } declared && declared > _capacity)
            {
                Exceeded = true;
            }
        }

        if (Skipped || Exceeded)
        {
            return;
        }

        _buffer ??= new MemoryStream();
        if (_buffer.Length + chunk.Length > _capacity)
        {
            Exceeded = true;
            _buffer.Dispose();
            _buffer = null;
            return;
        }

        _buffer.Write(chunk);
    }

    protected override void Dispose(bool disposing)
    {
        // 原始流归 HTTP 服务器所有，这里只放掉旁路缓冲。
        if (disposing)
        {
            _buffer?.Dispose();
            _buffer = null;
        }

        base.Dispose(disposing);
    }
}
