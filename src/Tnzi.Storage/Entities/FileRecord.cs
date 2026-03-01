namespace Tnzi.Storage.Entities;

public class FileRecord : EntityBase<Guid>, IHasCreationTime, IHasCreator
{
    public string FileName { get; set; } = string.Empty;

    public string OriginalName { get; set; } = string.Empty;

    public string Extension { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long Size { get; set; } // Bytes

    public string? Path { get; set; } // Relative path or URL

    public string? Md5Hash { get; set; } // MD5 哈希，用于去重

    public string Provider { get; set; } = "Local"; // Local, S3, Azure

    public int ReferenceCount { get; set; } = 1; // 引用此文件的引用数量

    // 是否为临时文件
    public bool IsTemporary { get; set; }

    // 图片缩略图路径
    public string? ThumbnailPath { get; set; }

    /// <summary>
    /// Custom tags (comma-separated, stored as single string for DB portability)
    /// </summary>
    public string? Tags { get; set; }

    // Audit info
    public DateTime CreationTime { get; set; }
    public Guid? CreatorId { get; set; }

    /// <summary>
    /// Get tags as a list
    /// </summary>
    public List<string> GetTagsList()
    {
        if (string.IsNullOrWhiteSpace(Tags))
            return new List<string>();
        return Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    /// <summary>
    /// Set tags from a list
    /// </summary>
    public void SetTagsList(List<string>? tags)
    {
        Tags = tags == null || tags.Count == 0
            ? null
            : string.Join(",", tags.Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).Distinct());
    }
}
