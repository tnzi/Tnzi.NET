namespace Tnzi.AI.Rag.Chunking;

/// <summary>
/// 文本切块策略接口
/// </summary>
public interface IChunkingStrategy
{
    /// <summary>
    /// 将文本拆分为指定大小的块，支持重叠
    /// </summary>
    /// <param name="text">待切块的文本</param>
    /// <param name="chunkSize">每个块的最大字符数</param>
    /// <param name="chunkOverlap">相邻块之间的重叠字符数</param>
    /// <returns>切块后的文本列表</returns>
    List<string> Chunk(string text, int chunkSize, int chunkOverlap);
}
