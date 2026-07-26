
namespace Tnzi.AI.Engine.Router;

/// <summary>
/// Router 执行策略 - 由入口 Agent 先选择目标，再委托目标 Agent 执行
/// </summary>
public class RouterExecutionStrategy : IExecutionStrategy
{
    private readonly RouterConfiguration _config;

    public RouterExecutionStrategy(RouterConfiguration config)
    {
        _config = Check.NotNull(config);
    }

    public async Task<ExecutionResult> ExecuteAsync(IAgentExecutor agent, List<ChatMessage> messages, ExecutionStrategyContext context, CancellationToken ct)
    {
        var routerAgent = InjectRouteTool(agent, _config.Targets.Keys);
        var routerResponse = await routerAgent.ExecuteAsync(messages, ct);

        var promptTokens = routerResponse.Usage?.InputTokens ?? 0;
        var completionTokens = routerResponse.Usage?.OutputTokens ?? 0;
        var routeTarget = ExtractRouteTarget(routerResponse);

        if (routeTarget == null)
        {
            if (!_config.AllowDirectResponse && _config.Targets.Count > 0)
            {
                // Fallback: route to the first target when direct response is disallowed
                routeTarget = _config.Targets.Keys.First();
                context.Logger.LogInformation("Router did not route but AllowDirectResponse=false - falling back to '{Target}'", routeTarget);
            }
            else
            {
                context.Logger.LogDebug("Router agent '{AgentName}' answered directly without routing", agent.Name);
                return BuildResult(routerResponse, [agent.Name], agent.Name, promptTokens, completionTokens);
            }
        }

        if (!_config.Targets.TryGetValue(routeTarget, out var targetAgentId))
        {
            context.Logger.LogWarning("Router target '{Target}' not in allowed targets", routeTarget);
            return BuildResult(routerResponse, [agent.Name], agent.Name, promptTokens, completionTokens);
        }

        var targetAgent = await ExecutionStrategyAgentLoader.ResolveAgentAsync(targetAgentId, context, ct);
        if (targetAgent == null)
        {
            context.Logger.LogWarning("Router target agent '{Target}' (ID: {AgentId}) not found or disabled", routeTarget, targetAgentId);
            return BuildResult(routerResponse, [agent.Name], agent.Name, promptTokens, completionTokens);
        }

        var delegatedMessages = BuildDelegatedMessages(messages, agent.Name, targetAgent.Name, routerResponse.Text);
        var targetResponse = await targetAgent.ExecuteAsync(delegatedMessages, ct);
        promptTokens += targetResponse.Usage?.InputTokens ?? 0;
        completionTokens += targetResponse.Usage?.OutputTokens ?? 0;

        return BuildResult(targetResponse, [agent.Name, targetAgent.Name], targetAgent.Name, promptTokens, completionTokens);
    }

