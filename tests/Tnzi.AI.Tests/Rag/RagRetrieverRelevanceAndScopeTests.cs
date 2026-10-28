using System.Diagnostics.Metrics;
using Tnzi.AI.Rag.Dtos;
using Tnzi.AI.Rag.Observability;

namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// <see cref="RagRetriever"/> 的两条范围约定：<c>MinRelevance</c> 比的是检索给出的绝对相关度（不是归一化后的名次），
/// 已禁用的知识库不进任何按库检索。
/// </summary>
/// <remarks>
/// <para>
/// ★ 此前阈值作用在 min-max 归一化之后：最高一条恒为 1.0、最低一条恒为 0。默认阈值 0.3 下，五条都高度相关的结果
/// 丢掉末尾两条；只命中一条毫不相关的分块时它被归一成 1.0、照样通过，还以 <c>relevance: 1.00</c> 写进 prompt 与引用。
/// </para>
/// <para>
/// ★ 此前按库检索只按 id 加载知识库、不看 <c>IsEnabled</c>：管理员禁用一个内容过期的库后，被授权它的 agent 照样检索、
/// 引用它，只有 search-all 不再返回它。图谱片段同样。
/// </para>
/// 用真实的后处理管线（去重、归一化、加权递减），与 DI 里注册的一致。
/// </remarks>
public class RagRetrieverRelevanceAndScopeTests
{
    private static readonly float[] QueryVector = [0.1f];

    private readonly Mock<IEmbeddingService> _embedding = new();
    private readonly Mock<IVectorStore> _vectorStore = new();
    private readonly Mock<IReranker> _reranker = new();
    private readonly Mock<IRepository<KnowledgeBase, Guid>> _kbRepo = new();
    private readonly Mock<IGraphSearchService> _graph = new();
    private readonly Guid _kb = Guid.NewGuid();

