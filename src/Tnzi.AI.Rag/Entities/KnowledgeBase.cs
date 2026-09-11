namespace Tnzi.AI.Rag.Entities;

/// <summary>
/// 知识库实体
/// </summary>
public class KnowledgeBase : MultiTenantAuditedEntity<Guid>
{
    /// <summary>
    /// 知识库名称
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// 知识库描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 嵌入提供商名称（对应 AI:Providers 配置）
    /// </summary>
    public string EmbeddingProvider { get; set; } = "default";

    /// <summary>
    /// 嵌入模型名称，null 使用默认模型
    /// </summary>
    public string? EmbeddingModel { get; set; }

    /// <summary>
    /// 切块大小（字符数）
    /// </summary>
    public int ChunkSize { get; set; } = 512;

    /// <summary>
    /// 切块重叠（字符数）
    /// </summary>
    public int ChunkOverlap { get; set; } = 128;

    /// <summary>
    /// 文档数量
    /// </summary>
    public int DocumentCount { get; set; }

    /// <summary>
    /// 总块数
    /// </summary>
    public int ChunkCount { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 是否允许普通已登录用户经用户端 RAG 端点（<c>POST /api/rag/query</c>、<c>/chat</c>）直接查询本库。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>默认 false</b>。这不是保守，是这条开关存在的全部理由：用户端端点此前没有任何
    /// 知识库级授权，不带 id 的请求走 search-all，任何已登录用户一次调用就能拿到全部知识库的
    /// 原文分块。默认 true 会让修复变成一句注释。
    /// </para>
    /// <para>
    /// 与 agent 侧无关：agent 能用哪些知识库由 <c>AgentKnowledgeGrant</c> 决定，
    /// 那条路径不经过本开关。
    /// </para>
    /// </remarks>
    public bool IsUserQueryable { get; set; }

    /// <summary>
    /// Timestamp when a reindex operation started. Used as a distributed mutex
    /// to prevent concurrent reindex runs from corrupting chunk embeddings.
    /// Null = no reindex in progress. Stale values older than ReindexStaleLockThreshold
    /// are considered abandoned (crashed worker) and may be taken over by a new run.
    /// </summary>
    public DateTime? ReindexingAt { get; set; }
}
