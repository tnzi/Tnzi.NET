namespace Tnzi.Storage.Dtos;

/// <summary>
/// 文件版本对外响应 DTO。
/// FileVersion 实体的安全子集，用于所有对外返回文件版本的 API 端点，
/// 绝不暴露内部字段：TenantId（租户隔离）、Path（版本文件的原始存储位置）、
/// Md5Hash（内部完整性哈希，仅供服务端去重/校验）。
/// 客户端通过 files/{id}/versions/{version}/download 端点访问版本内容，无需原始存储路径。
/// </summary>
public class FileVersionDto
{
    /// <summary>
    /// 版本记录 ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 所属文件 ID（关联 FileRecord）
    /// </summary>
    public Guid FileId { get; set; }

    /// <summary>
    /// 版本号（从 1 开始递增）
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// 版本文件大小（字节）
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// 版本描述（可选）
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 是否为当前版本
    /// </summary>
    public bool IsCurrent { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; set; }

    /// <summary>
    /// 创建者 ID
    /// </summary>
    public Guid? CreatorId { get; set; }
}
