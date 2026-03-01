namespace Tnzi.AI.Rag.Chunking;

/// <summary>
/// 层级切块策略 — 创建父子块关系，父块为大段落（按标题/分隔符分割），子块为固定大小的小块
/// </summary>
/// <remarks>
/// <para>
/// 算法流程：
/// 1. 将文本按 Markdown 标题或双换行符分割为父块（大段落）
/// 2. 对每个父块使用固定大小策略进一步拆分为子块
/// 3. 返回 <see cref="HierarchicalChunkResult"/>，包含父块和子块的映射关系
/// </para>
/// <para>
/// 使用场景：检索时先匹配子块（精确定位），再回溯父块获取更完整的上下文。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "Hierarchical chunking is in preview")]
public partial class HierarchicalChunkingStrategy : IChunkingStrategy
{
    private readonly FixedSizeChunkingStrategy _fixedSizeStrategy = new();

    /// <inheritdoc />
    /// <remarks>
    /// 返回所有子块的文本列表（扁平结构）。
    /// 如需获取父子关系，请使用 <see cref="ChunkWithHierarchy"/> 方法。
    /// </remarks>
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

        var result = ChunkWithHierarchy(text, chunkSize, chunkOverlap);
        // 扁平化：返回所有子块文本（包含只有父块无子块的情况）
        var allChunks = new List<string>();
        foreach (var parent in result.Parents)
        {
            var children = result.GetChildren(parent.Index);
            if (children.Count == 0)
            {
                // 父块本身作为一个块
                allChunks.Add(parent.Content);
            }
            else
            {
                allChunks.AddRange(children.Select(c => c.Content));
            }
        }
        return allChunks;
    }

    /// <summary>
    /// 层级切块 — 返回父块和子块的映射关系
    /// </summary>
    /// <param name="text">待切块的文本</param>
    /// <param name="chunkSize">子块的最大字符数</param>
    /// <param name="chunkOverlap">子块之间的重叠字符数</param>
    /// <returns>层级切块结果，包含父块列表和父子映射关系</returns>
    public HierarchicalChunkResult ChunkWithHierarchy(string text, int chunkSize, int chunkOverlap)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new HierarchicalChunkResult();
        }

        // 1. 按标题/段落分割为父块
        var parentSections = SplitIntoParentSections(text);
        var result = new HierarchicalChunkResult();
        var globalChildIndex = 0;

        for (var parentIndex = 0; parentIndex < parentSections.Count; parentIndex++)
        {
            var section = parentSections[parentIndex];
            var parentChunk = new HierarchicalChunk
            {
                Index = parentIndex,
                Content = section.Trim()
            };
            result.Parents.Add(parentChunk);

            // 2. 对父块内容进行子块切分
            if (section.Length <= chunkSize)
            {
                // 父块本身足够小，不需要子块
                continue;
            }

            var childTexts = _fixedSizeStrategy.Chunk(section, chunkSize, chunkOverlap);
            foreach (var childText in childTexts)
            {
                var childChunk = new HierarchicalChunk
                {
                    Index = globalChildIndex++,
                    Content = childText,
                    ParentIndex = parentIndex
                };
                result.Children.Add(childChunk);
            }
        }

        return result;
    }

    /// <summary>
    /// 按 Markdown 标题或双换行符分割文本为父段落
    /// </summary>
    private static List<string> SplitIntoParentSections(string text)
    {
        var sections = new List<string>();
        var lines = text.Split('\n');
        var currentSection = new StringBuilder();

        foreach (var line in lines)
        {
            if (HeaderRegex().IsMatch(line) && currentSection.Length > 0)
            {
                // 遇到新标题，保存当前段落
                var sectionText = currentSection.ToString().Trim();
                if (sectionText.Length > 0)
                {
                    sections.Add(sectionText);
                }
                currentSection.Clear();
            }

            if (currentSection.Length > 0)
            {
                currentSection.Append('\n');
            }
            currentSection.Append(line.TrimEnd());
        }

        // 最后一个段落
        var lastSection = currentSection.ToString().Trim();
        if (lastSection.Length > 0)
        {
            sections.Add(lastSection);
        }

        // 如果没有标题分割，尝试用双换行符分割
        if (sections.Count <= 1 && text.Contains("\n\n"))
        {
            sections.Clear();
            var paragraphs = text.Split(["\n\n"], StringSplitOptions.RemoveEmptyEntries);
            foreach (var para in paragraphs)
            {
                var trimmed = para.Trim();
                if (trimmed.Length > 0)
                {
                    sections.Add(trimmed);
                }
            }
        }

        // 如果仍然只有一个段落，就返回整个文本
        if (sections.Count == 0)
        {
            sections.Add(text.Trim());
        }

        return sections;
    }

    [GeneratedRegex(@"^#{1,6}\s+")]
    private static partial Regex HeaderRegex();
}

/// <summary>
/// 层级切块结果 — 包含父块列表和子块列表，通过 ParentIndex 关联
/// </summary>
[ExperimentalApi(Reason = "Hierarchical chunking is in preview")]
public class HierarchicalChunkResult
{
    /// <summary>
    /// 父块列表（大段落）
    /// </summary>
    public List<HierarchicalChunk> Parents { get; set; } = [];

    /// <summary>
    /// 子块列表（小块，每个子块关联一个父块）
    /// </summary>
    public List<HierarchicalChunk> Children { get; set; } = [];

    /// <summary>
    /// 获取指定父块的所有子块
    /// </summary>
    /// <param name="parentIndex">父块索引</param>
    /// <returns>该父块下的子块列表</returns>
    public List<HierarchicalChunk> GetChildren(int parentIndex)
    {
        return Children.Where(c => c.ParentIndex == parentIndex).ToList();
    }
}

/// <summary>
/// 层级块 — 可以是父块或子块
/// </summary>
[ExperimentalApi(Reason = "Hierarchical chunking is in preview")]
public class HierarchicalChunk
{
    /// <summary>
    /// 块索引
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// 块文本内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 父块索引（仅子块有值；父块为 null）
    /// </summary>
    public int? ParentIndex { get; set; }
}
