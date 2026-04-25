namespace Tnzi.AI.Services;

public class NoOpVectorStore : IVectorStore, INoOpService
{
    public Task<List<VectorSearchResult>> SearchAsync(
        float[] queryVector,
        int topK,
        Guid? knowledgeBaseId = null,
        CancellationToken ct = default)
        => Task.FromResult(new List<VectorSearchResult>());

    public Task UpsertAsync(Guid chunkId, float[] embedding, Guid documentId, Guid knowledgeBaseId,
        string content, int chunkIndex, string? metadata = null, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DeleteByDocumentAsync(Guid documentId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DeleteByKnowledgeBaseAsync(Guid knowledgeBaseId, CancellationToken ct = default)
        => Task.CompletedTask;
}
