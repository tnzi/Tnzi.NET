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
    private readonly ICurrentTenant? _currentTenant;
    private readonly bool _multiTenancyEnabled;
    private readonly string _connectionString;
    private readonly string _chunkTable;
    private readonly string _knowledgeBaseTable;

    public PgFullTextSearchProvider(
        IConfiguration configuration,
        IOptionsSnapshot<AIRagOptions> options,
        ILogger<PgFullTextSearchProvider> logger,
        ICurrentTenant? currentTenant = null,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null)
    {
        Check.NotNull(configuration);
        _logger = Check.NotNull(logger);
        _currentTenant = currentTenant;
        _multiTenancyEnabled = multiTenancyOptions?.Value.Enabled ?? false;
        _connectionString = RagConnectionStringResolver.Resolve(configuration, nameof(PgFullTextSearchProvider));

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

        // 多租户隔离：仅当存在非空租户上下文时追加 TenantId 谓词（单租户 / host 不追加，见 RagTenantSqlFilter）。
        // fail-closed：MT 启用且无租户上下文时禁止无 KB 范围的跨库搜索（守卫先于连接打开）。
        var tenantId = _currentTenant?.Id;
        RagTenantSqlFilter.EnsureTenantScopeForSearchAll(_multiTenancyEnabled, tenantId, knowledgeBaseId.HasValue);

        var sw = Stopwatch.StartNew();
        var results = new List<KeywordSearchResult>();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var sql = BuildSearchSql(_chunkTable, _knowledgeBaseTable, knowledgeBaseId.HasValue, tenantId);

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add(new NpgsqlParameter("query", NpgsqlDbType.Text) { Value = query });
        cmd.Parameters.Add(new NpgsqlParameter("topK", NpgsqlDbType.Integer) { Value = topK });

        if (knowledgeBaseId.HasValue)
        {
            cmd.Parameters.Add(new NpgsqlParameter("kbId", NpgsqlDbType.Uuid) { Value = knowledgeBaseId.Value });
        }

        RagTenantSqlFilter.AddParameter(cmd, tenantId);

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

    /// <summary>
    /// 组合全文搜索 SQL —— 提取为静态方法以便对租户谓词的有无进行单元测试（无需真实 PostgreSQL）。
    /// </summary>
    /// <param name="chunkTable">chunk 表名（已含前缀，不含引号）</param>
    /// <param name="knowledgeBaseTable">knowledge base 表名（已含前缀，不含引号）</param>
    /// <param name="hasKnowledgeBaseId">是否按指定知识库过滤（单表查询）</param>
    /// <param name="tenantId">当前租户 ID；null 时不追加租户谓词</param>
    public static string BuildSearchSql(
        string chunkTable, string knowledgeBaseTable, bool hasKnowledgeBaseId, Guid? tenantId)
    {
        if (hasKnowledgeBaseId)
        {
            var tenantPredicate = RagTenantSqlFilter.BuildPredicate(tenantId, columnQualifier: "");
            return $"""
              SELECT "Id", "Content", "DocumentId", "KnowledgeBaseId",
                     ts_rank(to_tsvector('english', "Content"), plainto_tsquery('english', @query)) AS score
              FROM "{chunkTable}"
              WHERE "KnowledgeBaseId" = @kbId
                AND to_tsvector('english', "Content") @@ plainto_tsquery('english', @query){tenantPredicate}
              ORDER BY score DESC
              LIMIT @topK
              """;
        }

        var joinTenantPredicate = RagTenantSqlFilter.BuildPredicate(tenantId, columnQualifier: "c.");
        return $"""
              SELECT c."Id", c."Content", c."DocumentId", c."KnowledgeBaseId",
                     ts_rank(to_tsvector('english', c."Content"), plainto_tsquery('english', @query)) AS score
              FROM "{chunkTable}" c
              INNER JOIN "{knowledgeBaseTable}" kb ON c."KnowledgeBaseId" = kb."Id"
              WHERE kb."IsEnabled" = true AND kb."IsDeleted" = false
                AND to_tsvector('english', c."Content") @@ plainto_tsquery('english', @query){joinTenantPredicate}
              ORDER BY score DESC
              LIMIT @topK
              """;
    }
}
