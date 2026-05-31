namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// Singleton registry that maps spawned background run IDs to their <see cref="CancellationTokenSource"/>.
/// Allows <see cref="IAgentRunSignalDispatcher.CancelAsync"/> to actually cancel the
/// in-process background task rather than only flipping a DB status flag.
/// </summary>
public interface ISubAgentRunCancellationRegistry
{
    /// <summary>
    /// Registers a CTS for the given <paramref name="runId"/>.
    /// The CTS lifetime is managed by the caller — it must call <see cref="Unregister"/>
    /// (typically in a <c>finally</c> block) when the run finishes.
    /// </summary>
    void Register(Guid runId, CancellationTokenSource cts);

    /// <summary>
    /// Removes the CTS for the given <paramref name="runId"/> and returns it so the
    /// caller can dispose it. Returns <see langword="null"/> if not found.
    /// </summary>
    CancellationTokenSource? Unregister(Guid runId);

    /// <summary>
    /// Cancels the CTS for the given <paramref name="runId"/> if one is registered.
    /// Returns <see langword="true"/> when a live CTS was found and cancelled.
    /// </summary>
    bool TryCancel(Guid runId);
}
