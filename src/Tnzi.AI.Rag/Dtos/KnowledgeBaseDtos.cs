namespace Tnzi.AI.Rag.Dtos;

/// <summary>
/// 知识库 DTO
/// </summary>
public class KnowledgeBaseDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string EmbeddingProvider { get; set; } = "default";
    public string? EmbeddingModel { get; set; }
    public int ChunkSize { get; set; }
    public int ChunkOverlap { get; set; }
    public int DocumentCount { get; set; }
    public int ChunkCount { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 创建知识库 DTO
/// </summary>
public class CreateKnowledgeBaseDto
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    public string? EmbeddingProvider { get; set; }
    public string? EmbeddingModel { get; set; }
    public int? ChunkSize { get; set; }
    public int? ChunkOverlap { get; set; }
}

/// <summary>
/// 更新知识库 DTO
/// </summary>
public class UpdateKnowledgeBaseDto
{
    [MaxLength(200)]
    public string? Name { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }

    public bool? IsEnabled { get; set; }
}

/// <summary>
/// 知识库查询 DTO
/// </summary>
public class KnowledgeBaseQueryDto : PagedQueryDto
{
    /// <summary>
    /// 名称关键字搜索
    /// </summary>
    public string? Keyword { get; set; }
}

/// <summary>
/// 文档查询 DTO
/// </summary>
public class DocumentQueryDto : PagedQueryDto
{
    /// <summary>
    /// 文件名关键字搜索
    /// </summary>
    public string? Keyword { get; set; }

    /// <summary>
    /// 按处理状态过滤
    /// </summary>
    public DocumentStatus? Status { get; set; }
}

/// <summary>
/// 文档上传结果 DTO
/// </summary>
public class DocumentUploadResultDto
{
    public Guid DocumentId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public DocumentStatus Status { get; set; }
    public int ChunkCount { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 是否为已存在的文档（去重检测命中）
    /// </summary>
    public bool IsDuplicate { get; set; }
}

/// <summary>
/// 知识文档 DTO
/// </summary>
public class KnowledgeDocumentDto
{
    public Guid Id { get; set; }
    public Guid KnowledgeBaseId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long FileSize { get; set; }
    public int ChunkCount { get; set; }
    public DocumentStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ContentHash { get; set; }
    public int Version { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 搜索结果 DTO
/// </summary>
public class SearchResultDto
{
    /// <summary>
    /// 匹配文本内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 来源文档名
    /// </summary>
    public string? SourceName { get; set; }

    /// <summary>
    /// 知识库名称
    /// </summary>
    public string? KnowledgeBaseName { get; set; }

    /// <summary>
    /// 相似度得分（0-1）
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// 块索引
    /// </summary>
    public int ChunkIndex { get; set; }

    /// <summary>
    /// 额外元数据
    /// </summary>
    public string? Metadata { get; set; }
}

/// <summary>
/// 搜索请求 DTO
/// </summary>
public class SearchRequestDto
{
    [Required]
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// 返回结果数量，默认 5
    /// </summary>
    public int TopK { get; set; } = 5;

    /// <summary>
    /// Metadata 键值过滤条件（在 chunk metadata JSON 中匹配）
    /// </summary>
    public Dictionary<string, string>? MetadataFilter { get; set; }
}

/// <summary>
/// 知识库重新索引结果 DTO
/// </summary>
public class ReindexResultDto
{
    /// <summary>
    /// 知识库 ID
    /// </summary>
    public Guid KnowledgeBaseId { get; set; }

    /// <summary>
    /// 重新嵌入的块总数
    /// </summary>
    public int ChunkCount { get; set; }

    /// <summary>
    /// 涉及的文档总数
    /// </summary>
    public int DocumentCount { get; set; }

    /// <summary>
    /// 重新索引耗时（毫秒）
    /// </summary>
    public long DurationMs { get; set; }
}
