namespace Tnzi.AI.Sandbox.Events;

/// <summary>
/// Published by <see cref="Tools.SandboxTools"/> after every <c>bash</c> tool
/// invocation, regardless of success/failure/denial. Carries enough context for
/// downstream audit, quota, and observability consumers to act without
/// re-querying the sandbox.
/// </summary>
/// <remarks>
/// <para>
/// Output and stderr are truncated to <c>2048</c> bytes before publication to
/// keep audit storage bounded; consumers needing full output should hook the
/// sandbox at a lower layer.
/// </para>
/// <para>
/// When <see cref="Denied"/> is true the command was rejected before reaching
/// the underlying shell (by <c>DeniedCommandMatcher</c>); in that case
/// <see cref="DenialReason"/> contains the matched pattern / segment.
/// </para>
/// </remarks>
public class SandboxCommandExecutedEvent : EventBase
{
    /// <summary>Owning thread (workspace partition) for this sandbox.</summary>
    public Guid? ThreadId { get; init; }

    /// <summary>Authenticated user, if a current-user resolver was injected.</summary>
    public Guid? UserId { get; init; }

    // TenantId is inherited from EventBase — set it at publish time.

    /// <summary>Per-process sandbox instance identifier (e.g. <c>local-3</c> or <c>docker-abc</c>).</summary>
    public string SandboxId { get; init; } = string.Empty;

    /// <summary>The raw command supplied by the agent (before virtual-path translation).</summary>
    public string Command { get; init; } = string.Empty;

    /// <summary>Process exit code (<c>-1</c> for sandbox-level denial or timeout).</summary>
    public int ExitCode { get; init; }

    /// <summary>Truncated stdout (max <c>2048</c> bytes; tail elided with marker).</summary>
    public string Output { get; init; } = string.Empty;

    /// <summary>Truncated stderr or denial reason payload (max <c>2048</c> bytes).</summary>
    public string Stderr { get; init; } = string.Empty;

    /// <summary>Wall-clock duration of <see cref="ISandbox.ExecuteCommandAsync"/>.</summary>
    public long DurationMs { get; init; }

    /// <summary>
    /// <c>true</c> when <c>DeniedCommandMatcher</c> rejected the command before
    /// it reached the shell. Useful for separating security incidents from
    /// ordinary command failures.
    /// </summary>
    public bool Denied { get; init; }

    /// <summary>Pattern / segment that matched the blacklist, only set when <see cref="Denied"/> is true.</summary>
    public string? DenialReason { get; init; }

    /// <summary>UTC start of <c>ExecuteCommandAsync</c>.</summary>
    public DateTime ExecutedAt { get; init; }
}
