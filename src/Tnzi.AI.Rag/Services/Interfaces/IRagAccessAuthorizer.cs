namespace Tnzi.AI.Rag.Services;

/// <summary>
/// 用户直连 RAG 端点的知识库级授权：把一次请求想查的知识库集合，收敛成调用者<b>确实被允许</b>查的那部分。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>为什么需要它</b>：<c>DefaultRagController</c> 是 <c>[DefaultController]</c>（自动激活），
/// 类级只有一个裸 <c>[ApiAuthorize]</c>。不带 <c>KnowledgeBaseIds</c> 的请求走 search-all，
/// 于是任何一个已登录用户一次 <c>POST /api/rag/query</c> 就能命中<b>全部</b>知识库的原文分块
/// （<c>Citations</c> 直接回原文）；带 id 的请求也只按 id 查，从不判定归属。
/// <c>AgentKnowledgeGrant</c> 只约束 agent 能用哪些知识库，管不到用户直连这条路。
/// </para>
/// <para>
/// 判定<b>落在服务层</b>（<c>RagQueryEngine</c> / <c>RagChatEngine</c>），不是控制器：控制器可以被
/// 消费方整体替换，挂在其上的守卫会随之消失。agent 侧检索经 <c>ITextSearchService</c>
/// （<c>VectorTextSearchService</c> 委托给 <c>IRagRetriever</c> / <c>HybridSearchService</c>），
/// 不经过这两个引擎，因此不受本契约影响 —— 它的知识库范围由 <c>AgentKnowledgeGrant</c> 约束。
/// </para>
/// <para>
/// 默认实现见 <c>DefaultRagAccessAuthorizer</c>。消费方可注册自己的实现来接入自家的可见性模型
/// （按部门、按项目、按订阅等）。
/// </para>
/// </remarks>
public interface IRagAccessAuthorizer
{
    /// <summary>
    /// 解析调用者本次可查的知识库集合。
    /// </summary>
    /// <param name="requestedKnowledgeBaseIds">
    /// 请求显式指定的知识库；null 或空表示"不限定"（search-all）。
    /// </param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>
    /// 成功时携带<b>必须显式使用</b>的知识库 id 列表（调用方应把它写回
    /// <c>RagRetrievalOptions.KnowledgeBaseIds</c>，而不是继续走 search-all）；
    /// 无权时返回失败结果（403）。
    /// </returns>
    Task<Result<IReadOnlyList<Guid>>> AuthorizeQueryAsync(
        IReadOnlyList<Guid>? requestedKnowledgeBaseIds,
        CancellationToken ct = default);
}
