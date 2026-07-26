namespace Tnzi.AI.Rag.Services.Interfaces;

/// <summary>
/// Parent Document Retriever - 将细粒度匹配块扩展为更大的上下文窗口
/// <para>
/// 搜索时使用小块（精准匹配），但返回时扩展为包含相邻块的更大上下文，
/// 提升 LLM 理解和回答质量。
/// </para>
/// </summary>
public interface IParentDocumentRetriever
{
    /// <summary>
    /// 将匹配的检索结果扩展为更大的上下文窗口
    /// </summary>
    /// <param name="matchedResults">向量搜索返回的匹配结果</param>
    /// <param name="options">扩展选项（窗口大小、最大 Token 数）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>合并后的扩展上下文块列表</returns>
    Task<IReadOnlyList<ParentDocumentResult>> RetrieveAsync(
        IReadOnlyList<RetrievalResult> matchedResults,
        ParentRetrievalOptions? options = null,
        CancellationToken ct = default);
}
