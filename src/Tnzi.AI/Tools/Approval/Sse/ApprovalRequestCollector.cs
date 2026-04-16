using System.Threading.Channels;
using Tnzi.DependencyInjection;

namespace Tnzi.AI.Tools.Approval.Sse;

/// <summary>
/// Per-execution (scoped) buffer of approval requests emitted by the SSE handler.
/// Consumers (e.g., an SSE endpoint streaming to a browser) read from <see cref="Reader"/>
/// to publish the request to the end user.
/// </summary>
public sealed class ApprovalRequestCollector : IScopedDependency
{
    /// <summary>
    /// Bounded channel with capacity=1 — only one tool runs at a time per request.
    /// </summary>
    private readonly Channel<PendingApprovalRequest> _channel = Channel.CreateBounded<PendingApprovalRequest>(
        new BoundedChannelOptions(1)
        {
            SingleWriter = true,
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
