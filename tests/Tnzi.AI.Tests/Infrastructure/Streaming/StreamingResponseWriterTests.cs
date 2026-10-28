using System.Text;
using Microsoft.AspNetCore.Http;

namespace Tnzi.AI.Tests.Infrastructure.Streaming;

/// <summary>
/// F6: StreamingResponseWriter serializer is injectable - DefaultJsonOptions is
/// public and WriteEventAsync accepts an optional JsonSerializerOptions override.
/// </summary>
public class StreamingResponseWriterTests
{
    private static (DefaultHttpContext ctx, MemoryStream body) NewResponse()
    {
        var ctx = new DefaultHttpContext();
        var body = new MemoryStream();
        ctx.Response.Body = body;
        return (ctx, body);
    }

    private static string ReadBody(MemoryStream body)
    {
        body.Position = 0;
        return new StreamReader(body).ReadToEnd();
    }

    [Fact]
    public void DefaultJsonOptions_IsCamelCase()
    {
        Assert.Same(JsonNamingPolicy.CamelCase, StreamingResponseWriter.DefaultJsonOptions.PropertyNamingPolicy);
    }

    [Fact]
    public async Task WriteEvent_DefaultsToCamelCase()
    {
        var (ctx, body) = NewResponse();

        await StreamingResponseWriter.WriteEventAsync(ctx.Response, new { FooBar = 1 }, StreamingFormat.NDJSON);

        Assert.Contains("\"fooBar\"", ReadBody(body));
    }

    /// <summary>
    /// 流式端点绕过 MVC 自己序列化，枚举必须与 REST 一样按成员名写出：
    /// 前端按 <c>event.type === 'Text'</c> 比较，数字 <c>0</c> 永远对不上。
    /// </summary>
    [Fact]
    public async Task WriteEvent_WritesEnumsByMemberName_LikeTheRestWireContract()
    {
        var (ctx, body) = NewResponse();
        var evt = new CliAgentEvent { Type = CliAgentEventType.Thinking, Content = "hm" };

        await StreamingResponseWriter.WriteEventAsync(ctx.Response, evt, StreamingFormat.SSE);

        var written = ReadBody(body);
        Assert.Contains("\"type\":\"Thinking\"", written);
        Assert.DoesNotContain("\"type\":1", written);
    }

    [Fact]
    public async Task WriteEvent_RespectsCustomJsonOptions()
    {
        var (ctx, body) = NewResponse();
        var pascal = new JsonSerializerOptions { PropertyNamingPolicy = null };

        await StreamingResponseWriter.WriteEventAsync(ctx.Response, new { FooBar = 1 }, StreamingFormat.NDJSON, jsonOptions: pascal);

        Assert.Contains("\"FooBar\"", ReadBody(body));
    }

    // -------------------------------------------------------------------------
    // 心跳：反向代理按「多久没有字节」断开上游（nginx proxy_read_timeout 默认 60s），
    // 长工具执行与模型缓冲工具参数时流是静默的。心跳必须在等下一个事件的空档里写出，
    // 下一个事件到了才补一个心跳、或事件刚写完紧跟一个心跳，都盖不住空档。
    // -------------------------------------------------------------------------

    private static readonly TimeSpan ShortInterval = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan SilentGap = TimeSpan.FromMilliseconds(400);

    private static (DefaultHttpContext ctx, RecordingBody body) NewRecordingResponse()
    {
        var ctx = new DefaultHttpContext();
        var body = new RecordingBody();
        ctx.Response.Body = body;
        return (ctx, body);
    }

