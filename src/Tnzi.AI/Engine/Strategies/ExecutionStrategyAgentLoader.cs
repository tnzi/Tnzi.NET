namespace Tnzi.AI.Engine.Strategies;

internal static class ExecutionStrategyAgentLoader
{
    public static async Task<AgentExecutor?> ResolveAgentAsync(Guid agentId, ExecutionStrategyContext context, CancellationToken ct)
    {
        var entity = await context.AgentRepository.GetAsync(agentId, ct);
        if (entity == null || !entity.IsEnabled) return null;

        var toolGroups = string.IsNullOrWhiteSpace(entity.ToolGroups)
            ? null
            : JsonSerializer.Deserialize<List<string>>(entity.ToolGroups);

        return await context.AgentFactory.CreateAgentAsync(
            entity.Provider, entity.Model, entity.Instructions, entity.Name,
            toolGroups, entity.Temperature, entity.MaxTokens, ct: ct);
    }

    public static string GetLatestUserQuestion(List<ChatMessage> messages)
    {
        return messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "User needs assistance.";
    }

    public static TokenUsageDto? BuildUsage(int promptTokens, int completionTokens)
    {
        return promptTokens > 0 || completionTokens > 0
            ? new TokenUsageDto
            {
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                TotalTokens = promptTokens + completionTokens
            }
            : null;
    }

    public static List<CitationDto>? MergeCitations(params IEnumerable<CitationDto>?[] sources)
    {
        var merged = sources
            .Where(x => x != null)
            .SelectMany(x => x!)
            .ToList();

        return merged.Count > 0 ? merged : null;
    }
}