    public async IAsyncEnumerable<AgentStreamChunk> ExecuteStreamingAsync(IAgentExecutor agent, List<ChatMessage> messages, ExecutionStrategyContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        string? detectedRoute = null;
        var routerAgent = InjectRouteTool(agent, _config.Targets.Keys, target => detectedRoute = target);

        // Buffer ALL router output (text + non-text) until we know whether routing occurs.
        // If the model writes preamble text before calling route_to_agent, we must suppress it
        // to avoid a duplicate response alongside the target agent's output.
        var preTextBuffer = new List<AgentStreamChunk>();

        await foreach (var chunk in routerAgent.ExecuteStreamingAsync(messages, ct).WithCancellation(ct))
        {
            preTextBuffer.Add(chunk);

            // Once route is detected, stop consuming the router stream
            if (detectedRoute != null)
                break;
        }

        if (detectedRoute == null)
        {
            if (!_config.AllowDirectResponse && _config.Targets.Count > 0)
            {
                // Fallback: route to the first target when direct response is disallowed
                detectedRoute = _config.Targets.Keys.First();
                context.Logger.LogInformation("Router did not route but AllowDirectResponse=false - falling back to '{Target}'", detectedRoute);
            }
            else
            {
                // No routing - direct response: yield everything the router produced
                foreach (var buffered in preTextBuffer) yield return buffered;
                yield break;
            }
        }

        // Routing occurred - discard buffered router text (suppressing any preamble the model wrote)

        if (!_config.Targets.TryGetValue(detectedRoute, out var targetAgentId))
        {
            context.Logger.LogWarning("Router target '{Target}' not in allowed targets", detectedRoute);
            foreach (var buffered in preTextBuffer) yield return buffered;
            yield break;
        }

        var targetAgent = await ExecutionStrategyAgentLoader.ResolveAgentAsync(targetAgentId, context, ct);
        if (targetAgent == null)
        {
            context.Logger.LogWarning("Router target agent '{Target}' (ID: {AgentId}) not found or disabled", detectedRoute, targetAgentId);
            foreach (var buffered in preTextBuffer) yield return buffered;
            yield break;
        }

        yield return new AgentStreamChunk { AgentName = targetAgent.Name };

        var delegatedMessages = BuildDelegatedMessages(messages, agent.Name, targetAgent.Name, null);
        await foreach (var chunk in targetAgent.ExecuteStreamingAsync(delegatedMessages, ct).WithCancellation(ct))
        {
            yield return chunk;
        }
    }

    internal static IAgentExecutor InjectRouteTool(IAgentExecutor agent, IEnumerable<string> availableTargets, Action<string>? onRoute = null)
    {
        var targetList = availableTargets.ToList();
        var targetDescription = targetList.Count > 0
            ? $"Available route targets: {string.Join(", ", targetList)}"
            : "Select the best target agent to handle the request.";

        var routeTool = AIFunctionFactory.Create(
            (string targetAgentName, string? reason) =>
            {
                onRoute?.Invoke(targetAgentName);
                return $"ROUTE:{targetAgentName}";
            },
            new AIFunctionFactoryOptions
            {
                Name = "route_to_agent",
                Description = $"Route this request to the most suitable specialist agent. {targetDescription}"
            });

        return agent.WithAdditionalTools([routeTool]);
    }

    internal static string? ExtractRouteTarget(AgentResponse response)
    {
        foreach (var msg in response.Messages ?? [])
        {
            foreach (var content in msg.Contents)
            {
                if (content is FunctionCallContent fcc
                    && string.Equals(fcc.Name, "route_to_agent", StringComparison.OrdinalIgnoreCase)
                    && fcc.Arguments?.TryGetValue("targetAgentName", out var targetArg) == true)
                {
                    var targetName = targetArg?.ToString();
                    if (!string.IsNullOrWhiteSpace(targetName))
                    {
                        return targetName;
                    }
                }

                if (content is FunctionResultContent frc
                    && frc.Result is string resultText
                    && resultText.StartsWith("ROUTE:", StringComparison.OrdinalIgnoreCase))
                {
                    return resultText["ROUTE:".Length..].Trim();
                }
            }
        }

        return null;
    }

    private static List<ChatMessage> BuildDelegatedMessages(List<ChatMessage> messages, string routerName, string targetAgentName, string? routerResponse)
    {
        var delegatedMessages = new List<ChatMessage>(messages)
        {
            new(ChatRole.System,
                $"[Routed by {routerName}] The request has been assigned to '{targetAgentName}'. " +
                $"Answer the user's request directly using the conversation above. " +
                $"{(string.IsNullOrWhiteSpace(routerResponse) ? string.Empty : $"Router context: {routerResponse}")}")
        };

        return delegatedMessages;
    }

    private static ExecutionResult BuildResult(AgentResponse response, List<string> path, string finalAgentName, int inputTokens, int outputTokens)
    {
        return new ExecutionResult
        {
            Response = response,
            HandoffPath = path,
            FinalAgentName = finalAgentName,
            AggregatedUsage = ExecutionStrategyAgentLoader.BuildUsage(inputTokens, outputTokens)
        };
    }
}
