namespace Tnzi.AI.Metadata;

/// <summary>
/// <c>AIFunction.AdditionalProperties</c> 上由框架写入、供权限评估读取的工具元数据键。
/// </summary>
/// <remarks>
/// <c>[AIFunction(IsDestructive = true)]</c> 这类安全标记原本只停在 <c>ToolDefinition</c> 上（执行器据此决定并行 / 串行），
/// 从没进过 <c>ToolPermissionContext</c>：评估器「破坏性工具无显式 allow 即拒绝」的默认与 <c>IsDestructiveOnly</c> 规则
/// 只对 shell 命令分析出的破坏性片段生效，<c>delete_memory</c> 这类属性标记的 C# 工具零规则下直接执行，
/// 而管理端 evaluate 端点（IsDestructive 由请求体给）对同一个工具答 Deny。<c>ToolAdapter</c> 现在把标记写到这里，
/// <c>ApprovalToolWrapper</c> 读出来与 shell 分析 OR 合并。
/// </remarks>
public static class ToolMetadataKeys
{
    /// <summary>bool：工具是否破坏性（属性 <c>IsDestructive</c>；MCP 工具另由包装器读 <c>destructiveHint</c>，只认显式 true）</summary>
    public const string Destructive = "tnzi.destructive";

    /// <summary>
    /// bool：工具是否只读（属性 <c>IsReadOnly</c>）。★<b>目前只写不读</b>：<c>ToolAdapter</c> 写入，框架内没有任何读者，
    /// 权限上下文<b>不</b>据此放宽任何判定（只读性对执行器的意义在 <c>ToolDefinition.IsReadOnly</c> 上，那是并发判定）。
    /// 与 <c>SearchHint</c> / <c>Aliases</c> 同类的保留字段；要让它参与权限评估，先在 <c>ApprovalToolWrapper</c> 里加读者。
    /// </summary>
    public const string ReadOnly = "tnzi.readOnly";
}
