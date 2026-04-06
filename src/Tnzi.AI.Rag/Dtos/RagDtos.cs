namespace Tnzi.AI.Rag.Dtos;

/// <summary>
/// RAG 检索选项
/// </summary>
public class RagRetrievalOptions
{
    /// <summary>指定知识库 ID 列表（null 则搜索全部启用的知识库）</summary>
    public List<Guid>? KnowledgeBaseIds { get; set; }

    /// <summary>返回的最大结果数</summary>
    public int TopK { get; set; } = 5;

    /// <summary>最低相关性得分阈值（0-1）</summary>
    public double MinRelevance { get; set; } = 0.3;

    /// <summary>是否启用 Parent Document Retrieval（null 则使用全局配置）</summary>
    public bool? EnableParentRetrieval { get; set; }
}

/// <summary>
/// RAG 检索结果（单条）
/// </summary>
public class RetrievalResult
{
    /// <summary>匹配文本内容</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>相似度得分（0-1）</summary>
    public double Score { get; set; }

    /// <summary>所属知识库 ID</summary>
    public Guid KnowledgeBaseId { get; set; }

    /// <summary>所属文档 ID</summary>
    public Guid DocumentId { get; set; }

    /// <summary>额外元数据</summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// RAG 查询请求（单轮 Q&amp;A，无历史上下文）
/// </summary>
public class RagQueryRequest
{
    /// <summary>查询文本</summary>
    [Required]
    [MaxLength(10000)]
    public string Query { get; set; } = null!;

    /// <summary>指定知识库 ID 列表（null 则搜索全部启用的知识库）</summary>
    public List<Guid>? KnowledgeBaseIds { get; set; }

    /// <summary>返回的最大检索结果数</summary>
    public int TopK { get; set; } = 5;

    /// <summary>最低相关性得分阈值（0-1）</summary>
    public double MinRelevance { get; set; } = 0.3;

    /// <summary>是否包含引用来源</summary>
    public bool IncludeCitations { get; set; } = true;
}

/// <summary>
/// RAG 查询结果
/// </summary>
public class RagQueryResult
{
    /// <summary>AI 生成的回答</summary>
    public string Answer { get; set; } = string.Empty;

    /// <summary>引用来源列表</summary>
    public List<CitationDto> Citations { get; set; } = new();

    /// <summary>Token 使用量</summary>
    public TokenUsageDto? Usage { get; set; }
}

/// <summary>
/// RAG 聊天请求（多轮对话，带历史上下文）
/// </summary>
public class RagChatRequest : RagQueryRequest
{
    /// <summary>对话线程 ID（null 则新建）</summary>
    public Guid? ThreadId { get; set; }

    /// <summary>Agent ID（null 使用默认配置）</summary>
    public Guid? AgentId { get; set; }
}

/// <summary>
/// RAG 聊天结果
/// </summary>
public class RagChatResult : RagQueryResult
{
    /// <summary>对话线程 ID</summary>
    public Guid ThreadId { get; set; }
}
