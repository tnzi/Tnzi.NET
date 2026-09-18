namespace Tnzi.AI.Services;

/// <summary>
/// 一次经 <see cref="ISubAgentRunCancellationRegistry"/> 发出的取消及其人类可读的原因（写进 <c>AgentRun.Error</c>）。
/// </summary>
public sealed record RunCancellation(RunCancellationKind Kind, string Reason);
