using System.Collections;
using Microsoft.EntityFrameworkCore.Query;
using Tnzi.AI.Rag.Dtos;

namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// 父文档检索只读各命中窗口里的块，不读窗口之间的块。
/// </summary>
/// <remarks>
/// ★ 此前把所有文档、所有命中压成一个全局区间 [最小窗口起点, 最大窗口终点] 再套给每篇文档：命中文档 A 第 2 块与文档 B 第 400 块，
/// 就把两篇的第 0..402 块原文全读进内存，只为拼出两个 5 块窗口；同一文档里相距很远的两处命中也一样。
/// 这里经真实的 <see cref="ParentDocumentRetriever.RetrieveAsync"/> 截下它发出的查询条件，按条件数出会取回多少行。
/// </remarks>
public class ParentDocumentWindowQueryTests
{
    private const int ChunksPerDocument = 700;

    private readonly Guid _docA = Guid.NewGuid();
    private readonly Guid _docB = Guid.NewGuid();
    private readonly List<DocumentChunk> _chunks;
    private readonly List<Expression> _issued = [];

    public ParentDocumentWindowQueryTests()
    {
        _chunks = [.. Chunks(_docA), .. Chunks(_docB)];
    }

    [Fact]
    public async Task HitsInTwoLongDocuments_FetchOnlyTheTwoWindows()
    {
        var results = await Retrieve([Match(_docA, 2), Match(_docB, 400)], windowSize: 2);

        RowsFetched().ShouldBe(10);
        results.Count.ShouldBe(2);
        results.Single(r => r.DocumentId == _docA).MergedContent.ShouldBe(Window(_docA, 0, 4));
        results.Single(r => r.DocumentId == _docB).MergedContent.ShouldBe(Window(_docB, 398, 402));
    }

    [Fact]
    public async Task FarApartHitsInOneDocument_FetchOnlyTheirWindows()
    {
        var results = await Retrieve([Match(_docA, 3), Match(_docA, 650)], windowSize: 2);

        RowsFetched().ShouldBe(10);
        results.Select(r => (r.StartChunkIndex, r.EndChunkIndex)).ShouldBe([(1, 5), (648, 652)], ignoreOrder: true);
    }

    [Fact]
    public async Task OverlappingWindows_AreFetchedOnce()
    {
        var results = await Retrieve([Match(_docA, 10), Match(_docA, 12)], windowSize: 2);

        RowsFetched().ShouldBe(7);
        var block = results.ShouldHaveSingleItem();
        block.StartChunkIndex.ShouldBe(8);
        block.EndChunkIndex.ShouldBe(14);
    }

