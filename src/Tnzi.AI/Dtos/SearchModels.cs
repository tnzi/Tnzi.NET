namespace Tnzi.AI.Dtos;

/// <summary>
/// 文本搜索过滤条件
/// </summary>
public class TextSearchFilter
{
    /// <summary>
    /// 用户 ID 过滤
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Agent ID 过滤
    /// </summary>
    public Guid? AgentId { get; set; }

    /// <summary>
    /// 知识库 ID 范围过滤 — 仅在这些知识库内检索（空/null = 跨所有启用知识库）。
    /// 用于按 Agent 分配的知识库限定 RAG 检索范围。
    /// </summary>
    public List<Guid>? KnowledgeBaseIds { get; set; }

    /// <summary>
    /// 线程 ID 过滤
    /// </summary>
    public string? ThreadId { get; set; }

    /// <summary>
    /// 集合名称过滤
    /// </summary>
    public string? CollectionName { get; set; }

    /// <summary>
    /// 额外元数据过滤
    /// </summary>
    public Dictionary<string, object?>? Metadata { get; set; }
}

/// <summary>
/// 文本搜索结果
/// </summary>
public class TextSearchResult
{
    /// <summary>
    /// 结果文本内容
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// 来源文档名称（可选）
    /// </summary>
    public string? SourceName { get; set; }

    /// <summary>
    /// 来源文档链接（可选）
    /// </summary>
    public string? SourceLink { get; set; }

    /// <summary>
    /// 相似度得分（可选）
    /// </summary>
    public double? Score { get; set; }

    /// <summary>
    /// 额外元数据
    /// </summary>
    public Dictionary<string, object?>? Metadata { get; set; }
}

/// <summary>
/// 向量搜索结果
/// </summary>
public class VectorSearchResult
{
    /// <summary>块 ID</summary>
    public Guid Id { get; set; }

    /// <summary>块文本内容</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>所属文档 ID</summary>
    public Guid DocumentId { get; set; }

    /// <summary>所属知识库 ID</summary>
    public Guid KnowledgeBaseId { get; set; }

    /// <summary>块在文档中的索引</summary>
    public int ChunkIndex { get; set; }

    /// <summary>额外元数据（JSON 格式）</summary>
    public string? Metadata { get; set; }

    /// <summary>父块 ID（层级切块时的父子关系）</summary>
    public Guid? ParentChunkId { get; set; }

    /// <summary>相似度得分（0-1，越高越相似）</summary>
    public double Score { get; set; }

    /// <summary>内容哈希（用于 WeightedDiminishingReranker 按内容去重聚合）</summary>
    public string? ContentHash { get; set; }
}

/// <summary>
/// 结构化输出选项
/// </summary>
public class StructuredOutputOptions
{
    /// <summary>
    /// 提供商名称
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// 模型 ID
    /// </summary>
    public string? ModelId { get; set; }

    /// <summary>
    /// 是否启用严格模式（强制 AI 输出符合 Schema）
    /// </summary>
    public bool StrictMode { get; set; } = true;

    /// <summary>
    /// 最大重试次数（解析失败时）
    /// </summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>
    /// 温度参数
    /// </summary>
    public float? Temperature { get; set; }

    /// <summary>
    /// 最大输出 Token 数（UseAgent 时通过 ChatOptions 传递给 Agent）
    /// </summary>
    public int? MaxOutputTokens { get; set; }

    /// <summary>
    /// 系统提示词（可选，用于指导输出格式）
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// 自定义 JSON Schema（可选，默认从类型生成）
    /// </summary>
    public string? JsonSchema { get; set; }

    /// <summary>
    /// 是否通过 ChatClientAgent 执行（可复用 ContextProviders/Tools）
    /// </summary>
    /// <remarks>
    /// 默认 false（轻量模式，直接用 IChatClient）；设为 true 时通过 AgentFactory 创建 Agent 并调用 RunAsync&lt;T&gt;。
    /// </remarks>
    public bool UseAgent { get; set; } = false;
}
