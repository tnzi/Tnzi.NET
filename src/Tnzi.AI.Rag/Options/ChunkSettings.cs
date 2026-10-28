namespace Tnzi.AI.Rag.Options;

/// <summary>
/// 切块参数的取值规则。知识库级的 <c>ChunkSize</c> / <c>ChunkOverlap</c> 与全局默认值
/// （<see cref="AIRagOptions.DefaultChunkSize"/> / <see cref="AIRagOptions.DefaultChunkOverlap"/>）按同一条规则校验。
/// </summary>
/// <remarks>
/// <para>
/// 重叠至多半个块：默认切块器在块的最后 20% 里找句子边界，一块最早在 0.8 处断开；重叠超过半个块时下一块的起点
/// 离上一块不到 0.3 个块，重叠逼近 0.8 个块时每一块只前进一个字符，一篇 50KB 的文档切出约 5 万块、付 5 万次嵌入调用。
/// </para>
/// <para>
/// 块大小有上下限：太小的块装不下一句完整的话；太大的块超出嵌入模型的输入上限，这个知识库的每一次上传都会失败，
/// 而知识库的切块参数创建后改不了。单位是字符。
/// </para>
/// </remarks>
public static class ChunkSettings
{
    /// <summary>块大小下限（字符）。</summary>
    public const int MinChunkSize = 128;

    /// <summary>块大小上限（字符）。</summary>
    public const int MaxChunkSize = 8192;

    /// <summary>给定块大小下允许的最大重叠（半个块）。</summary>
    public static int MaxOverlapFor(int chunkSize) => chunkSize / 2;

    /// <summary>
    /// 校验一组切块参数；有效时返回 <see langword="null"/>，否则返回说明原因的英文消息（可直接回给调用方）。
    /// </summary>
    public static string? Validate(int chunkSize, int chunkOverlap)
    {
        if (chunkSize is < MinChunkSize or > MaxChunkSize)
        {
            return $"ChunkSize must be between {MinChunkSize} and {MaxChunkSize} characters (got {chunkSize}).";
        }

        if (chunkOverlap < 0 || chunkOverlap > MaxOverlapFor(chunkSize))
        {
            return $"ChunkOverlap must be between 0 and half of ChunkSize ({MaxOverlapFor(chunkSize)}) (got {chunkOverlap}).";
        }

        return null;
    }
}