    [Fact]
    public void WindowPredicate_TranslatesToParameterizedSql_OnPostgreSql()
    {
        // 上面几条按内存执行数行数；这一条确认同一个条件在 Npgsql 提供程序下能整体翻译成 SQL（翻不了 EF 会抛，而不是退回客户端求值），
        // 且窗口的值是查询参数而不是内联字面量。只生成 SQL，不连库。
        using var context = new ChunkQueryContext();
        var predicate = ParentDocumentRetriever.BuildWindowPredicate(
        [
            new ParentDocumentRetriever.ChunkWindow(_docA, 1, 5),
            new ParentDocumentRetriever.ChunkWindow(_docB, 648, 652)
        ]);

        var sql = context.Set<DocumentChunk>().Where(predicate).Select(c => c.Content).ToQueryString();

        // ToQueryString 先以 "-- @参数='值'" 注释列出参数，其后才是 SQL 本身
        var statement = string.Join("\n", sql.Split('\n').Where(line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal)));
        statement.ShouldContain("\"ChunkIndex\" >= @");
        statement.ShouldContain("\"ChunkIndex\" <= @");
        statement.ShouldContain(" OR ");
        statement.ShouldNotContain("648");
        statement.ShouldNotContain(_docB.ToString());
    }

    private async Task<IReadOnlyList<ParentDocumentResult>> Retrieve(List<RetrievalResult> matches, int windowSize)
    {
        var chunkRepo = new Mock<IRepository<DocumentChunk, Guid>>();
        chunkRepo.Setup(r => r.AsQueryable(It.IsAny<bool>()))
            .Returns(() => new CapturingQueryable<DocumentChunk>(_chunks.BuildMock(), _issued));
        var docRepo = new Mock<IRepository<KnowledgeDocument, Guid>>();
        docRepo.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(new List<KnowledgeDocument>().BuildMock());
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(sp => sp.GetService(typeof(ILoggerFactory))).Returns(NullLoggerFactory.Instance);

        return await new ParentDocumentRetriever(serviceProvider.Object, chunkRepo.Object, docRepo.Object)
            .RetrieveAsync(matches, new ParentRetrievalOptions { WindowSize = windowSize, MaxTokens = 100_000 });
    }

    /// <summary>按实际发出的 Where 条件数出它会取回的行数。</summary>
    private int RowsFetched()
    {
        var where = _issued.OfType<MethodCallExpression>()
            .Single(call => call.Method.Name == nameof(Queryable.Where));
        var predicate = (Expression<Func<DocumentChunk, bool>>)((UnaryExpression)where.Arguments[1]).Operand;
        return _chunks.Count(predicate.Compile());
    }

    private static List<DocumentChunk> Chunks(Guid documentId)
        => Enumerable.Range(0, ChunksPerDocument)
            .Select(i => new DocumentChunk { Id = Guid.NewGuid(), DocumentId = documentId, ChunkIndex = i, Content = $"{documentId:N}-{i}" })
            .ToList();

    private static string Window(Guid documentId, int start, int end)
        => string.Join("\n", Enumerable.Range(start, end - start + 1).Select(i => $"{documentId:N}-{i}"));

    private static RetrievalResult Match(Guid documentId, int chunkIndex)
        => new()
        {
            Content = "match",
            Score = 0.9,
            DocumentId = documentId,
            Metadata = new Dictionary<string, object> { ["chunkIndex"] = chunkIndex }
        };

    /// <summary>只映射分块表的 Npgsql 上下文，用来看查询会被翻译成什么 SQL（不打开连接）。</summary>
    private sealed class ChunkQueryContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseNpgsql("Host=localhost;Database=translation_only", npgsql => npgsql.UseVector());

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var chunk = modelBuilder.Entity<DocumentChunk>();
            chunk.HasKey(c => c.Id);
            chunk.Property(c => c.Embedding).HasColumnType("vector(3)");
        }
    }

    /// <summary>记下经它构造的每一个查询表达式，执行交给内层（MockQueryable）。</summary>
    private sealed class CapturingQueryable<T>(IQueryable<T> inner, List<Expression> issued) : IOrderedQueryable<T>, IAsyncEnumerable<T>
    {
        public Type ElementType => inner.ElementType;
        public Expression Expression => inner.Expression;
        public IQueryProvider Provider => new CapturingProvider(inner.Provider, issued);
        public IEnumerator<T> GetEnumerator() => inner.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            => ((IAsyncEnumerable<T>)inner).GetAsyncEnumerator(cancellationToken);
    }

    private sealed class CapturingProvider(IQueryProvider inner, List<Expression> issued) : IAsyncQueryProvider
    {
        public IQueryable CreateQuery(Expression expression) => throw new NotSupportedException();

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        {
            issued.Add(expression);
            return new CapturingQueryable<TElement>(inner.CreateQuery<TElement>(expression), issued);
        }

        public object? Execute(Expression expression) => inner.Execute(expression);

        public TResult Execute<TResult>(Expression expression) => inner.Execute<TResult>(expression);

        public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
            => ((IAsyncQueryProvider)inner).ExecuteAsync<TResult>(expression, cancellationToken);
    }
}
