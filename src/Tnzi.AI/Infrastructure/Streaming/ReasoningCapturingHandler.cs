
namespace Tnzi.AI.Infrastructure.Streaming;

/// <summary>
/// DelegatingHandler that intercepts streaming HTTP responses and extracts
/// reasoning_content from SSE deltas, forwarding them to
/// ReasoningAwareChatClientDecorator via AsyncLocal ChannelWriter.
///
/// This handler is provider-agnostic: any OpenAI-compatible provider that
/// returns reasoning_content in SSE deltas (DeepSeek-R1, future providers) is supported.
/// </summary>
internal sealed class ReasoningCapturingHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        var writer = ReasoningAwareChatClientDecorator.ReasoningChannelWriter.Value;
        if (writer != null && IsStreamingResponse(response))
        {
            response.Content = new ReasoningExtractingStreamContent(response.Content, writer);
        }

        return response;
    }

    private static bool IsStreamingResponse(HttpResponseMessage response)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        return mediaType != null
            && (mediaType.Contains("event-stream", StringComparison.OrdinalIgnoreCase)
                || mediaType.Contains("stream", StringComparison.OrdinalIgnoreCase));
    }
}
