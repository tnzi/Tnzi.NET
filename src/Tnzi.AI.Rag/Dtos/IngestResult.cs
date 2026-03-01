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

    public static IngestResult Success(int chunkCount) => new() { Succeeded = true, ChunkCount = chunkCount };
    public static IngestResult Failure(string error) => new() { Succeeded = false, ErrorMessage = error };
}
