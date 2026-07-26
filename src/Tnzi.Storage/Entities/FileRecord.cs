namespace Tnzi.Storage.Entities;

public class FileRecord : EntityBase<Guid>, IHasCreationTime, IHasCreator, IMultiTenant
{
    public Guid? TenantId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string OriginalName { get; set; } = string.Empty;

    public string Extension { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long Size { get; set; } // Bytes

    public string? Path { get; set; } // Relative path or URL

    public string? Md5Hash { get; set; } // MD5 哈希，用于去重

    public string Provider { get; set; } = "Local"; // Local, S3, Azure

    public int ReferenceCount { get; set; } = 1; // 引用此文件的引用数量

    /// <summary>
    /// Folder ID (null for root/unfiled)
    /// </summary>
    public Guid? FolderId { get; set; }

    // 是否为临时文件
    public bool IsTemporary { get; set; }

    /// <summary>
    /// 是否公开可读。true 时任何人(含未认证请求)都能读取该文件,用于头像、站点素材这类
    /// **有意**公开的资源。默认 false:文件只有创建者与持 `storage.file.view` 的管理员可读。
    ///
    /// 公开只影响读取,不影响变更——否则任何人都能删掉站点的共享素材。
    /// </summary>
    public bool IsPublic { get; set; }

    // 图片缩略图路径
    public string? ThumbnailPath { get; set; }

    /// <summary>
    /// Custom tags (comma-separated, stored as single string for DB portability)
    /// </summary>
    public string? Tags { get; set; }

    /// <summary>
    /// Arbitrary key-value metadata stored as JSON string (e.g., {"author":"John","department":"HR"})
    /// Use GetMetadata() / SetMetadata() helpers for typed access.
    /// </summary>
    public string? Metadata { get; set; }

    /// <summary>
    /// Get metadata as a dictionary
    /// </summary>
    public Dictionary<string, string> GetMetadata()
    {
        if (string.IsNullOrWhiteSpace(Metadata))
            return new Dictionary<string, string>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(Metadata) ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// Set metadata from a dictionary
    /// </summary>
    public void SetMetadata(Dictionary<string, string>? metadata)
    {
        Metadata = metadata == null || metadata.Count == 0
            ? null
            : JsonSerializer.Serialize(metadata, JsonSerializerOptions.Default);
    }

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
