
namespace Tnzi.AI.Infrastructure;

/// <summary>
/// AI 模块 ActivitySource - 用于 OpenTelemetry 追踪
/// </summary>
/// <remarks>
/// <para>
/// 遵循 OpenTelemetry Semantic Conventions for Generative AI systems。
/// 参考: https://opentelemetry.io/docs/specs/semconv/gen-ai/
/// </para>
/// </remarks>
public static class AIActivitySource
{
    /// <summary>
    /// ActivitySource 名称
    /// </summary>
    public const string Name = "Tnzi.AI";

    /// <summary>
    /// 版本号
    /// </summary>
    public const string Version = "1.0.0";

    /// <summary>
    /// ActivitySource 实例
    /// </summary>
    public static readonly ActivitySource Source = new(Name, Version);

    /// <summary>
    /// Meter 实例（用于指标）
    /// </summary>
    public static readonly Meter Meter = new(Name, Version);

    // 定义指标
    private static readonly Counter<long> _chatRequestCounter;
    private static readonly Counter<long> _chatTokenCounter;
    private static readonly Histogram<double> _chatLatencyHistogram;
    private static readonly Counter<long> _toolCallCounter;
    private static readonly Histogram<double> _toolCallDurationHistogram;
    private static readonly Counter<long> _errorCounter;

    static AIActivitySource()
    {
        // 聊天请求计数器
        _chatRequestCounter = Meter.CreateCounter<long>(
            "gen_ai.client.operation.count",
            unit: "{request}",
            description: "Number of AI chat operations");

        // Token 使用计数器
        _chatTokenCounter = Meter.CreateCounter<long>(
            "gen_ai.client.token.usage",
            unit: "{token}",
            description: "Number of tokens used in AI operations");

        // 聊天延迟直方图
        _chatLatencyHistogram = Meter.CreateHistogram<double>(
            "gen_ai.client.operation.duration",
            unit: "s",
            description: "Duration of AI chat operations");

        // 工具调用计数器
        _toolCallCounter = Meter.CreateCounter<long>(
            "gen_ai.client.tool.call.count",
            unit: "{call}",
            description: "Number of tool calls made by AI");

        // 工具调用耗时直方图
        _toolCallDurationHistogram = Meter.CreateHistogram<double>(
            "gen_ai.client.tool.call.duration",
            unit: "s",
            description: "Duration of individual tool calls");

        // 错误计数器
        _errorCounter = Meter.CreateCounter<long>(
            "gen_ai.client.error.count",
            unit: "{error}",
            description: "Number of errors in AI operations");
    }

    /// <summary>
    /// 记录聊天请求
    /// </summary>
    public static void RecordChatRequest(string provider, string? model, string operationType = "chat")
    {
        operationType = NormalizeOperationName(operationType);
        _chatRequestCounter.Add(1,
            new KeyValuePair<string, object?>(SemanticTags.ProviderName, provider),
            new KeyValuePair<string, object?>(SemanticTags.ModelId, model),
            new KeyValuePair<string, object?>(SemanticTags.OperationName, operationType));
    }

    /// <summary>
    /// 记录 Token 使用
    /// </summary>
    public static void RecordTokenUsage(
        string provider,
        string? model,
        int inputTokens,
        int outputTokens,
        string operationType = "chat")
    {
        operationType = NormalizeOperationName(operationType);
        var tags = new[]
        {
            new KeyValuePair<string, object?>(SemanticTags.ProviderName, provider),
            new KeyValuePair<string, object?>(SemanticTags.ModelId, model),
            new KeyValuePair<string, object?>(SemanticTags.OperationName, operationType)
        };

        // 输入 Token
        _chatTokenCounter.Add(inputTokens,
            tags.Append(new KeyValuePair<string, object?>(SemanticTags.TokenType, "input")).ToArray());

        // 输出 Token
        _chatTokenCounter.Add(outputTokens,
            tags.Append(new KeyValuePair<string, object?>(SemanticTags.TokenType, "output")).ToArray());
    }

    /// <summary>
    /// 记录聊天延迟
    /// </summary>
    public static void RecordChatLatency(string provider, string? model, double durationSeconds, string operationType = "chat")
    {
        operationType = NormalizeOperationName(operationType);
        _chatLatencyHistogram.Record(durationSeconds,
            new KeyValuePair<string, object?>(SemanticTags.ProviderName, provider),
            new KeyValuePair<string, object?>(SemanticTags.ModelId, model),
            new KeyValuePair<string, object?>(SemanticTags.OperationName, operationType));
    }

    private static string NormalizeOperationName(string? operationType)
    {
        return operationType switch
        {
            null or "" => "chat",
            AIOperationType.Chat => "chat",
            AIOperationType.ChatStreaming => "chat_streaming",
            AIOperationType.AgentRun => "agent_run",
            AIOperationType.AgentRunStreaming => "agent_run_streaming",
            AIOperationType.WorkflowRun => "workflow_run",
            AIOperationType.WorkflowRunStreaming => "workflow_run_streaming",
            _ => operationType
        };
    }

    /// <summary>
    /// 记录工具调用（增强版：含耗时、参数摘要、结果摘要）
    /// </summary>
    public static void RecordToolCallDetailed(
        string toolName,
        double durationSeconds,
        string? toolGroup = null,
        string? argumentSummary = null,
        string? resultSummary = null,
        bool isSuccess = true)
    {
        _toolCallCounter.Add(1,
            new KeyValuePair<string, object?>(SemanticTags.ToolName, toolName),
            new KeyValuePair<string, object?>(SemanticTags.ToolGroup, toolGroup));

        _toolCallDurationHistogram.Record(durationSeconds,
            new KeyValuePair<string, object?>(SemanticTags.ToolName, toolName),
            new KeyValuePair<string, object?>(SemanticTags.ToolGroup, toolGroup));
    }

