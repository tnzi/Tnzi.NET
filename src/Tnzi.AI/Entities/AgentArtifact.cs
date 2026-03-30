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
