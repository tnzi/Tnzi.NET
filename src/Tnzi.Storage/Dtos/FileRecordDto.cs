namespace Tnzi.Storage.Dtos;

/// <summary>
/// 文件记录对外响应 DTO。
/// FileRecord 实体的安全子集，用于所有对外返回文件记录的 API 端点，
/// 绝不暴露内部字段：TenantId（租户隔离）、Path/ThumbnailPath（原始存储位置）、
/// Md5Hash（内部完整性哈希，由完整性校验端点单独提供）、Metadata（原始 JSON，由元数据端点单独提供）。
/// 客户端通过 files/{id}/download、preview、url 等端点访问文件内容，无需原始存储路径。
/// </summary>
public class FileRecordDto
{
    /// <summary>
    /// 文件 ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 存储文件名（系统生成）
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 原始文件名（上传时）
    /// </summary>
    public string OriginalName { get; set; } = string.Empty;

    /// <summary>
    /// 扩展名
    /// </summary>
    public string Extension { get; set; } = string.Empty;

    /// <summary>
    /// MIME 类型
    /// </summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// 文件大小（字节）
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// 存储提供商（Local / S3 / Azure 等）
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// 引用此文件的引用数量
    /// </summary>
    public int ReferenceCount { get; set; }

    /// <summary>
    /// 所属文件夹 ID（null 表示根目录 / 未归档）
    /// </summary>
    public Guid? FolderId { get; set; }

    /// <summary>
    /// 是否为临时文件
    /// </summary>
    public bool IsTemporary { get; set; }

    /// <summary>
    /// 自定义标签（逗号分隔的单一字符串，null / 空表示无标签）
    /// </summary>
    public string? Tags { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; set; }

    /// <summary>
    /// 创建者 ID
    /// </summary>
    public Guid? CreatorId { get; set; }
}
