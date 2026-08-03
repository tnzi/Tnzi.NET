namespace Tnzi.AI.Cli.Entities;

/// <summary>
/// 外部执行的一条归一化事件。
/// </summary>
/// <remarks>
/// 用 <see cref="CreationAuditedEntity{TKey}"/> —— 只追加，不修改，不软删。
/// 事件流是既成事实，改一条已发生的事件没有任何合法语义；软删列只会白白拖慢
/// 长会话的补发查询。
/// </remarks>
public class CliRunMessage : CreationAuditedEntity<Guid>, IMultiTenant
{
    /// <summary>租户。</summary>
    public Guid? TenantId { get; set; }

    /// <summary>所属运行。</summary>
    public Guid RunId { get; set; }

    /// <summary>运行内单调递增序号。断线重连按它补发。</summary>
    public int Sequence { get; set; }

    /// <summary>事件类型。</summary>
    public CliAgentEventType Type { get; set; }

    /// <summary>文本内容。</summary>
    public string? Content { get; set; }

    /// <summary>工具名。</summary>
    public string? Tool { get; set; }

    /// <summary>工具调用 ID。</summary>
    public string? CallId { get; set; }

    /// <summary>工具入参 JSON。</summary>
    public string? InputJson { get; set; }

    /// <summary>工具输出，超长截断。</summary>
    public string? Output { get; set; }

    /// <summary>状态标识（Status 事件）。</summary>
    public string? Status { get; set; }

    /// <summary>日志级别（Log 事件）。</summary>
    public string? Level { get; set; }
}
