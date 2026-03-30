namespace Tnzi.AI.Dtos;

/// <summary>
/// AgentArtifact 输出 DTO
/// </summary>
public class AgentArtifactDto
{
    /// <summary>产物 ID</summary>
    public Guid Id { get; set; }

    /// <summary>关联的运行 ID</summary>
    public Guid RunId { get; set; }

    /// <summary>关联的线程 ID</summary>
    public Guid ThreadId { get; set; }

    /// <summary>虚拟路径</summary>
    public string VirtualPath { get; set; } = string.Empty;

    /// <summary>文件名</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>MIME 类型</summary>
    public string? ContentType { get; set; }

    /// <summary>文件大小（字节）</summary>
    public long? Size { get; set; }

    /// <summary>创建时间</summary>
    public DateTime CreationTime { get; set; }
}
