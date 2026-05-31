namespace Tnzi.AI.Engine.Workflow;

/// <summary>
/// 工作流状态 — 管理步骤间的输入/输出传递
/// </summary>
/// <remarks>
/// 输出以 <see cref="WorkflowStepOutput"/>（文本 + 元数据）存储，
/// 同时暴露字符串兼容 API —— <see cref="SetOutput(string, string)"/> 依赖
/// string 到 <see cref="WorkflowStepOutput"/> 的隐式转换。
/// </remarks>
public partial class WorkflowState
{
    private readonly ConcurrentDictionary<string, WorkflowStepOutput> _outputs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>工作流初始输入</summary>
    public string InitialInput { get; }

    public WorkflowState(string initialInput)
    {
        InitialInput = Check.NotNullOrEmpty(initialInput);
    }

    /// <summary>设置步骤输出（WorkflowStepOutput）</summary>
    public void SetOutput(string stepId, WorkflowStepOutput output) => _outputs[stepId] = output;

    /// <summary>设置步骤输出（string，向后兼容）</summary>
    public void SetOutput(string stepId, string output) => _outputs[stepId] = output;

    /// <summary>获取步骤输出（WorkflowStepOutput）</summary>
    public WorkflowStepOutput? GetOutput(string stepId) => _outputs.GetValueOrDefault(stepId);

    /// <summary>获取步骤输出文本（向后兼容）</summary>
    public string? GetOutputText(string stepId) => _outputs.GetValueOrDefault(stepId)?.Text;

    /// <summary>
    /// 将所有步骤输出导出为 Dictionary（用于检查点序列化，向后兼容 string 格式）
    /// </summary>
    public Dictionary<string, WorkflowStepOutput> ToDictionary()
    {
        var dict = new Dictionary<string, WorkflowStepOutput>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in _outputs)
        {
            dict[key] = value;
        }

        return dict;
    }

    /// <summary>
    /// 从检查点恢复 WorkflowState
    /// </summary>
    /// <param name="checkpoint">检查点数据</param>
    /// <returns>恢复后的 WorkflowState（含已完成步骤的输出）</returns>
    public static WorkflowState FromCheckpoint(WorkflowCheckpoint checkpoint)
    {
        Check.NotNull(checkpoint);

        var state = new WorkflowState(checkpoint.InitialInput);
        foreach (var (stepId, output) in checkpoint.StepOutputs)
        {
            state.SetOutput(stepId, output);
        }

        return state;
    }

    /// <summary>
    /// 解析模板变量：将 {{stepId}} 替换为对应步骤的输出
    /// </summary>
    public string ResolveTemplate(string template)
    {
        if (string.IsNullOrWhiteSpace(template)) return template;

        return TemplateVariableRegex().Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            if (key.Equals("input", StringComparison.OrdinalIgnoreCase))
                return InitialInput;
            var output = _outputs.GetValueOrDefault(key);
            return output?.Text ?? match.Value;
        });
    }

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex TemplateVariableRegex();
}
