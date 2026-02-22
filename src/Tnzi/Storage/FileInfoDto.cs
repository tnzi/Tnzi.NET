namespace Tnzi.Storage;

/// <summary>
/// 文件信息 DTO
/// 通用的文件信息结构，用于跨模块传递文件信息
/// </summary>
public class FileInfoDto
{
    /// <summary>
    /// 文件ID
    /// </summary>
    public Guid? FileId { get; set; }

    /// <summary>
    /// 文件名
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 文件路径或URL
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// 文件大小（字节）
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// MIME类型
    /// </summary>
    public string ContentType { get; set; } = "application/octet-stream";
}

