namespace Tnzi.AI.Rag.Chunking;

/// <summary>
/// 固定大小切块策略，在句子边界处断开以保持语义完整性
/// </summary>
public class FixedSizeChunkingStrategy : IChunkingStrategy
{
    private static readonly char[] SentenceEndings = ['.', '!', '?', '\n'];

    /// <inheritdoc />
    public List<string> Chunk(string text, int chunkSize, int chunkOverlap)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        if (text.Length <= chunkSize)
        {
            return [text];
        }

        var chunks = new List<string>();
        var position = 0;

        while (position < text.Length)
        {
            var remaining = text.Length - position;
            var length = Math.Min(chunkSize, remaining);

            if (length == remaining)
            {
                // 最后一段，直接取到末尾
                chunks.Add(text[position..].Trim());
                break;
            }

            // 在最后 20% 范围内寻找句子边界
            var breakPoint = FindSentenceBreak(text, position, length);
            var chunk = text.Substring(position, breakPoint).Trim();

            if (chunk.Length > 0)
            {
                chunks.Add(chunk);
            }

            // 下一个块的起始位置，考虑重叠
            position += Math.Max(breakPoint - chunkOverlap, 1);
        }

        return chunks;
    }

    /// <summary>
    /// 在块的最后 20% 范围内寻找句子边界断点
    /// </summary>
    private static int FindSentenceBreak(string text, int start, int length)
    {
        var searchStart = start + (int)(length * 0.8);
        var searchEnd = start + length;

        // 从后向前搜索句子结束符
        for (var i = searchEnd - 1; i >= searchStart; i--)
        {
            if (SentenceEndings.Contains(text[i]))
            {
                return i - start + 1;
            }
        }

        // 未找到句子边界，使用完整块大小
        return length;
    }
}
