namespace Tnzi.AI.Infrastructure.Helpers;

public static class AgentRunStatusResolver
{
    public static AgentRunStatus Resolve(string? finishReason, AgentRunStatus defaultStatus = AgentRunStatus.Completed)
    {
        return finishReason switch
        {
            FinishReasons.AwaitingApproval => AgentRunStatus.AwaitingApproval,
            FinishReasons.RequiresClarification => AgentRunStatus.RequiresClarification,
            FinishReasons.Cancelled => AgentRunStatus.Cancelled,
            FinishReasons.Error or
            FinishReasons.Failed or
            FinishReasons.GuardrailRejected or
            FinishReasons.QuotaExceeded or
            FinishReasons.Rejected or
            FinishReasons.MaxHandoffs => AgentRunStatus.Failed,
            _ => defaultStatus
        };
    }

    public static AgentRunResult EnsureStatus(AgentRunResult result, AgentRunStatus defaultStatus = AgentRunStatus.Completed)
    {
        return result.Status.HasValue
            ? result
            : result.CloneWith(status: Resolve(result.FinishReason, defaultStatus));
    }

    public static bool ShouldGenerateThreadTitle(string? finishReason)
    {
        return finishReason switch
        {
            FinishReasons.GuardrailRejected or
            FinishReasons.QuotaExceeded or
            FinishReasons.Error or
            FinishReasons.Failed or
            FinishReasons.Rejected or
            FinishReasons.MaxHandoffs or
            FinishReasons.MaxToolIterations => false,
            _ => true
        };
    }
}
