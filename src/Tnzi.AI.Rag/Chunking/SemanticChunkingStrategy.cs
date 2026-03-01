namespace Tnzi.AI.Rag.Chunking;

/// <summary>
/// 语义分块策略 — 基于句子间嵌入相似度在语义边界处拆分文本
/// </summary>
/// <remarks>
/// <para>
/// 算法流程：
/// 1. 将文本拆分为句子（以 .!?\n 为分隔符）
/// 2. 通过 <see cref="IEmbeddingService"/> 批量生成每个句子的嵌入向量
/// 3. 计算相邻句子间的余弦相似度
/// 4. 在相似度低于阈值（默认 0.5）的位置确定分块边界
/// 5. 将边界之间的句子合并为块
/// 6. 对过小的块进行合并，对过大的块用 <see cref="FixedSizeChunkingStrategy"/> 回退拆分
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "Semantic chunking is in preview")]
public class SemanticChunkingStrategy : IAsyncChunkingStrategy
{
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<SemanticChunkingStrategy> _logger;
    private readonly double _similarityThreshold;
    private readonly int _minChunkSize;

    private static readonly char[] SentenceDelimiters = ['.', '!', '?', '\n'];

    /// <summary>
    /// 创建语义分块策略实例
    /// </summary>
    /// <param name="embeddingService">嵌入服务（用于计算句子向量）</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="similarityThreshold">相似度阈值，低于此值的位置将作为分块边界（默认 0.5）</param>
    /// <param name="minChunkSize">最小块大小（字符数），小于此值的块将与相邻块合并（默认 100）</param>
    public SemanticChunkingStrategy(
        IEmbeddingService embeddingService,
        ILogger<SemanticChunkingStrategy> logger,
        double similarityThreshold = 0.5,
        int minChunkSize = 100)
    {
        _embeddingService = Check.NotNull(embeddingService);
        _logger = Check.NotNull(logger);
        _similarityThreshold = similarityThreshold;
        _minChunkSize = minChunkSize;
    }

    /// <inheritdoc />
    public async Task<List<string>> ChunkAsync(string text, int chunkSize, int chunkOverlap, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        if (text.Length <= chunkSize)
        {
            return [text];
        }

        // 1. 拆分为句子
        var sentences = SplitIntoSentences(text);
        if (sentences.Count <= 1)
        {
            return [text];
        }

        _logger.LogDebug("Split text into {SentenceCount} sentences for semantic chunking", sentences.Count);

        // 2. 批量生成嵌入
        var embeddingResult = await _embeddingService.GenerateEmbeddingsAsync(sentences, ct: ct);
        if (!embeddingResult.Succeeded || embeddingResult.Data == null || embeddingResult.Data.Count != sentences.Count)
        {
            // 嵌入生成失败，回退到固定大小分块
            _logger.LogWarning("Embedding generation failed for semantic chunking, falling back to fixed-size chunking");
            return new FixedSizeChunkingStrategy().Chunk(text, chunkSize, chunkOverlap);
        }

        var embeddings = embeddingResult.Data;

        // 3. 计算相邻句子间余弦相似度并确定分块边界
        var breakPoints = FindBreakPoints(embeddings);

        _logger.LogDebug("Found {BreakPointCount} semantic break points", breakPoints.Count);

        // 4. 将句子按边界分组
        var rawChunks = GroupSentencesByBreakPoints(sentences, breakPoints);

        // 5. 合并过小的块、拆分过大的块
        var finalChunks = EnforceChunkSizeLimits(rawChunks, chunkSize, chunkOverlap);

        _logger.LogDebug("Semantic chunking produced {ChunkCount} chunks", finalChunks.Count);

        return finalChunks;
    }

    /// <summary>
    /// 将文本拆分为句子
    /// </summary>
    public static List<string> SplitIntoSentences(string text)
    {
        var sentences = new List<string>();
        var current = new StringBuilder();

        for (var i = 0; i < text.Length; i++)
        {
            current.Append(text[i]);

            if (SentenceDelimiters.Contains(text[i]))
            {
                var sentence = current.ToString().Trim();
                if (sentence.Length > 0)
                {
                    sentences.Add(sentence);
                }
                current.Clear();
            }
        }

        // 处理末尾没有句号的文本
        var remaining = current.ToString().Trim();
        if (remaining.Length > 0)
        {
            sentences.Add(remaining);
        }

        return sentences;
    }

    /// <summary>
    /// 找到相似度下降的分块边界
    /// </summary>
    private List<int> FindBreakPoints(List<float[]> embeddings)
    {
        var breakPoints = new List<int>();

        for (var i = 0; i < embeddings.Count - 1; i++)
        {
            var similarity = InMemoryVectorStore.CosineSimilarity(embeddings[i], embeddings[i + 1]);

            if (similarity < _similarityThreshold)
            {
                // 在 i 和 i+1 之间断开，即 i+1 是新块的起始句子
                breakPoints.Add(i + 1);
            }
        }

        return breakPoints;
    }

    /// <summary>
    /// 将句子按分块边界分组为文本块
    /// </summary>
    private static List<string> GroupSentencesByBreakPoints(List<string> sentences, List<int> breakPoints)
    {
        var chunks = new List<string>();
        var startIndex = 0;

        foreach (var bp in breakPoints)
        {
            if (bp > startIndex)
            {
                var chunk = string.Join(" ", sentences.Skip(startIndex).Take(bp - startIndex));
                if (chunk.Trim().Length > 0)
                {
                    chunks.Add(chunk.Trim());
                }
            }
            startIndex = bp;
        }

        // 最后一组
        if (startIndex < sentences.Count)
        {
            var lastChunk = string.Join(" ", sentences.Skip(startIndex));
            if (lastChunk.Trim().Length > 0)
            {
                chunks.Add(lastChunk.Trim());
            }
        }

        return chunks;
    }

    /// <summary>
    /// 强制执行块大小限制 — 合并过小的块，拆分过大的块
    /// </summary>
    private List<string> EnforceChunkSizeLimits(List<string> chunks, int maxChunkSize, int chunkOverlap)
    {
        if (chunks.Count == 0) return [];

        var result = new List<string>();
        var fallbackStrategy = new FixedSizeChunkingStrategy();

        // 先合并过小的块
        var merged = MergeSmallChunks(chunks, maxChunkSize);

        // 再拆分过大的块
        foreach (var chunk in merged)
        {
            if (chunk.Length > maxChunkSize)
            {
                // 使用固定大小策略拆分过大的块
                var subChunks = fallbackStrategy.Chunk(chunk, maxChunkSize, chunkOverlap);
                result.AddRange(subChunks);
            }
            else
            {
                result.Add(chunk);
            }
        }

        return result;
    }

    /// <summary>
    /// 将小于最小大小的块与下一个块合并
    /// </summary>
    private List<string> MergeSmallChunks(List<string> chunks, int maxChunkSize)
    {
        if (chunks.Count <= 1) return chunks;

        var merged = new List<string>();
        var current = chunks[0];

        for (var i = 1; i < chunks.Count; i++)
        {
            if (current.Length < _minChunkSize)
            {
                // 当前块太小，与下一个合并（如果合并后不超过最大大小）
                var combined = current + " " + chunks[i];
                if (combined.Length <= maxChunkSize)
                {
                    current = combined;
                    continue;
                }
            }

            merged.Add(current);
            current = chunks[i];
        }

        merged.Add(current);

        return merged;
    }
}
