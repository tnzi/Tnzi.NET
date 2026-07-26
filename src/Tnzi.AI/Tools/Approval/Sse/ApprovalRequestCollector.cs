using System.Threading.Channels;

namespace Tnzi.AI.Tools.Approval.Sse;

/// <summary>
/// Per-execution (scoped) buffer of approval requests emitted by the SSE handler.
/// Consumers (e.g., an SSE endpoint streaming to a browser) read from <see cref="Reader"/>
/// to publish the request to the end user.
/// </summary>
public sealed class ApprovalRequestCollector
{
    /// <summary>
    /// Bounded channel - sized to comfortably accommodate concurrent destructive tool calls
    /// from a single agent run without blocking the writer. If callers consistently fill it
    /// the SSE consumer is too slow / disconnected and writers will wait (which is the correct
    /// back-pressure behavior).
    /// </summary>
    private readonly Channel<PendingApprovalRequest> _channel = Channel.CreateBounded<PendingApprovalRequest>(
        new BoundedChannelOptions(16)
        {
            SingleWriter = false,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });

    public ChannelReader<PendingApprovalRequest> Reader => _channel.Reader;

    /// <summary>
    /// Writes the pending approval to the channel.
    /// Called by <see cref="SseToolApprovalHandler"/> before it blocks on the decision wait.
    /// </summary>
    public ValueTask WriteAsync(PendingApprovalRequest pending, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(pending, ct);
}