    /// <summary>先给出 <paramref name="first"/>，静默 <see cref="SilentGap"/>，在空档结束、第二个事件出现之前记下响应体快照。</summary>
    private static async IAsyncEnumerable<T> WithSilentGap<T>(T first, T second, RecordingBody body, List<string> snapshotsAtGapEnd, [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return first;
        await Task.Delay(SilentGap, ct);
        snapshotsAtGapEnd.Add(body.Snapshot());
        yield return second;
    }

    [Fact]
    public async Task WriteFullStream_SilentGapBetweenEvents_WritesHeartbeatsWhileWaiting()
    {
        var (ctx, body) = NewRecordingResponse();
        var snapshots = new List<string>();
        var stream = WithSilentGap(new StreamEvent { Delta = "first" }, new StreamEvent { Delta = "second", IsDone = true }, body, snapshots);

        await StreamingResponseWriter.WriteFullStreamAsync(ctx.Response, stream, StreamingFormat.SSE, heartbeatInterval: ShortInterval);

        snapshots.Single().ShouldContain(": heartbeat");
        var text = body.Snapshot();
        text[text.IndexOf("second", StringComparison.Ordinal)..].ShouldNotContain(": heartbeat");
    }

    [Fact]
    public async Task WriteFullStream_EventsArriveFasterThanTheInterval_WritesNoHeartbeat()
    {
        var (ctx, body) = NewRecordingResponse();
        var stream = new[] { new StreamEvent { IsToolCall = true }, new StreamEvent { Delta = "done", IsDone = true } }.ToAsyncEnumerable();

        await StreamingResponseWriter.WriteFullStreamAsync(ctx.Response, stream, StreamingFormat.SSE, heartbeatInterval: TimeSpan.FromSeconds(30));

        body.Snapshot().ShouldNotContain(": heartbeat");
    }

    [Fact]
    public async Task WriteFullStream_Ndjson_HeartbeatIsABlankLine()
    {
        var (ctx, body) = NewRecordingResponse();
        var snapshots = new List<string>();
        var stream = WithSilentGap(new StreamEvent { Delta = "first" }, new StreamEvent { Delta = "second", IsDone = true }, body, snapshots);

        await StreamingResponseWriter.WriteFullStreamAsync(ctx.Response, stream, StreamingFormat.NDJSON, heartbeatInterval: ShortInterval);

        snapshots.Single().ShouldContain("}\n\n");
    }

    [Fact]
    public async Task WriteFullStream_GenericOverload_WritesHeartbeatsWhileWaiting()
    {
        var (ctx, body) = NewRecordingResponse();
        var snapshots = new List<string>();
        var stream = WithSilentGap("first", "second", body, snapshots);

        await StreamingResponseWriter.WriteFullStreamAsync(
            ctx.Response, stream, s => new { value = s, done = s == "second" }, e => e.done,
            ex => new { value = ex.Message, done = true }, StreamingFormat.SSE, heartbeatInterval: ShortInterval);

        snapshots.Single().ShouldContain(": heartbeat");
    }

    [Fact]
    public async Task WriteStreamWithHeartbeat_SilentGap_WritesHeartbeatsWhileWaiting()
    {
        var (ctx, body) = NewRecordingResponse();
        var snapshots = new List<string>();
        var stream = WithSilentGap(new StreamEvent { Delta = "first" }, new StreamEvent { Delta = "second" }, body, snapshots);

        await StreamingResponseWriter.WriteStreamWithHeartbeatAsync(ctx.Response, stream, StreamingFormat.SSE, ShortInterval);

        snapshots.Single().ShouldContain(": heartbeat");
        body.Snapshot().ShouldContain("second");
    }

    [Fact]
    public async Task WriteFullStream_CancelledDuringTheGap_FinishesWithoutTheLaterEvent()
    {
        var (ctx, body) = NewRecordingResponse();
        using var cts = new CancellationTokenSource();
        var stream = WithSilentGap(new StreamEvent { Delta = "first" }, new StreamEvent { Delta = "never", IsDone = true }, body, [], cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(150));

        var write = StreamingResponseWriter.WriteFullStreamAsync(ctx.Response, stream, StreamingFormat.SSE, cts.Token, ShortInterval);

        (await Task.WhenAny(write, Task.Delay(TimeSpan.FromSeconds(10)))).ShouldBeSameAs(write);
        body.Snapshot().ShouldNotContain("never");
    }

    /// <summary>线程安全的响应体：心跳由计时器写入，源序列在空档里同时读快照。</summary>
    private sealed class RecordingBody : Stream
    {
        private readonly object _gate = new();
        private readonly MemoryStream _inner = new();

        public string Snapshot()
        {
            lock (_gate)
            {
                return Encoding.UTF8.GetString(_inner.ToArray());
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            lock (_gate)
            {
                _inner.Write(buffer, offset, count);
            }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Write(buffer, offset, count);
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _inner.Write(buffer.Span);
            }
            return ValueTask.CompletedTask;
        }

        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
