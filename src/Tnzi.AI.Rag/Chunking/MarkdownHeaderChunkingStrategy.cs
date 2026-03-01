namespace Tnzi.AI.Rag.Chunking;

/// <summary>
/// Markdown 标题切块策略，按标题分割文档并保留标题上下文
/// </summary>
public partial class MarkdownHeaderChunkingStrategy : IChunkingStrategy
{
    private readonly FixedSizeChunkingStrategy _fallback = new();

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

        var sections = SplitByHeaders(text);
        var chunks = new List<string>();

        foreach (var (header, content) in sections)
        {
            var sectionText = string.IsNullOrEmpty(header) ? content : $"{header}\n{content}";

            if (sectionText.Length <= chunkSize)
            {
                var trimmed = sectionText.Trim();
                if (trimmed.Length > 0)
                {
                    chunks.Add(trimmed);
                }
            }
            else
            {
                // 段落过长，使用固定大小策略对内容切块，并在每个块前加标题
                var effectiveChunkSize = Math.Max(chunkSize - header.Length - 1, 1);
                var subChunks = _fallback.Chunk(content, effectiveChunkSize, chunkOverlap);
                foreach (var sub in subChunks)
                {
                    var chunk = string.IsNullOrEmpty(header) ? sub : $"{header}\n{sub}";
                    chunks.Add(chunk.Trim());
                }
            }
        }

        return chunks;
    }

    /// <summary>
    /// 按 Markdown 标题分割文本为 (标题, 内容) 列表
    /// </summary>
    private static List<(string Header, string Content)> SplitByHeaders(string text)
    {
        var sections = new List<(string Header, string Content)>();
        var lines = text.Split('\n');
        var currentHeader = string.Empty;
        var currentContent = new StringBuilder();

        foreach (var line in lines)
        {
            if (HeaderRegex().IsMatch(line))
            {
                // 保存前一个段落
                if (currentContent.Length > 0 || !string.IsNullOrEmpty(currentHeader))
                {
                    sections.Add((currentHeader, currentContent.ToString()));
                    currentContent.Clear();
                }
                currentHeader = line.TrimEnd();
            }
            else
            {
                if (currentContent.Length > 0)
                {
                    currentContent.Append('\n');
                }
                currentContent.Append(line.TrimEnd());
            }
        }

        // 最后一个段落
        if (currentContent.Length > 0 || !string.IsNullOrEmpty(currentHeader))
        {
            sections.Add((currentHeader, currentContent.ToString()));
        }

        return sections;
    }

    [GeneratedRegex(@"^#{1,6}\s+")]
    private static partial Regex HeaderRegex();
}
