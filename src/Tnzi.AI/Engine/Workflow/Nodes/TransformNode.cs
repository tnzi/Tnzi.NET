namespace Tnzi.AI.Engine.Workflow.Nodes;

/// <summary>
/// 转换节点 — 纯代码数据转换（不使用 LLM），如 JSON 提取、文本分割、合并等
/// </summary>
/// <remarks>
/// 配置项：
/// - transformType: 转换类型，支持：
///   - json_extract: 从 JSON 中提取字段（配合 jsonPath 配置）
///   - text_split: 按分隔符分割文本（配合 delimiter 配置）
///   - merge: 合并所有上游输出（配合 separator 配置）
///   - template: 模板渲染（配合 template 配置，支持 {{stepId}} 变量）
///   - regex_extract: 正则提取（配合 pattern 配置）
///   - uppercase/lowercase: 大小写转换
/// - jsonPath: JSON 提取路径（如 "result.summary"）
/// - delimiter: 分割符（默认 "\n"）
/// - separator: 合并分隔符（默认 "\n\n"）
/// - template: 模板字符串
/// - pattern: 正则表达式
/// </remarks>
public class TransformNode : IWorkflowNode
{
    private readonly ILogger<TransformNode> _logger;

    public string NodeType => "transform";

    public TransformNode(IServiceProvider serviceProvider, ILogger<TransformNode> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
    {
        var step = context.Step;
        var state = context.State;
        var config = step.Configuration ?? new Dictionary<string, string>();

        var transformType = config.TryGetValue("transformType", out var tt) ? tt : "merge";
        var inputText = CollectInput(context);

        try
        {
            var result = transformType.ToLowerInvariant() switch
            {
                "json_extract" => JsonExtract(inputText, config),
                "text_split" => TextSplit(inputText, config),
                "merge" => Merge(context, config),
                "template" => TemplateRender(state, config),
                "regex_extract" => RegexExtract(inputText, config),
                "uppercase" => inputText.ToUpperInvariant(),
                "lowercase" => inputText.ToLowerInvariant(),
                _ => inputText
            };

            return Task.FromResult(new WorkflowNodeResult
            {
                Output = new WorkflowStepOutput
                {
                    Text = result,
                    Metadata = new Dictionary<string, string>
                    {
                        ["transform_type"] = transformType
                    }
                },
                IsSuccess = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Transform node '{StepId}' failed with type '{Type}'", step.StepId, transformType);
            return Task.FromResult(new WorkflowNodeResult
            {
                Output = $"[Transform error: {ex.Message}]",
                IsSuccess = false,
                Error = ex.Message
            });
        }
    }

    /// <summary>
    /// JSON 字段提取
    /// </summary>
    private static string JsonExtract(string input, Dictionary<string, string> config)
    {
        var jsonPath = config.TryGetValue("jsonPath", out var jp) ? jp : string.Empty;
        if (string.IsNullOrWhiteSpace(jsonPath))
            return input;

        using var doc = JsonDocument.Parse(input);
        var current = doc.RootElement;

        // 简单路径解析（如 "result.summary"）
        foreach (var segment in jsonPath.Split('.'))
        {
            if (current.TryGetProperty(segment, out var next))
                current = next;
            else
                return string.Empty;
        }

        return current.ValueKind == JsonValueKind.String
            ? current.GetString() ?? string.Empty
            : current.GetRawText();
    }

    /// <summary>
    /// 文本分割
    /// </summary>
    private static string TextSplit(string input, Dictionary<string, string> config)
    {
        var delimiter = config.TryGetValue("delimiter", out var d) ? d : "\n";
        var parts = input.Split(delimiter, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // 返回 JSON 数组格式
        return JsonSerializer.Serialize(parts, TnziJsonDefaults.Options);
    }

    /// <summary>
    /// 合并所有上游输出
    /// </summary>
    private static string Merge(WorkflowNodeContext context, Dictionary<string, string> config)
    {
        var separator = config.TryGetValue("separator", out var sep) ? sep : "\n\n";

        if (context.DependencyOutputs.Count == 0)
            return context.State.InitialInput;

        return string.Join(separator, context.DependencyOutputs.Values.Select(o => o.Text));
    }

    /// <summary>
    /// 模板渲染
    /// </summary>
    private static string TemplateRender(WorkflowState state, Dictionary<string, string> config)
    {
        var template = config.TryGetValue("template", out var t) ? t : string.Empty;
        return state.ResolveTemplate(template);
    }

    /// <summary>
    /// 正则提取
    /// </summary>
    private static string RegexExtract(string input, Dictionary<string, string> config)
    {
        var pattern = config.TryGetValue("pattern", out var p) ? p : string.Empty;
        if (string.IsNullOrWhiteSpace(pattern))
            return input;

        var match = Regex.Match(input, pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
        if (!match.Success)
            return string.Empty;

        // 如果有捕获组，返回第一个捕获组；否则返回整个匹配
        return match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
    }

    /// <summary>
    /// 收集输入
    /// </summary>
    private static string CollectInput(WorkflowNodeContext context)
    {
        if (context.DependencyOutputs.Count == 0)
            return context.State.InitialInput;

        if (context.DependencyOutputs.Count == 1)
            return context.DependencyOutputs.Values.First().Text;

        var sb = new StringBuilder();
        foreach (var (depId, output) in context.DependencyOutputs)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine($"[{depId}]");
            sb.AppendLine(output.Text);
        }

        return sb.ToString().TrimEnd();
    }
}
