namespace Tnzi.AI.Rag.Search;

/// <summary>
/// PostgreSQL 全文搜索提供者 — 基于 tsvector/tsquery 进行关键词搜索
/// </summary>
/// <remarks>
/// <para>
/// 使用 PostgreSQL 内置的全文搜索引擎，通过 <c>to_tsvector</c> 和 <c>plainto_tsquery</c>
/// 进行高效的关键词匹配。不需要额外的 schema 变更（tsvector 在查询时动态计算）。
/// </para>
/// <para>
/// 生产环境可考虑在 DocumentChunk.Content 列上创建 GIN 索引以提升性能：
/// <c>CREATE INDEX idx_chunk_content_fts ON "{prefix}_DocumentChunk" USING GIN (to_tsvector('english', "Content"));</c>
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "PostgreSQL full-text search is in preview")]
public class PgFullTextSearchProvider : IKeywordSearchProvider
{
    private readonly ILogger<PgFullTextSearchProvider> _logger;
    private readonly string _connectionString;
    private readonly string _chunkTable;
    private readonly string _knowledgeBaseTable;

    public PgFullTextSearchProvider(
        IConfiguration configuration, IOptions<AIRagOptions> options, ILogger<PgFullTextSearchProvider> logger)
    {
        Check.NotNull(configuration);
        _logger = Check.NotNull(logger);
        _connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' not found for PgFullTextSearchProvider");

        var prefix = Check.NotNull(options).Value.TableNamePrefix;
        _chunkTable = $"{prefix}_DocumentChunk";
        _knowledgeBaseTable = $"{prefix}_KnowledgeBase";
    }

    /// <inheritdoc />
    public async Task<List<KeywordSearchResult>> SearchAsync(
        string query, int topK, Guid? knowledgeBaseId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var sw = Stopwatch.StartNew();
        var results = new List<KeywordSearchResult>();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var sql = knowledgeBaseId.HasValue
            ? $"""
              SELECT "Id", "Content", "DocumentId", "KnowledgeBaseId",
                     ts_rank(to_tsvector('english', "Content"), plainto_tsquery('english', @query)) AS score
              FROM "{_chunkTable}"
              WHERE "KnowledgeBaseId" = @kbId
                AND to_tsvector('english', "Content") @@ plainto_tsquery('english', @query)
              ORDER BY score DESC
              LIMIT @topK
              """
            : $"""
              SELECT c."Id", c."Content", c."DocumentId", c."KnowledgeBaseId",
                     ts_rank(to_tsvector('english', c."Content"), plainto_tsquery('english', @query)) AS score
              FROM "{_chunkTable}" c
              INNER JOIN "{_knowledgeBaseTable}" kb ON c."KnowledgeBaseId" = kb."Id"
              WHERE kb."IsEnabled" = true AND kb."IsDeleted" = false
                AND to_tsvector('english', c."Content") @@ plainto_tsquery('english', @query)
              ORDER BY score DESC
              LIMIT @topK
              """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add(new NpgsqlParameter("query", NpgsqlDbType.Text) { Value = query });
        cmd.Parameters.Add(new NpgsqlParameter("topK", NpgsqlDbType.Integer) { Value = topK });

        if (knowledgeBaseId.HasValue)
        {
            cmd.Parameters.Add(new NpgsqlParameter("kbId", NpgsqlDbType.Uuid) { Value = knowledgeBaseId.Value });
        }

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new KeywordSearchResult
            {
                ChunkId = reader.GetGuid(0),
                Content = reader.GetString(1),
                DocumentId = reader.GetGuid(2),
                KnowledgeBaseId = reader.GetGuid(3),
                Score = reader.GetFloat(4)
            });
        }

        sw.Stop();
        _logger.LogDebug(
            "PostgreSQL full-text search returned {Count} results (topK={TopK}, kbId={KbId}, {Duration:F3}s)",
            results.Count, topK, knowledgeBaseId, sw.Elapsed.TotalSeconds);

        return results;
    }
}