    public RagRetrieverRelevanceAndScopeTests()
    {
        _embedding
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success(QueryVector));
        _reranker
            .Setup(r => r.RerankAsync(It.IsAny<string>(), It.IsAny<List<VectorSearchResult>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, List<VectorSearchResult> results, int topK, CancellationToken _) => results.Take(topK).ToList());
        _graph
            .Setup(g => g.SearchAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<GraphSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GraphSearchResult>());
        UseKnowledgeBases(new KnowledgeBase { Id = _kb, Name = "kb" });
    }

    // =====================================================================
    // MinRelevance 比绝对相关度
    // =====================================================================

    [Fact]
    public async Task SingleIrrelevantHit_IsFilteredOut_ByTheDefaultThreshold()
    {
        ReturnFromKb(_kb, (0.05, "The cafeteria menu changes every Tuesday."));

        var results = await CreateRetriever().RetrieveAsync("refund policy", Scoped(minRelevance: 0.3));

        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task CloselyScoredRelevantHits_AreAllKept()
    {
        ReturnFromKb(_kb,
            (0.82, "Refunds are issued within 14 days of the return."),
            (0.81, "A refund requires the original receipt."),
            (0.80, "Store credit is offered when no receipt exists."),
            (0.79, "Opened software cannot be returned for cash."),
            (0.78, "Gift cards are not refundable."));

        var results = await CreateRetriever().RetrieveAsync("refund policy", Scoped(minRelevance: 0.3));

        results.Count.ShouldBe(5);
    }

    [Fact]
    public async Task ReturnedScore_IsTheAbsoluteRelevance_NotTheNormalizedRank()
    {
        ReturnFromKb(_kb,
            (0.62, "Refunds are issued within 14 days of the return."),
            (0.41, "Store credit is offered when no receipt exists."));

        var results = await CreateRetriever().RetrieveAsync("refund policy", Scoped(minRelevance: 0));

        results.Select(r => r.Score).ShouldBe([0.62, 0.41]);
    }

    [Fact]
    public async Task Threshold_DropsOnlyWhatIsBelowIt()
    {
        ReturnFromKb(_kb,
            (0.71, "Refunds are issued within 14 days of the return."),
            (0.52, "Store credit is offered when no receipt exists."),
            (0.12, "The cafeteria menu changes every Tuesday."));

        var results = await CreateRetriever().RetrieveAsync("refund policy", Scoped(minRelevance: 0.3));

        results.Select(r => r.Content).ShouldBe(
            ["Refunds are issued within 14 days of the return.", "Store credit is offered when no receipt exists."]);
    }

    // =====================================================================
    // 已禁用的知识库不进按库检索
    // =====================================================================

    [Fact]
    public async Task DisabledKnowledgeBase_IsNotSearched_ByVectorOrGraph()
    {
        var disabled = Guid.NewGuid();
        UseKnowledgeBases(
            new KnowledgeBase { Id = _kb, Name = "enabled" },
            new KnowledgeBase { Id = disabled, Name = "disabled", IsEnabled = false });
        ReturnFromKb(_kb, (0.9, "enabled content"));
        ReturnFromKb(disabled, (0.95, "stale content"));

        var results = await CreateRetriever().RetrieveAsync(
            "q", new RagRetrievalOptions { KnowledgeBaseIds = [disabled, _kb], MinRelevance = 0 });

        results.ShouldHaveSingleItem().Content.ShouldBe("enabled content");
        _vectorStore.Verify(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), disabled, It.IsAny<CancellationToken>()), Times.Never);
        _graph.Verify(g => g.SearchAsync(It.IsAny<string>(), disabled, It.IsAny<GraphSearchOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        _graph.Verify(g => g.SearchAsync(It.IsAny<string>(), _kb, It.IsAny<GraphSearchOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnlyDisabledKnowledgeBases_ReturnNothing_WithoutEmbeddingTheQuery()
    {
        var disabled = Guid.NewGuid();
        UseKnowledgeBases(new KnowledgeBase { Id = disabled, Name = "disabled", IsEnabled = false });
        ReturnFromKb(disabled, (0.95, "stale content"));

        var results = await CreateRetriever().RetrieveAsync(
            "q", new RagRetrievalOptions { KnowledgeBaseIds = [disabled], MinRelevance = 0 });

        results.ShouldBeEmpty();
        _embedding.Verify(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        _graph.Verify(g => g.SearchAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<GraphSearchOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task VectorTextSearch_ForADisabledGrantedKnowledgeBase_InjectsNothing()
    {
        // agent 主路径：授权里的库被禁用后，agent 的检索里就不再有它
        var disabled = Guid.NewGuid();
        UseKnowledgeBases(new KnowledgeBase { Id = disabled, Name = "disabled", IsEnabled = false });
        ReturnFromKb(disabled, (0.95, "stale content"));
        var docRepo = new Mock<IRepository<KnowledgeDocument, Guid>>();
        docRepo.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(new List<KnowledgeDocument>().BuildMock());

        var search = new VectorTextSearchService(LoggingServiceProvider(), CreateRetriever(), docRepo.Object);
        var results = await search.SearchAsync("q", new TextSearchFilter { KnowledgeBaseIds = [disabled] });

        results.ShouldBeEmpty();
    }

    // =====================================================================
    // 加权递减重排：权重乘在相关度上，同一内容多次命中先聚合再去重
    // =====================================================================

    /// <summary>
    /// ★ 此前权重乘在 min-max 归一化之后的分数上，再被合并上限（默认 1.0）截回 1.0：加权库里分数较高的结果与另一库的
    /// 头名并列 1.0，改按块序排，权重 1.5 的库反而排在后面。
    /// </summary>
    [Fact]
    public async Task KnowledgeBaseWeight_RaisesItsResultsAboveAHigherScoringResultOfAnotherBase()
    {
        var weighted = Guid.NewGuid();
        var other = Guid.NewGuid();
        UseKnowledgeBases(new KnowledgeBase { Id = weighted, Name = "weighted" }, new KnowledgeBase { Id = other, Name = "other" });
        // 低分的那条排在块序前面：上限把两条都压成 1.0 时会改按块序排，顺序就反了
        ReturnFromKb(weighted, (0.75, "Weighted base: receipts are required."), (0.80, "Weighted base: returns within thirty days."));
        ReturnFromKb(other, (0.90, "Other base: store credit policy."), (0.50, "Other base: gift card terms."));

        var options = new AIRagOptions();
        options.Reranker.KnowledgeBaseWeights[weighted.ToString()] = 1.5;

        var results = await CreateRetriever(options).RetrieveAsync("refund policy",
            new RagRetrievalOptions { KnowledgeBaseIds = [weighted, other], TopK = 10, MinRelevance = 0 });

        results.Select(r => r.Content).ShouldBe(
        [
            "Weighted base: returns within thirty days.",
            "Weighted base: receipts are required.",
            "Other base: store credit policy.",
            "Other base: gift card terms."
        ]);
    }

    /// <summary>
    /// ★ 此前递减聚合排在去重之后：逐字相同的内容 Jaccard 为 1.0，早被去重删掉，每组只剩一条，DiminishingFactor 从不起作用。
    /// </summary>
    [Fact]
    public async Task SameContentFoundInTwoBases_IsMergedIntoOneResultThatOutranksASingleHit()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        UseKnowledgeBases(new KnowledgeBase { Id = first, Name = "first" }, new KnowledgeBase { Id = second, Name = "second" });
        ReturnFromKb(first, (0.60, "Refunds are issued within fourteen days."));
        ReturnFromKb(second, (0.60, "Refunds are issued within fourteen days."), (0.70, "Exchanges need the original packaging."));

        var results = await CreateRetriever().RetrieveAsync("refund policy",
            new RagRetrievalOptions { KnowledgeBaseIds = [first, second], TopK = 10, MinRelevance = 0 });

        results.Select(r => r.Content).ShouldBe(
        [
            "Refunds are issued within fourteen days.",
            "Exchanges need the original packaging."
        ]);
        // 报回的仍是绝对相关度，不是合并后的排序分
        results[0].Score.ShouldBe(0.60, tolerance: 1e-9);
    }

    // =====================================================================
    // 指标：用户端引擎与默认 agent 检索共同走的这条路径也记 rag.search.*
    // =====================================================================

    [Fact]
    public async Task Retrieval_RecordsOneSearch_TaggedWithTheKnowledgeBase()
    {
        ReturnFromKb(_kb, (0.82, "Refunds are issued within 14 days of the return."));
        using var capture = new RagMetricCapture();

        await CreateRetriever().RetrieveAsync("refund policy", Scoped(minRelevance: 0.3));

        capture.Count("rag.search.count", "rag.knowledge_base.id", _kb.ToString()).ShouldBe(1);
        capture.Count("rag.search.duration", "rag.knowledge_base.id", _kb.ToString()).ShouldBe(1);
    }

    [Fact]
    public async Task Retrieval_ThatFails_RecordsARetrievalError()
    {
        _vectorStore
            .Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), _kb, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RetrievalProbeException());
        using var capture = new RagMetricCapture();

        var results = await CreateRetriever().RetrieveAsync("refund policy", Scoped(minRelevance: 0.3));

        results.ShouldBeEmpty();
        capture.Count("rag.error.count", "error.type", typeof(RetrievalProbeException).FullName!).ShouldBe(1);
        capture.Count("rag.search.count", "rag.knowledge_base.id", _kb.ToString()).ShouldBe(0);
    }

    [Fact]
    public async Task Retrieval_CancelledByTheCaller_IsNotRecordedAsAnError()
    {
        using var cancelled = new CancellationTokenSource();
        _vectorStore
            .Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), _kb, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await cancelled.CancelAsync();
                throw new RetrievalCancelledProbeException(cancelled.Token);
            });
        using var capture = new RagMetricCapture();

        var results = await CreateRetriever().RetrieveAsync("refund policy", Scoped(minRelevance: 0.3), cancelled.Token);

        results.ShouldBeEmpty();
        capture.Count("rag.error.count", "error.type", typeof(RetrievalCancelledProbeException).FullName!).ShouldBe(0);
    }

    private sealed class RetrievalProbeException() : Exception("vector store unavailable");

    private sealed class RetrievalCancelledProbeException(CancellationToken token) : OperationCanceledException(token);

    /// <summary>
    /// 捕获 RAG Meter 的测量值。监听器是进程级的，并行跑的别的测试也会记：断言一律按本测试独有的标签值筛。
    /// </summary>
    private sealed class RagMetricCapture : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Instrument, KeyValuePair<string, object?>[] Tags)> _measurements = [];
        private readonly object _gate = new();

        public RagMetricCapture()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == RagActivitySource.Name)
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => Record(instrument, tags));
            _listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) => Record(instrument, tags));
            _listener.Start();
        }

        private void Record(Instrument instrument, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            lock (_gate)
                _measurements.Add((instrument.Name, tags.ToArray()));
        }

        public int Count(string instrument, string tag, string value)
        {
            lock (_gate)
                return _measurements.Count(m => m.Instrument == instrument
                    && m.Tags.Any(t => t.Key == tag && Equals(t.Value as string, value)));
        }

        public void Dispose() => _listener.Dispose();
    }

    private RagRetrievalOptions Scoped(double minRelevance) => new() { KnowledgeBaseIds = [_kb], TopK = 10, MinRelevance = minRelevance };

    private void UseKnowledgeBases(params KnowledgeBase[] knowledgeBases)
        => _kbRepo.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(knowledgeBases.ToList().BuildMock());

    private void ReturnFromKb(Guid kbId, params (double Score, string Content)[] hits)
        => _vectorStore
            .Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), kbId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hits.Select((h, i) => new VectorSearchResult
            {
                Id = Guid.NewGuid(),
                Content = h.Content,
                DocumentId = Guid.NewGuid(),
                KnowledgeBaseId = kbId,
                ChunkIndex = i,
                Score = h.Score
            }).ToList());

    private static IServiceProvider LoggingServiceProvider()
    {
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(sp => sp.GetService(typeof(ILoggerFactory))).Returns(NullLoggerFactory.Instance);
        return serviceProvider.Object;
    }

    private RagRetriever CreateRetriever(AIRagOptions? ragOptions = null)
    {
        var options = new StaticOptionsMonitor<AIRagOptions>(ragOptions ?? new AIRagOptions());
        ISearchPostProcessor[] postProcessors =
        [
            new DeduplicationPostProcessor(NullLogger<DeduplicationPostProcessor>.Instance),
            new ScoreNormalizationPostProcessor(NullLogger<ScoreNormalizationPostProcessor>.Instance),
            new WeightedDiminishingReranker(options, NullLogger<WeightedDiminishingReranker>.Instance)
        ];

        return new RagRetriever(
            LoggingServiceProvider(),
            _embedding.Object,
            _vectorStore.Object,
            _reranker.Object,
            _kbRepo.Object,
            postProcessors,
            options,
            graphSearchService: _graph.Object);
    }
}
