using System.IO.Pipelines;
using System.Threading.Channels;

namespace Tnzi.AI.Infrastructure.Streaming;

/// <summary>
/// Stream wrapper that intercepts an SSE response stream, extracts reasoning content
/// from SSE deltas, and forwards it to a ChannelWriter while passing (potentially modified)
/// bytes through a Pipe to the consumer.
/// <para>
/// Supports multiple reasoning formats:
/// - DeepSeek R1 / Qwen QwQ / OpenAI o-series: delta.reasoning_content field
/// - Gemini 2.5: delta.content with extra_content.google.thought=true flag
/// </para>
/// <para>
/// This is the PipelinePolicy-compatible version that wraps a ContentStream (Stream),
/// unlike ReasoningExtractingStreamContent which wraps HttpContent.
/// </para>
/// </summary>
internal sealed class ReasoningExtractingStream : Stream
{
    private readonly Stream _inner;
    private readonly ChannelWriter<string> _reasoningWriter;
    private readonly Pipe _pipe = new();
    private readonly Stream _readerStream;
    private Task? _produceTask;

    /// <summary>
    /// Tracks whether we are inside a Gemini thinking block.
    /// </summary>
    private bool _inGeminiThinking;

    private readonly CancellationTokenSource _cts = new();

    public ReasoningExtractingStream(Stream inner, ChannelWriter<string> reasoningWriter)
    {
        _inner = Check.NotNull(inner);
        _reasoningWriter = Check.NotNull(reasoningWriter);
        _readerStream = _pipe.Reader.AsStream();
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        _produceTask ??= Task.Run(() => ProduceAsync(_cts.Token));
        return _readerStream.Read(buffer, offset, count);
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        _produceTask ??= Task.Run(() => ProduceAsync(_cts.Token));
        return await _readerStream.ReadAsync(buffer, offset, count, cancellationToken);
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        _produceTask ??= Task.Run(() => ProduceAsync(_cts.Token));
        return await _readerStream.ReadAsync(buffer, cancellationToken);
    }

    /// <summary>
    /// Background producer: reads the original stream line by line,
    /// extracts reasoning from SSE JSON, writes it to the channel,
    /// and passes (modified) bytes through the pipe to the SDK.
    /// </summary>
    private async Task ProduceAsync(CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(_inner, Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: false);

            while (true)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line == null) break; // EOF

                if (line.Length > 6 && line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    var jsonSpan = line.AsSpan(6);
                    if (!jsonSpan.StartsWith("[DONE]", StringComparison.Ordinal))
                    {
                        // Try standard reasoning_content (DeepSeek R1 / Qwen QwQ / OpenAI o-series)
                        var reasoning = TryExtractReasoningContent(jsonSpan);
                        if (reasoning != null)
                        {
                            await _reasoningWriter.WriteAsync(reasoning, ct);
                            var bytes = Encoding.UTF8.GetBytes(line + "\n");
                            await _pipe.Writer.WriteAsync(bytes, ct);
                            continue;
                        }

                        // Try Gemini thinking format
                        var geminiResult = TryExtractGeminiThinking(jsonSpan);
                        if (geminiResult != null)
                        {
                            if (geminiResult.Value.IsThinking && geminiResult.Value.Content != null)
                            {
                                _inGeminiThinking = true;
                                await _reasoningWriter.WriteAsync(geminiResult.Value.Content, ct);
                                var rewritten = RewriteSseLineWithEmptyContent(line);
                                var rewrittenBytes = Encoding.UTF8.GetBytes(rewritten + "\n");
                                await _pipe.Writer.WriteAsync(rewrittenBytes, ct);
                                continue;
                            }

                            if (!geminiResult.Value.IsThinking && _inGeminiThinking && geminiResult.Value.Content != null)
                            {
                                _inGeminiThinking = false;
                                var cleanContent = StripThoughtClosingTag(geminiResult.Value.Content);
                                if (!string.IsNullOrEmpty(cleanContent))
                                {
                                    var rewritten = RewriteSseLineWithContent(line, cleanContent);
                                    var rewrittenBytes = Encoding.UTF8.GetBytes(rewritten + "\n");
                                    await _pipe.Writer.WriteAsync(rewrittenBytes, ct);
                                }
                                else
                                {
                                    var rewritten = RewriteSseLineWithEmptyContent(line);
                                    var rewrittenBytes = Encoding.UTF8.GetBytes(rewritten + "\n");
                                    await _pipe.Writer.WriteAsync(rewrittenBytes, ct);
                                }
                                continue;
                            }
                        }
                    }
                }

