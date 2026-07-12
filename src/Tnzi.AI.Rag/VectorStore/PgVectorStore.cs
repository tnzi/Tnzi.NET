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
    private readonly IServiceProvider _serviceProvider;
    private readonly bool _multiTenancyEnabled;
    private readonly string _connectionString;
    private readonly string _chunkTable;
    private readonly string _knowledgeBaseTable;

    public PgVectorStore(
        IConfiguration configuration,
        IOptionsMonitor<AIRagOptions> options,
        ILogger<PgVectorStore> logger,
        IServiceProvider serviceProvider,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null)
    {
        Check.NotNull(configuration);
        _logger = Check.NotNull(logger);
        _serviceProvider = Check.NotNull(serviceProvider);
        _multiTenancyEnabled = multiTenancyOptions?.Value.Enabled ?? false;
        _connectionString = RagConnectionStringResolver.Resolve(configuration, nameof(PgVectorStore));

        var prefix = Check.NotNull(options).CurrentValue.TableNamePrefix;
        _chunkTable = $"{prefix}_DocumentChunk";
        _knowledgeBaseTable = $"{prefix}_KnowledgeBase";
    }

    /// <summary>
    /// 解析当前租户 ID。
    /// <para>
    /// PgVectorStore 注册为 Singleton，而 <c>ICurrentTenant</c> 是 Scoped，不能在构造时持有引用
    /// （captive dependency）。通过根 <c>IServiceProvider</c> 创建 scope 动态解析；
    /// <c>CurrentTenant</c> 的 tenant 来源（AsyncLocal 覆盖 + IHttpContextAccessor 驱动的 ICurrentUser）
    /// 均跨 scope 流动，故新建 scope 仍能读到当前请求的租户。与 HangfireBackgroundJobManager 一致。
    /// </para>
    /// </summary>
    private Guid? ResolveTenantId()
    {
        using var scope = _serviceProvider.CreateScope();
        return scope.ServiceProvider.GetService<ICurrentTenant>()?.Id;
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

        // 多租户隔离：仅当存在非空租户上下文时追加 TenantId 谓词（单租户 / host 不追加，见 RagTenantSqlFilter）。
        // fail-closed：MT 启用且无租户上下文（host 后台任务等）时禁止无 KB 范围的跨库搜索，
        // 否则不追加谓词等于放行全部租户的数据。守卫先于连接打开，避免无效连接开销。
        var tenantId = ResolveTenantId();
        RagTenantSqlFilter.EnsureTenantScopeForSearchAll(_multiTenancyEnabled, tenantId, knowledgeBaseId.HasValue);

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

        var sql = BuildSearchSql(_chunkTable, _knowledgeBaseTable, knowledgeBaseId.HasValue, extraWhere, tenantId);

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add(new NpgsqlParameter("query", NpgsqlDbType.Varchar) { Value = vectorString });
        cmd.Parameters.Add(new NpgsqlParameter("topK", NpgsqlDbType.Integer) { Value = topK });

        if (knowledgeBaseId.HasValue)
        {
            cmd.Parameters.Add(new NpgsqlParameter("kbId", NpgsqlDbType.Uuid) { Value = knowledgeBaseId.Value });
        }

        RagTenantSqlFilter.AddParameter(cmd, tenantId);

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
    /// 组合向量搜索 SQL —— 提取为静态方法以便对租户谓词的有无进行单元测试（无需真实 pgvector）。
    /// </summary>
    /// <param name="chunkTable">chunk 表名（已含前缀，不含引号）</param>
    /// <param name="knowledgeBaseTable">knowledge base 表名（已含前缀，不含引号）</param>
    /// <param name="hasKnowledgeBaseId">是否按指定知识库过滤（单表查询）</param>
    /// <param name="extraWhere">metadata 过滤片段（针对非 join 查询的 <c>"Metadata"</c> 列）</param>
    /// <param name="tenantId">当前租户 ID；null 时不追加租户谓词</param>
    public static string BuildSearchSql(
        string chunkTable, string knowledgeBaseTable, bool hasKnowledgeBaseId, string extraWhere, Guid? tenantId)
    {
        if (hasKnowledgeBaseId)
        {
            var tenantPredicate = RagTenantSqlFilter.BuildPredicate(tenantId, columnQualifier: "");
            return $"""
              SELECT "Id", "Content", "DocumentId", "KnowledgeBaseId", "ChunkIndex", "Metadata", "ParentChunkId",
                     1 - ("Embedding" <=> @query::vector) AS score
              FROM "{chunkTable}"
              WHERE "KnowledgeBaseId" = @kbId{extraWhere}{tenantPredicate}
              ORDER BY "Embedding" <=> @query::vector
              LIMIT @topK
              """;
        }

        var joinTenantPredicate = RagTenantSqlFilter.BuildPredicate(tenantId, columnQualifier: "c.");
        return $"""
              SELECT c."Id", c."Content", c."DocumentId", c."KnowledgeBaseId", c."ChunkIndex", c."Metadata", c."ParentChunkId",
                     1 - (c."Embedding" <=> @query::vector) AS score
              FROM "{chunkTable}" c
              INNER JOIN "{knowledgeBaseTable}" kb ON c."KnowledgeBaseId" = kb."Id"
              WHERE kb."IsEnabled" = true AND kb."IsDeleted" = false{extraWhere.Replace("\"Metadata\"", "c.\"Metadata\"")}{joinTenantPredicate}
              ORDER BY c."Embedding" <=> @query::vector
              LIMIT @topK
              """;
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
