namespace Tnzi.AI.Middleware;

/// <summary>
/// 澄清拦截中间件（Order = 999, MUST be last）
/// 检测 ask_clarification 工具调用，中断执行并返回 RequiresClarification 状态
/// </summary>
public class ClarificationMiddleware : IAiMiddleware
{
    private readonly IAgentExecutionContextAccessor _contextAccessor;
    private readonly ILogger<ClarificationMiddleware> _logger;

    public int Order => AiMiddlewareOrders.Clarification;

    public ClarificationMiddleware(IAgentExecutionContextAccessor contextAccessor, ILogger<ClarificationMiddleware> logger)
    {
        _contextAccessor = Check.NotNull(contextAccessor);
        _logger = Check.NotNull(logger);
    }

    public async Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        if (context.ShouldSkipMiddleware)
            return await next(context, cancellationToken);

        var result = await next(context, cancellationToken);

        // 检查工具是否写入了澄清请求
        var request = GetClarificationRequest();
        if (request != null)
        {
            var formattedQuestion = FormatClarificationMessage(request);

            _logger.LogInformation("Clarification requested: type={Type}, question={Question}",
                request.Type, request.Question);

            return result.CloneWith(
                response: formattedQuestion,
                finishReason: FinishReasons.RequiresClarification,
                status: AgentRunStatus.RequiresClarification,
                clarificationQuestion: formattedQuestion);
        }

        return result;
    }

    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (context.ShouldSkipMiddleware)
        {
            await foreach (var chunk in next(context, cancellationToken))
                yield return chunk;
            yield break;
        }

        await foreach (var chunk in next(context, cancellationToken))
        {
            yield return chunk;
        }

        // 流式模式下在最后检查澄清请求
        var request = GetClarificationRequest();
        if (request != null)
        {
            var formattedQuestion = FormatClarificationMessage(request);

            yield return new AgentStreamChunk
            {
                Text = formattedQuestion,
                FinishReason = FinishReasons.RequiresClarification,
                EventType = MiddlewareEventTypes.Clarification,
                EventData = new Dictionary<string, object>
                {
                    ["type"] = request.Type.ToString(),
                    ["options"] = request.Options ?? []
                }
            };
        }
    }

    /// <summary>
    /// 从 accessor Properties 中获取澄清请求
    /// </summary>
    private ClarificationRequest? GetClarificationRequest()
    {
        if (_contextAccessor.Properties.TryGetValue(ContextPropertyKeys.ClarificationRequest, out var reqObj)
            && reqObj is ClarificationRequest request)
        {
            // 读取后清除，避免重复处理
            _contextAccessor.Properties.Remove(ContextPropertyKeys.ClarificationRequest);
            return request;
        }
        return null;
    }

    /// <summary>
    /// 格式化澄清消息：类型图标 + 上下文 + 编号选项
    /// </summary>
    internal static string FormatClarificationMessage(ClarificationRequest request)
    {
        var isChinese = ContainsChinese(request.Question);
        var typeIcon = GetTypeIcon(request.Type);
        var sb = new StringBuilder();

        sb.AppendLine($"{typeIcon} {request.Question}");

        if (!string.IsNullOrWhiteSpace(request.Context))
        {
            sb.AppendLine();
            sb.AppendLine(request.Context);
        }

        if (request.Options is { Count: > 0 })
        {
            sb.AppendLine();
            var label = isChinese ? "选项：" : "Options:";
            sb.AppendLine(label);
            for (var i = 0; i < request.Options.Count; i++)
            {
                sb.AppendLine($"  {i + 1}. {request.Options[i]}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string GetTypeIcon(ClarificationType type) => type switch
    {
        ClarificationType.MissingInfo => "❓",
        ClarificationType.AmbiguousRequirement => "🔍",
        ClarificationType.ApproachChoice => "🔀",
        ClarificationType.RiskConfirmation => "⚠️",
        ClarificationType.Suggestion => "💡",
        _ => "❓"
    };

    private static bool ContainsChinese(string text)
        => AiTextHelper.ContainsChinese(text);
}