                // Default: pass through unchanged
                var defaultBytes = Encoding.UTF8.GetBytes(line + "\n");
                await _pipe.Writer.WriteAsync(defaultBytes, ct);
            }

            _pipe.Writer.Complete();
        }
        catch (OperationCanceledException)
        {
            _pipe.Writer.Complete();
        }
        catch (Exception ex)
        {
            _pipe.Writer.Complete(ex);
        }
        finally
        {
            _reasoningWriter.TryComplete();
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

    private static GeminiThinkingResult? TryExtractGeminiThinking(ReadOnlySpan<char> json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json.ToString());
            var root = doc.RootElement;

            if (!root.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
                return null;

            var choice = choices[0];
            if (!choice.TryGetProperty("delta", out var delta))
                return null;

            if (!delta.TryGetProperty("content", out var contentProp)
                || contentProp.ValueKind != JsonValueKind.String)
                return null;

            var content = contentProp.GetString();
            if (string.IsNullOrEmpty(content))
                return null;

            var isThinking = false;
            if (delta.TryGetProperty("extra_content", out var extraContent)
                && extraContent.TryGetProperty("google", out var google)
                && google.TryGetProperty("thought", out var thought)
                && thought.ValueKind == JsonValueKind.True)
            {
                isThinking = true;
            }

            return new GeminiThinkingResult(isThinking, content);
        }
        catch
        {
            return null;
        }
    }

    private static string RewriteSseLineWithEmptyContent(string line)
        => RewriteSseLineWithContent(line, "");

    private static string RewriteSseLineWithContent(string line, string newContent)
    {
        try
        {
            var jsonStr = line.AsSpan(6).ToString();
            using var doc = JsonDocument.Parse(jsonStr);
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms))
            {
                RewriteElementWithContent(writer, doc.RootElement, newContent);
            }
            return "data: " + Encoding.UTF8.GetString(ms.ToArray());
        }
        catch
        {
            return line;
        }
    }

    private static void RewriteElementWithContent(Utf8JsonWriter writer, JsonElement element, string newContent)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            element.WriteTo(writer);
            return;
        }

        writer.WriteStartObject();
        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Name == "choices" && prop.Value.ValueKind == JsonValueKind.Array)
            {
                writer.WritePropertyName("choices");
                writer.WriteStartArray();
                foreach (var choice in prop.Value.EnumerateArray())
                {
                    writer.WriteStartObject();
                    foreach (var choiceProp in choice.EnumerateObject())
                    {
                        if (choiceProp.Name == "delta" && choiceProp.Value.ValueKind == JsonValueKind.Object)
                        {
                            writer.WritePropertyName("delta");
                            writer.WriteStartObject();
                            foreach (var deltaProp in choiceProp.Value.EnumerateObject())
                            {
                                if (deltaProp.Name == "content")
                                {
                                    writer.WriteString("content", newContent);
                                }
                                else if (deltaProp.Name == "extra_content")
                                {
                                    // Remove thinking flag from output
                                }
                                else
                                {
                                    deltaProp.WriteTo(writer);
                                }
                            }
                            writer.WriteEndObject();
                        }
                        else
                        {
                            choiceProp.WriteTo(writer);
                        }
                    }
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            else
            {
                prop.WriteTo(writer);
            }
        }
        writer.WriteEndObject();
    }

    private static string StripThoughtClosingTag(string content)
    {
        const string closingTag = "</thought>";
        return content.StartsWith(closingTag, StringComparison.Ordinal)
            ? content[closingTag.Length..].TrimStart()
            : content;
    }

    private readonly record struct GeminiThinkingResult(bool IsThinking, string? Content);

    // Required Stream overrides
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts.Cancel();
            _pipe.Writer.Complete();
            // Do NOT block on _produceTask — ProduceAsync's finally already completes pipe and channel
            _inner.Dispose();
            _readerStream.Dispose();
            _cts.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _pipe.Writer.Complete();

        if (_produceTask != null)
        {
            try
            {
                await _produceTask;
            }
            catch
            {
                // Already handled in ProduceAsync
            }
        }

        if (_inner is IAsyncDisposable asyncInner)
            await asyncInner.DisposeAsync();
        else
            _inner.Dispose();

        if (_readerStream is IAsyncDisposable asyncReader)
            await asyncReader.DisposeAsync();
        else
            _readerStream.Dispose();

        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
