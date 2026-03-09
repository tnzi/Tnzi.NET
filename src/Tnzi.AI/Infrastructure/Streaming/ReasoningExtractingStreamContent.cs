using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Channels;

namespace Tnzi.AI.Infrastructure.Streaming;

/// <summary>
/// Custom HttpContent that wraps a streaming response, extracts reasoning_content
/// from SSE deltas, and forwards it to a ChannelWriter while passing all original
/// bytes through a Pipe to the OpenAI SDK reader — ensuring zero byte loss.
/// </summary>
internal sealed class ReasoningExtractingStreamContent : HttpContent
{
    private readonly HttpContent _inner;
    private readonly ChannelWriter<string> _reasoningWriter;
    private readonly Pipe _pipe = new();
    private Task? _produceTask;

    public ReasoningExtractingStreamContent(HttpContent inner, ChannelWriter<string> reasoningWriter)
    {
        _inner = inner;
        _reasoningWriter = reasoningWriter;

        // Preserve Content-Type so the OpenAI SDK can negotiate format correctly
        if (inner.Headers.ContentType != null)
            Headers.ContentType = inner.Headers.ContentType;
    }

    // ReadAsStreamAsync() → CreateContentReadStreamAsync(ct) path (used by Azure Client Model)
    protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
    {
        _produceTask ??= Task.Run(() => ProduceAsync(cancellationToken));
        return Task.FromResult<Stream>(_pipe.Reader.AsStream());
    }

    // Fallback for callers that use the parameterless overload
    protected override Task<Stream> CreateContentReadStreamAsync()
        => CreateContentReadStreamAsync(CancellationToken.None);

    // CopyToAsync() → SerializeToStreamAsync() path (used by LoadIntoBufferAsync, etc.)
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        _produceTask ??= Task.Run(() => ProduceAsync(cancellationToken));
        await _pipe.Reader.CopyToAsync(stream, cancellationToken);
        if (_produceTask != null) await _produceTask;
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        => SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false; // Unknown length (streaming)
    }

    /// <summary>
    /// Background producer: reads the original response stream line by line,
    /// extracts reasoning_content from SSE JSON, writes it to the channel,
    /// and passes all original bytes through the pipe to the OpenAI SDK.
    /// </summary>
    private async Task ProduceAsync(CancellationToken ct)
    {
        try
        {
            var innerStream = await _inner.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(innerStream, Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: false);

            while (true)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line == null) break; // EOF

                // Write original line bytes (with newline) to pipe so the OpenAI SDK sees them
                var bytes = Encoding.UTF8.GetBytes(line + "\n");
                await _pipe.Writer.WriteAsync(bytes, ct);

                // Extract reasoning_content from SSE data lines
                if (line.Length > 6 && line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    var json = line.AsSpan(6); // skip "data: "
                    if (!json.StartsWith("[DONE]", StringComparison.Ordinal))
                    {
                        var reasoning = TryExtractReasoningContent(json);
                        if (reasoning != null)
                            await _reasoningWriter.WriteAsync(reasoning, ct);
                    }
                }
            }

            _pipe.Writer.Complete();
        }
        catch (Exception ex)
        {
            _pipe.Writer.Complete(ex);
        }
    }

    private static string? TryExtractReasoningContent(ReadOnlySpan<char> json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json.ToString());
            var root = doc.RootElement;

            if (!root.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
                return null;

            if (!choices[0].TryGetProperty("delta", out var delta))
                return null;

            if (!delta.TryGetProperty("reasoning_content", out var rc)
                || rc.ValueKind != JsonValueKind.String)
                return null;

            var text = rc.GetString();
            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}
