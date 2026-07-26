namespace Tnzi.Finance.Dtos;

/// <summary>
/// 单据附件 DTO
/// </summary>
public class DocumentAttachmentDto
{
    public Guid Id { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;

    /// <summary>Storage 文件 Id（下载/预览按它取活文件）</summary>
    public Guid FileId { get; set; }

    /// <summary>附加时的文件名快照</summary>
    public string FileName { get; set; } = string.Empty;

    public string? ContentType { get; set; }
    public long FileSize { get; set; }
    public string? Caption { get; set; }

    /// <summary>谁挂的</summary>
    public Guid? CreatorId { get; set; }

    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 挂附件请求（文件已由前端经 Storage 上传，这里只登记链接）
/// </summary>
public class CreateDocumentAttachmentDto
{
    public Guid FileId { get; set; }

    /// <summary>文件名（前端从上传结果带过来；留空则退回文件 Id 的短串）</summary>
    public string? FileName { get; set; }

    public string? ContentType { get; set; }
    public long FileSize { get; set; }
    public string? Caption { get; set; }
}

/// <summary>
/// 单据讨论 DTO
/// </summary>
public class DocumentCommentDto
{
    public Guid Id { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    /// <summary>作者</summary>
    public Guid? CreatorId { get; set; }

    /// <summary>作者显示名（由消费应用的用户目录解析；框架侧可能为空）</summary>
    public string? CreatorName { get; set; }

    public DateTime CreationTime { get; set; }

    /// <summary>当前用户能否删除这一条（作者本人或持删除权限）</summary>
    public bool CanDelete { get; set; }
}

/// <summary>
/// 发一条讨论
/// </summary>
public class CreateDocumentCommentDto
{
    public string Body { get; set; } = string.Empty;
}
