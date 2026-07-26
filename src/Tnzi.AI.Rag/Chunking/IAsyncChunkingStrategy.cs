namespace Tnzi.AI.Rag.Chunking;

/// <summary>
/// 异步分块策略接口 - 支持需要 I/O 操作（如嵌入计算）的分块策略
/// </summary>
/// <remarks>
/// <para>
/// 与同步的 <see cref="IChunkingStrategy"/> 不同，此接口允许在分块过程中执行异步操作，
/// 例如通过嵌入服务计算句子间的语义相似度来确定分块边界。
/// </para>
/// <para>
/// 当同时注册了 <see cref="IAsyncChunkingStrategy"/> 和 <see cref="IChunkingStrategy"/> 时，
/// <see cref="Services.DocumentIngestionService"/> 优先使用异步策略。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "Async chunking strategy is in preview")]
public interface IAsyncChunkingStrategy
{
    /// <summary>
    /// 将文本异步拆分为语义相关的块
    /// </summary>
    /// <param name="text">待切块的文本</param>
    /// <param name="chunkSize">每个块的最大字符数</param>
    /// <param name="chunkOverlap">相邻块之间的重叠字符数（语义分块中仅作为参考）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>切块后的文本列表</returns>
    Task<List<string>> ChunkAsync(string text, int chunkSize, int chunkOverlap, CancellationToken ct = default);
}
