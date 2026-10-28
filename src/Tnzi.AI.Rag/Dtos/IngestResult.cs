namespace Tnzi.AI.Rag.Dtos;

/// <summary>
/// 文档摄取结果
/// </summary>
public class IngestResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Succeeded { get; set; }

    /// <summary>
    /// 生成的块数
    /// </summary>
    public int ChunkCount { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 文档行在摄取进行期间被删除了：本次写下的分块与图谱已经清掉，调用方不应再回写文档状态或知识库计数。
    /// </summary>
    public bool DocumentDeleted { get; set; }

    public static IngestResult Success(int chunkCount) => new() { Succeeded = true, ChunkCount = chunkCount };
    public static IngestResult Failure(string error) => new() { Succeeded = false, ErrorMessage = error };

    public static IngestResult DeletedDuringIngestion() => new()
    {
        Succeeded = false,
        DocumentDeleted = true,
        ErrorMessage = "The document was deleted while it was being ingested."
    };
}
