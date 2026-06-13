namespace Tnzi.AI.Entities;

/// <summary>
/// Agent 运行产出物实体
/// </summary>
public class AgentArtifact : CreationAuditedEntity<Guid>, IMultiTenant
{
    /// <summary>关联的运行 ID</summary>
    public Guid RunId { get; set; }

    /// <summary>关联的线程 ID</summary>
    public Guid ThreadId { get; set; }

    /// <summary>虚拟路径（统一标准化后的路径）</summary>
    /// <remarks>
    /// 这是 sandbox/虚拟文件系统的路径字符串，<b>不是</b> Storage 文件记录的 Guid，
    /// 因此<b>有意</b>不标记 <c>[FileField]</c>：文件引用追踪器（FileReferenceChangeTracker）
    /// 只解析 Guid/Guid 列表，对路径字符串是静默 no-op。Artifact 仅记录运行产物元数据，
    /// 不持有 Storage 文件引用，无需参与文件清理。
    /// </remarks>
    public string VirtualPath { get; set; } = string.Empty;

    /// <summary>文件名</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>内容类型（MIME）</summary>
    public string? ContentType { get; set; }

    /// <summary>文件大小（字节）</summary>
    public long? Size { get; set; }

    /// <summary>租户 ID</summary>
    public Guid? TenantId { get; set; }
}
