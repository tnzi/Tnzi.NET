namespace Tnzi.AI.Rag.VectorStore;

/// <summary>
/// pgvector 向量存储适配器 — 基于 Npgsql 直接操作 PostgreSQL pgvector 扩展
/// </summary>
/// <remarks>
/// <para>
/// <b>Important:</b> This implementation requires PostgreSQL with the pgvector extension installed.
/// Other database providers (SQL Server, MySQL, SQLite) are not supported.
/// The pgvector extension must be enabled in the target database: <c>CREATE EXTENSION IF NOT EXISTS vector;</c>
/// </para>
/// <para>
/// Implements <see cref="IVectorStore"/> interface using raw Npgsql commands for optimal performance.
/// Uses <c>float[]</c> as vector input, converting to pgvector format for cosine similarity search.
/// </para>
/// </remarks>
public class PgVectorStore : IVectorStore
{
    private readonly ILogger<PgVectorStore> _logger;
    private readonly string _connectionString;
    private readonly string _chunkTable;
    private readonly string _knowledgeBaseTable;

    public PgVectorStore(IConfiguration configuration, IOptions<AIRagOptions> options, ILogger<PgVectorStore> logger)
    {
        Check.NotNull(configuration);
        _logger = Check.NotNull(logger);
        _connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' not found for PgVectorStore");

        var prefix = Check.NotNull(options).Value.TableNamePrefix;
        _chunkTable = $"{prefix}_DocumentChunk";
        _knowledgeBaseTable = $"{prefix}_KnowledgeBase";
    }

    /// <inheritdoc />
    public async Task<List<VectorSearchResult>> SearchAsync(
        float[] queryVector,
        int topK,
        Guid? knowledgeBaseId = null,
        CancellationToken ct = default)
    {
        return await SearchAsync(queryVector, topK, knowledgeBaseId, metadataFilter: null, ct);
    }

