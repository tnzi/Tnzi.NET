namespace Tnzi.AI.Workflow;

/// <summary>
/// Workflow 步骤输出 - 文本 + 结构化元数据。
/// 解决纯 string 无法承载 verdict/route/结构化数据的问题，
/// 同时保持序列化简单性（不引入 object? 或 JsonElement?）。
/// </summary>
/// <remarks>
/// 演进说明：
/// 当前 WorkflowStepOutput（Text + Metadata&lt;string,string&gt;）适用于以文本为主、带少量结构化元数据的场景。
/// 如果后续节点间需要传递更复杂的结构化数据（review verdict 对象、工具返回的 typed payload、
/// 多模态结果等），可演进为 Text + JsonData(JsonElement?) + Metadata 三字段模型。
/// 届时 JsonData 承载强类型序列化数据，Text 保持人类可读摘要，Metadata 保持轻量键值对。
/// 隐式 string 转换和 Checkpoint 序列化兼容性不受影响。
/// </remarks>
public class WorkflowStepOutput
{
    /// <summary>主输出文本（LLM 生成的内容）</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>结构化元数据（verdict、route、score 等键值对）</summary>
    public Dictionary<string, string>? Metadata { get; init; }

    /// <summary>隐式转换：兼容纯 string 场景</summary>
    public static implicit operator WorkflowStepOutput(string text) => new() { Text = text };

    /// <inheritdoc/>
    public override string ToString() => Text;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj switch
    {
        WorkflowStepOutput other => string.Equals(Text, other.Text, StringComparison.Ordinal),
        string s => string.Equals(Text, s, StringComparison.Ordinal),
        _ => false
    };

    /// <inheritdoc/>
    public override int GetHashCode() => Text.GetHashCode(StringComparison.Ordinal);
}