    /// <summary>
    /// 记录错误
    /// </summary>
    public static void RecordError(string provider, string? model, string errorType)
    {
        _errorCounter.Add(1,
            new KeyValuePair<string, object?>(SemanticTags.ProviderName, provider),
            new KeyValuePair<string, object?>(SemanticTags.ModelId, model),
            new KeyValuePair<string, object?>(SemanticTags.ErrorType, errorType));
    }

    /// <summary>
    /// 开始聊天 Activity
    /// </summary>
    public static Activity? StartChatActivity(
        string provider,
        string? model,
        string operationName = "chat")
    {
        var activity = Source.StartActivity(
            $"gen_ai.{operationName}",
            ActivityKind.Client);

        if (activity != null)
        {
            activity.SetTag(SemanticTags.OperationName, operationName);
            activity.SetTag(SemanticTags.ProviderName, provider);

            if (!string.IsNullOrEmpty(model))
            {
                activity.SetTag(SemanticTags.ModelId, model);
            }
        }

        return activity;
    }

    /// <summary>
    /// 开始 Agent 调用 Activity
    /// </summary>
    public static Activity? StartAgentActivity(
        string agentId,
        string? agentName,
        string provider)
    {
        var displayName = string.IsNullOrWhiteSpace(agentName)
            ? $"invoke_agent {agentId}"
            : $"invoke_agent {agentName}({agentId})";

        var activity = Source.StartActivity(displayName, ActivityKind.Client);

        if (activity != null)
        {
            activity.SetTag(SemanticTags.OperationName, "invoke_agent");
            activity.SetTag(SemanticTags.AgentId, agentId);
            activity.SetTag(SemanticTags.ProviderName, provider);

            if (!string.IsNullOrWhiteSpace(agentName))
            {
                activity.SetTag(SemanticTags.AgentName, agentName);
            }
        }

        return activity;
    }

    /// <summary>
    /// 开始工具调用 Activity
    /// </summary>
    public static Activity? StartToolActivity(
        string toolName,
        string? toolGroup = null,
        string? argumentSummary = null)
    {
        var activity = Source.StartActivity(
            $"tool.{toolName}",
            ActivityKind.Internal);

        if (activity != null)
        {
            activity.SetTag(SemanticTags.ToolName, toolName);

            if (!string.IsNullOrEmpty(toolGroup))
            {
                activity.SetTag(SemanticTags.ToolGroup, toolGroup);
            }

            if (!string.IsNullOrEmpty(argumentSummary))
            {
                activity.SetTag(SemanticTags.ToolArguments, argumentSummary);
            }
        }

        return activity;
    }

    /// <summary>
    /// 完成 Activity 并记录结果
    /// </summary>
    public static void CompleteActivity(
        Activity? activity,
        int? inputTokens = null,
        int? outputTokens = null,
        string? finishReason = null)
    {
        if (activity == null) return;

        if (inputTokens.HasValue)
        {
            activity.SetTag(SemanticTags.InputTokens, inputTokens.Value);
        }

        if (outputTokens.HasValue)
        {
            activity.SetTag(SemanticTags.OutputTokens, outputTokens.Value);
        }

        if (!string.IsNullOrEmpty(finishReason))
        {
            activity.SetTag(SemanticTags.FinishReason, finishReason);
        }

        activity.SetStatus(ActivityStatusCode.Ok);
    }

    /// <summary>
    /// 完成工具调用 Activity 并记录结果
    /// </summary>
    public static void CompleteToolActivity(
        Activity? activity,
        double durationSeconds,
        string? resultSummary = null,
        bool isSuccess = true)
    {
        if (activity == null) return;

        activity.SetTag(SemanticTags.ToolDuration, durationSeconds);

        if (!string.IsNullOrEmpty(resultSummary))
        {
            activity.SetTag(SemanticTags.ToolResult, resultSummary);
        }

        activity.SetStatus(isSuccess ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
    }

    /// <summary>
    /// 记录 Activity 错误
    /// </summary>
    public static void RecordActivityError(Activity? activity, Exception exception)
    {
        if (activity == null) return;

        activity.SetTag(SemanticTags.ErrorType, exception.GetType().FullName);
        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
        {
            ["exception.type"] = exception.GetType().FullName,
            ["exception.message"] = exception.Message,
            ["exception.stacktrace"] = exception.StackTrace
        }));
    }
}

/// <summary>
/// OpenTelemetry 语义标签常量
/// </summary>
/// <remarks>
/// 遵循 OpenTelemetry Semantic Conventions for Generative AI systems
/// </remarks>
public static class SemanticTags
{
    // 操作相关
    public const string OperationName = "gen_ai.operation.name";

    // 提供商/模型相关
    public const string ProviderName = "gen_ai.system";
    public const string ModelId = "gen_ai.request.model";

    // Agent 相关
    public const string AgentId = "gen_ai.agent.id";
    public const string AgentName = "gen_ai.agent.name";
    public const string AgentDescription = "gen_ai.agent.description";

    // Token 相关
    public const string InputTokens = "gen_ai.usage.input_tokens";
    public const string OutputTokens = "gen_ai.usage.output_tokens";
    public const string TokenType = "gen_ai.token.type";

    // 响应相关
    public const string FinishReason = "gen_ai.response.finish_reasons";

    // 工具相关
    public const string ToolName = "gen_ai.tool.name";
    public const string ToolGroup = "gen_ai.tool.group";

    // 工具详细追踪
    public const string ToolArguments = "gen_ai.tool.arguments";
    public const string ToolResult = "gen_ai.tool.result";
    public const string ToolDuration = "gen_ai.tool.duration";

    // 错误相关
    public const string ErrorType = "error.type";
}
