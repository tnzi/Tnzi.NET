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

        // 重叠至多算半个块（与 ChunkSettings 的校验同一条线）：一块最早在 0.8 处断开，重叠逼近它时每块只前进一个字符，
        // 一篇长文档会切出与字符数同量级的块。校验挡住了配置入口，这里挡住绕过校验的调用方（直接 new 的切块器、
        // 校验收紧之前建的知识库、MarkdownHeader 减掉标题长度之后的有效块大小）。合规的重叠不受影响。
        var effectiveOverlap = Math.Clamp(chunkOverlap, 0, chunkSize / 2);

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

            // 没有句子边界、按整块大小断开时，断点不落在代理对中间：分块逐条落库，
            // 孤立的高代理项会让严格 UTF-8 的驱动（Npgsql）拒绝写入，整篇文档入库失败。
            if (breakPoint > 1 && char.IsHighSurrogate(text[position + breakPoint - 1]))
            {
                breakPoint--;
            }

            var chunk = text.Substring(position, breakPoint).Trim();

            if (chunk.Length > 0)
            {
                chunks.Add(chunk);
            }

            // 下一个块的起始位置，考虑重叠
            position += Math.Max(breakPoint - effectiveOverlap, 1);

            // 重叠的起点同样不落在代理对中间：落在后半个上时往后挪一位，下一块不以孤立的低代理项开头（也保证前进）。
            if (position < text.Length && char.IsLowSurrogate(text[position]))
            {
                position++;
            }
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