    /// <inheritdoc />
    public async Task<List<VectorSearchResult>> SearchAsync(
        float[] queryVector,
        int topK,
        Guid? knowledgeBaseId,
        Dictionary<string, string>? metadataFilter,
        CancellationToken ct = default)
    {
        Check.NotNull(queryVector);

        var results = new List<VectorSearchResult>();
        var vectorString = FormatVector(queryVector);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        // 构建 metadata 过滤条件
        var metadataConditions = new List<string>();
        var metadataParams = new List<NpgsqlParameter>();
        if (metadataFilter is { Count: > 0 })
        {
            var idx = 0;
            foreach (var (key, value) in metadataFilter)
            {
                var paramName = $"@meta_{idx}";
                // 使用 PostgreSQL JSON 操作符 ->>
                metadataConditions.Add($"\"Metadata\"::jsonb ->> @metaKey_{idx} = {paramName}");
                metadataParams.Add(new NpgsqlParameter($"metaKey_{idx}", NpgsqlDbType.Text) { Value = key });
                metadataParams.Add(new NpgsqlParameter($"meta_{idx}", NpgsqlDbType.Text) { Value = value });
                idx++;
            }
        }

        var extraWhere = metadataConditions.Count > 0
            ? " AND " + string.Join(" AND ", metadataConditions)
            : "";

        var sql = knowledgeBaseId.HasValue
            ? $"""
              SELECT "Id", "Content", "DocumentId", "KnowledgeBaseId", "ChunkIndex", "Metadata", "ParentChunkId",
                     1 - ("Embedding" <=> @query::vector) AS score
              FROM "{_chunkTable}"
              WHERE "KnowledgeBaseId" = @kbId{extraWhere}
              ORDER BY "Embedding" <=> @query::vector
              LIMIT @topK
              """
            : $"""
              SELECT c."Id", c."Content", c."DocumentId", c."KnowledgeBaseId", c."ChunkIndex", c."Metadata", c."ParentChunkId",
                     1 - (c."Embedding" <=> @query::vector) AS score
              FROM "{_chunkTable}" c
              INNER JOIN "{_knowledgeBaseTable}" kb ON c."KnowledgeBaseId" = kb."Id"
              WHERE kb."IsEnabled" = true AND kb."IsDeleted" = false{extraWhere.Replace("\"Metadata\"", "c.\"Metadata\"")}
              ORDER BY c."Embedding" <=> @query::vector
              LIMIT @topK
              """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add(new NpgsqlParameter("query", NpgsqlDbType.Varchar) { Value = vectorString });
        cmd.Parameters.Add(new NpgsqlParameter("topK", NpgsqlDbType.Integer) { Value = topK });

        if (knowledgeBaseId.HasValue)
        {
            cmd.Parameters.Add(new NpgsqlParameter("kbId", NpgsqlDbType.Uuid) { Value = knowledgeBaseId.Value });
        }

        foreach (var param in metadataParams)
        {
            cmd.Parameters.Add(param);
        }

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new VectorSearchResult
            {
                Id = reader.GetGuid(0),
                Content = reader.GetString(1),
                DocumentId = reader.GetGuid(2),
                KnowledgeBaseId = reader.GetGuid(3),
                ChunkIndex = reader.GetInt32(4),
                Metadata = reader.IsDBNull(5) ? null : reader.GetString(5),
                ParentChunkId = reader.IsDBNull(6) ? null : reader.GetGuid(6),
                Score = reader.GetDouble(7)
            });
        }

        _logger.LogDebug("Vector search returned {Count} results (topK={TopK}, kbId={KbId})",
            results.Count, topK, knowledgeBaseId);

        return results;
    }

    /// <inheritdoc />
    public async Task DeleteByDocumentAsync(Guid documentId, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"""DELETE FROM "{_chunkTable}" WHERE "DocumentId" = @docId""", connection);
        cmd.Parameters.Add(new NpgsqlParameter("docId", NpgsqlDbType.Uuid) { Value = documentId });

        var deleted = await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogDebug("Deleted {Count} chunks for document {DocumentId}", deleted, documentId);
    }

    /// <inheritdoc />
    public async Task DeleteByKnowledgeBaseAsync(Guid knowledgeBaseId, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"""DELETE FROM "{_chunkTable}" WHERE "KnowledgeBaseId" = @kbId""", connection);
        cmd.Parameters.Add(new NpgsqlParameter("kbId", NpgsqlDbType.Uuid) { Value = knowledgeBaseId });

        var deleted = await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogDebug("Deleted {Count} chunks for knowledge base {KnowledgeBaseId}", deleted, knowledgeBaseId);
    }

    /// <inheritdoc />
    public async Task UpsertAsync(Guid chunkId, float[] embedding, Guid documentId, Guid knowledgeBaseId,
        string content, int chunkIndex, string? metadata = null, CancellationToken ct = default)
    {
        Check.NotNull(embedding);
        Check.NotNull(content);

        var vectorString = FormatVector(embedding);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        // INSERT ... ON CONFLICT DO UPDATE — 对已有 chunk 进行更新
        var sql = $"""
            INSERT INTO "{_chunkTable}" ("Id", "Embedding", "DocumentId", "KnowledgeBaseId", "Content", "ChunkIndex", "Metadata")
            VALUES (@id, @embedding::vector, @docId, @kbId, @content, @chunkIndex, @metadata)
            ON CONFLICT ("Id") DO UPDATE SET
                "Embedding" = EXCLUDED."Embedding",
                "Content" = EXCLUDED."Content",
                "ChunkIndex" = EXCLUDED."ChunkIndex",
                "Metadata" = EXCLUDED."Metadata"
            """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = chunkId });
        cmd.Parameters.Add(new NpgsqlParameter("embedding", NpgsqlDbType.Varchar) { Value = vectorString });
        cmd.Parameters.Add(new NpgsqlParameter("docId", NpgsqlDbType.Uuid) { Value = documentId });
        cmd.Parameters.Add(new NpgsqlParameter("kbId", NpgsqlDbType.Uuid) { Value = knowledgeBaseId });
        cmd.Parameters.Add(new NpgsqlParameter("content", NpgsqlDbType.Text) { Value = content });
        cmd.Parameters.Add(new NpgsqlParameter("chunkIndex", NpgsqlDbType.Integer) { Value = chunkIndex });
        cmd.Parameters.Add(new NpgsqlParameter("metadata", NpgsqlDbType.Text) { Value = (object?)metadata ?? DBNull.Value });

        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogDebug("Upserted chunk {ChunkId} for document {DocumentId} in knowledge base {KnowledgeBaseId}",
            chunkId, documentId, knowledgeBaseId);
    }

    /// <summary>
    /// 将 float[] 转为 pgvector 格式字符串 "[0.1,0.2,0.3]"
    /// </summary>
    private static string FormatVector(float[] vector)
    {
        var sb = new StringBuilder("[");
        for (var i = 0; i < vector.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(vector[i].ToString(CultureInfo.InvariantCulture));
        }
        sb.Append(']');
        return sb.ToString();
    }
}
