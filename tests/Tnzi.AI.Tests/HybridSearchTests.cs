using Tnzi.AI.Rag.Dtos;
namespace Tnzi.AI.Tests;

/// <summary>
/// HybridSearchService + InMemoryKeywordSearchProvider 单元测试
/// </summary>
public class HybridSearchTests
{
    #region RRF 融合测试

    [Fact]
    public void RrfFusion_MergesScoresCorrectly()
    {
        // 准备 HybridSearchService（通过反射或直接调用 internal 方法测试 RRF 逻辑）
        var hybridOptions = new AIRagOptions
        {
            HybridSearch = new HybridSearchOptions
            {
                VectorWeight = 0.7,
                KeywordWeight = 0.3,
                FusionConstantK = 60
            }
        };

        var service = CreateHybridSearchService(hybridOptions);

        var chunkIdBoth = Guid.NewGuid();
        var chunkIdVectorOnly = Guid.NewGuid();
        var chunkIdKeywordOnly = Guid.NewGuid();

        var vectorResults = new List<VectorSearchResult>
        {
            new() { Id = chunkIdBoth, Content = "Both", DocumentId = Guid.NewGuid(), KnowledgeBaseId = Guid.NewGuid(), Score = 0.95 },
            new() { Id = chunkIdVectorOnly, Content = "Vector only", DocumentId = Guid.NewGuid(), KnowledgeBaseId = Guid.NewGuid(), Score = 0.85 }
        };

        var keywordResults = new List<KeywordSearchResult>
        {
            new() { ChunkId = chunkIdBoth, Content = "Both", DocumentId = vectorResults[0].DocumentId, KnowledgeBaseId = vectorResults[0].KnowledgeBaseId, Score = 5.0 },
            new() { ChunkId = chunkIdKeywordOnly, Content = "Keyword only", DocumentId = Guid.NewGuid(), KnowledgeBaseId = Guid.NewGuid(), Score = 3.0 }
        };

        var fused = service.ReciprocalRankFusion(vectorResults, keywordResults, topK: 10);

        // chunkIdBoth 同时出现在两路结果中，RRF 分数应该最高
        fused.Count.ShouldBe(3);
        fused[0].Id.ShouldBe(chunkIdBoth);

        // 验证分数计算（RRF: alpha/(k+rank) + beta/(k+rank)）
        // chunkIdBoth: 0.7/(60+1) + 0.3/(60+1) = 1.0/61 ≈ 0.01639
        var expectedBothScore = 0.7 / 61.0 + 0.3 / 61.0;
        fused[0].Score.ShouldBe(expectedBothScore, tolerance: 0.0001);
    }

    [Fact]
    public void RrfFusion_DeduplicatesByChunkId()
    {
        var hybridOptions = new AIRagOptions
        {
            HybridSearch = new HybridSearchOptions
            {
                VectorWeight = 0.6,
                KeywordWeight = 0.4,
                FusionConstantK = 60
            }
        };

        var service = CreateHybridSearchService(hybridOptions);

        var sharedChunkId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        var kbId = Guid.NewGuid();

        var vectorResults = new List<VectorSearchResult>
        {
            new() { Id = sharedChunkId, Content = "Shared chunk", DocumentId = docId, KnowledgeBaseId = kbId, Score = 0.9 }
        };

        var keywordResults = new List<KeywordSearchResult>
        {
            new() { ChunkId = sharedChunkId, Content = "Shared chunk", DocumentId = docId, KnowledgeBaseId = kbId, Score = 4.0 }
        };

        var fused = service.ReciprocalRankFusion(vectorResults, keywordResults, topK: 10);

        // 相同 ChunkId 只出现一次（去重）
        fused.Count.ShouldBe(1);
        fused[0].Id.ShouldBe(sharedChunkId);
    }

    [Fact]
    public void RrfFusion_EmptyInputs_ReturnsEmpty()
    {
        var service = CreateHybridSearchService(new AIRagOptions());

        var fused = service.ReciprocalRankFusion([], [], topK: 10);

        fused.ShouldBeEmpty();
    }

    [Fact]
    public void RrfFusion_TopKLimitsResults()
    {
        var service = CreateHybridSearchService(new AIRagOptions());

        var vectorResults = Enumerable.Range(0, 10)
            .Select(i => new VectorSearchResult
            {
                Id = Guid.NewGuid(),
                Content = $"V{i}",
                DocumentId = Guid.NewGuid(),
                KnowledgeBaseId = Guid.NewGuid(),
                Score = 0.9 - i * 0.05
            })
            .ToList();

        var fused = service.ReciprocalRankFusion(vectorResults, [], topK: 3);

        fused.Count.ShouldBe(3);
    }

    #endregion

    #region SearchAsync 端到端管线测试（嵌入对齐 + 双路 + RRF + 重排 + 映射）

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmpty()
    {
        var (service, _) = CreateHybridSearchServiceWithMocks(new AIRagOptions());

        (await service.SearchAsync("")).ShouldBeEmpty();
        (await service.SearchAsync("   ")).ShouldBeEmpty();
    }

    [Fact]
    public async Task SearchAsync_UsesRagDefaultEmbeddingProviderAndModel()
    {
        // 锁定 #1 修复：查询向量必须用 RAG 全局默认 embedding 配置，
        // 而非回退到 AI 顶层 DefaultProvider（否则向量空间不一致）。
        var options = new AIRagOptions
        {
            DefaultEmbeddingProvider = "rag-embed-provider",
            DefaultEmbeddingModel = "rag-embed-model"
        };
        var (service, mocks) = CreateHybridSearchServiceWithMocks(options);

        EmbeddingOptions? capturedOptions = null;
        mocks.Embedding
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, EmbeddingOptions?, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(Result<float[]>.Success(new[] { 0.1f, 0.2f }));

        mocks.VectorStore
            .Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>());
        mocks.Keyword
            .Setup(k => k.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KeywordSearchResult>());

        await service.SearchAsync("how does hybrid search work", Scoped());

        capturedOptions.ShouldNotBeNull();
        capturedOptions!.Provider.ShouldBe("rag-embed-provider");
        capturedOptions.Model.ShouldBe("rag-embed-model");
    }

    [Fact]
    public async Task SearchAsync_FusesBothPaths_RerankThenMapsToTextSearchResults()
    {
        var options = new AIRagOptions
        {
            DefaultEmbeddingProvider = "openai",
            DefaultEmbeddingModel = "text-embedding-3-small",
            HybridSearch = new HybridSearchOptions
            {
                VectorWeight = 0.6,
                KeywordWeight = 0.4,
                FusionConstantK = 60
            }
        };
        var (service, mocks) = CreateHybridSearchServiceWithMocks(options);

        var queryVector = new[] { 0.1f, 0.2f, 0.3f };
        mocks.Embedding
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success(queryVector));

        var docVector = Guid.NewGuid();
        var docKeyword = Guid.NewGuid();
        var kbId = Guid.NewGuid();
        var chunkVectorOnly = Guid.NewGuid();
        var chunkKeywordOnly = Guid.NewGuid();

        // 向量路命中 chunkVectorOnly；关键词路命中 chunkKeywordOnly（无重叠 → 融合后 2 条）
        mocks.VectorStore
            .Setup(v => v.SearchAsync(queryVector, It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>
            {
                new() { Id = chunkVectorOnly, Content = "Vector hit", DocumentId = docVector, KnowledgeBaseId = kbId, ChunkIndex = 2, Score = 0.95 }
            });
        mocks.Keyword
            .Setup(k => k.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KeywordSearchResult>
            {
                new() { ChunkId = chunkKeywordOnly, Content = "Keyword hit", DocumentId = docKeyword, KnowledgeBaseId = kbId, ChunkIndex = 5, Score = 4.0 }
            });

        // Reranker 透传（验证融合结果交给 reranker 且其输出被映射）
        List<VectorSearchResult>? rerankInput = null;
        mocks.Reranker
            .Setup(r => r.RerankAsync(It.IsAny<string>(), It.IsAny<List<VectorSearchResult>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<string, List<VectorSearchResult>, int, CancellationToken>((_, fused, _, _) => rerankInput = fused)
            .ReturnsAsync((string _, List<VectorSearchResult> fused, int topK, CancellationToken _) => fused.Take(topK).ToList());

        var docs = new List<KnowledgeDocument>
        {
            new() { Id = docVector, FileName = "vector-doc.pdf", KnowledgeBaseId = kbId },
            new() { Id = docKeyword, FileName = "keyword-doc.md", KnowledgeBaseId = kbId }
        };
        mocks.DocRepo.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(docs.BuildMock());

        var results = (await service.SearchAsync("query text", Scoped(), maxResults: 5)).ToList();

        // 融合后 2 条都进入 reranker
        rerankInput.ShouldNotBeNull();
        rerankInput!.Count.ShouldBe(2);

        // 映射结果：2 条，含正确 SourceName + hybrid searchType metadata
        results.Count.ShouldBe(2);
        results.ShouldContain(r => r.Text == "Vector hit" && r.SourceName == "vector-doc.pdf");
        results.ShouldContain(r => r.Text == "Keyword hit" && r.SourceName == "keyword-doc.md");
        foreach (var r in results)
        {
            r.Metadata.ShouldNotBeNull();
            r.Metadata!["searchType"].ShouldBe("hybrid");
            r.Metadata["knowledgeBaseId"].ShouldBe(kbId);
        }

        // 双路各检索一次（fetchCount = maxResults * 2）
        mocks.VectorStore.Verify(v => v.SearchAsync(queryVector, 10, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
        mocks.Keyword.Verify(k => k.SearchAsync("query text", 10, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_WithKbFilter_ScopesBothPaths_AndIncludesGraphResults()
    {
        // agent 路径带着知识库范围进来：向量与关键词两路都必须按库检索，图谱片段也只在这些库里找
        var kbId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        var graph = new Mock<IGraphSearchService>();
        graph
            .Setup(g => g.SearchAsync("query", kbId, It.IsAny<GraphSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GraphSearchResult> { new("Acme", "Organization", [], 0.8, "Acme acquired Foo") });
        var (service, mocks) = CreateHybridSearchServiceWithMocks(new AIRagOptions(), graph.Object);
        mocks.UseKnowledgeBases(new KnowledgeBase { Id = kbId, Name = "kb" });

        var queryVector = new[] { 0.1f };
        mocks.Embedding
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success(queryVector));
        mocks.VectorStore
            .Setup(v => v.SearchAsync(queryVector, It.IsAny<int>(), kbId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>
            {
                new() { Id = Guid.NewGuid(), Content = "Vector hit", DocumentId = docId, KnowledgeBaseId = kbId, Score = 0.9 }
            });
        mocks.Keyword
            .Setup(k => k.SearchAsync("query", It.IsAny<int>(), kbId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KeywordSearchResult>());
        mocks.Reranker
            .Setup(r => r.RerankAsync(It.IsAny<string>(), It.IsAny<List<VectorSearchResult>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, List<VectorSearchResult> fused, int topK, CancellationToken _) => fused.Take(topK).ToList());
        mocks.DocRepo.Setup(r => r.AsQueryable(It.IsAny<bool>()))
            .Returns(new List<KnowledgeDocument> { new() { Id = docId, FileName = "doc.md", KnowledgeBaseId = kbId } }.BuildMock());

        var results = (await service.SearchAsync("query", new TextSearchFilter { KnowledgeBaseIds = [kbId] }, maxResults: 5)).ToList();

        results.Count.ShouldBe(2);
        results.ShouldContain(r => r.Text == "Vector hit" && r.SourceName == "doc.md" && (string)r.Metadata!["searchType"]! == "hybrid");
        results.ShouldContain(r => r.Text == "Acme acquired Foo" && (string)r.Metadata!["searchType"]! == "graph");
        // 两路都没有走 search-all
        mocks.VectorStore.Verify(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), null, It.IsAny<CancellationToken>()), Times.Never);
        mocks.Keyword.Verify(k => k.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), null, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchAsync_WithParentRetrievalEnabled_ExpandsToParentWindow()
    {
        var docId = Guid.NewGuid();
        var parent = new Mock<IParentDocumentRetriever>();
        parent
            .Setup(p => p.RetrieveAsync(It.IsAny<IReadOnlyList<RetrievalResult>>(), It.IsAny<ParentRetrievalOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParentDocumentResult>
            {
                new() { DocumentId = docId, StartChunkIndex = 1, EndChunkIndex = 3, MergedContent = "wider window", Score = 0.9, DocumentName = "doc.md" }
            });
        var options = new AIRagOptions { ParentDocumentRetrieval = new ParentDocumentRetrievalOptions { Enabled = true } };
        var (service, mocks) = CreateHybridSearchServiceWithMocks(options, parentDocumentRetriever: parent.Object);

        var queryVector = new[] { 0.1f };
        mocks.Embedding
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success(queryVector));
        mocks.VectorStore
            .Setup(v => v.SearchAsync(queryVector, It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>
            {
                new() { Id = Guid.NewGuid(), Content = "small chunk", DocumentId = docId, KnowledgeBaseId = Guid.NewGuid(), ChunkIndex = 2, Score = 0.9 }
            });
        mocks.Keyword
            .Setup(k => k.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KeywordSearchResult>());
        mocks.Reranker
            .Setup(r => r.RerankAsync(It.IsAny<string>(), It.IsAny<List<VectorSearchResult>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, List<VectorSearchResult> fused, int topK, CancellationToken _) => fused.Take(topK).ToList());
        mocks.DocRepo.Setup(r => r.AsQueryable(It.IsAny<bool>()))
            .Returns(new List<KnowledgeDocument> { new() { Id = docId, FileName = "doc.md" } }.BuildMock());

        var results = (await service.SearchAsync("query", Scoped(), maxResults: 5)).ToList();

        var only = results.ShouldHaveSingleItem();
        only.Text.ShouldBe("wider window");
        only.Metadata!["searchType"].ShouldBe("parent_document");
    }

    [Fact]
    public async Task SearchAsync_ExceptionThrown_ReturnsEmptyGracefully()
    {
        var (service, mocks) = CreateHybridSearchServiceWithMocks(new AIRagOptions());

        mocks.Embedding
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        (await service.SearchAsync("query", Scoped())).ShouldBeEmpty();
    }

    [Fact]
    public async Task SearchAsync_EmbeddingFails_FallsBackToKeywordResults()
    {
        // 嵌入失败只让向量路缺席：关键词路照常检索并返回（此前整次检索连关键词一起答空）
        var (service, mocks) = CreateHybridSearchServiceWithMocks(new AIRagOptions());

        mocks.Embedding
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Failure("embedding API error"));
        mocks.Keyword
            .Setup(k => k.SearchAsync("query", It.IsAny<int>(), ScopedKb, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KeywordSearchResult>
            {
                new() { ChunkId = Guid.NewGuid(), Content = "Keyword hit", DocumentId = Guid.NewGuid(), KnowledgeBaseId = ScopedKb, ChunkIndex = 4, Score = 2.0 }
            });
        PassThroughReranker(mocks);
        mocks.DocRepo.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(new List<KnowledgeDocument>().BuildMock());

        var results = (await service.SearchAsync("query", Scoped())).ToList();

        results.ShouldHaveSingleItem().Text.ShouldBe("Keyword hit");
        mocks.VectorStore.Verify(
            v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region 知识库范围：按库对齐查询向量、禁用的库不检索

    [Fact]
    public async Task SearchAsync_KnowledgeBasesWithTheirOwnEmbeddingModels_EachSearchedWithItsOwnQueryVector()
    {
        // 混合检索的查询向量与 RagRetriever / 摄取同一解析：按 (provider, model) 分组，每组生成一次
        var (service, mocks) = CreateHybridSearchServiceWithMocks(new AIRagOptions { DefaultEmbeddingProvider = "openai" });
        var kbSmall = Guid.NewGuid();
        var kbLarge = Guid.NewGuid();
        mocks.UseKnowledgeBases(
            new KnowledgeBase { Id = kbSmall, Name = "small", EmbeddingProvider = "openai", EmbeddingModel = "text-embedding-3-small" },
            new KnowledgeBase { Id = kbLarge, Name = "large", EmbeddingProvider = "azure", EmbeddingModel = "text-embedding-3-large" });
        var smallVector = new[] { 0.1f };
        var largeVector = new[] { 0.9f };
        mocks.Embedding
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.Is<EmbeddingOptions?>(o => o!.Model == "text-embedding-3-small"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success(smallVector));
        mocks.Embedding
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.Is<EmbeddingOptions?>(o => o!.Model == "text-embedding-3-large"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success(largeVector));
        mocks.VectorStore
            .Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>());
        mocks.Keyword
            .Setup(k => k.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KeywordSearchResult>());

        await service.SearchAsync("query", new TextSearchFilter { KnowledgeBaseIds = [kbSmall, kbLarge] });

        mocks.Embedding.Verify(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        mocks.VectorStore.Verify(v => v.SearchAsync(smallVector, It.IsAny<int>(), kbSmall, It.IsAny<CancellationToken>()), Times.Once);
        mocks.VectorStore.Verify(v => v.SearchAsync(largeVector, It.IsAny<int>(), kbLarge, It.IsAny<CancellationToken>()), Times.Once);
        mocks.VectorStore.Verify(v => v.SearchAsync(smallVector, It.IsAny<int>(), kbLarge, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchAsync_OneEmbeddingGroupFails_TheOtherGroupAndKeywordsStillAnswer()
    {
        // 不设全局默认、每库各配 provider 是文档列出的正常用法；一组的 provider 不可用不能连坐另一组
        var (service, mocks) = CreateHybridSearchServiceWithMocks(new AIRagOptions { DefaultEmbeddingProvider = null });
        var kbOk = Guid.NewGuid();
        var kbBroken = Guid.NewGuid();
        mocks.UseKnowledgeBases(
            new KnowledgeBase { Id = kbOk, Name = "ok", EmbeddingProvider = "openai", EmbeddingModel = "m1" },
            new KnowledgeBase { Id = kbBroken, Name = "broken", EmbeddingProvider = "chat-only", EmbeddingModel = "m2" });
        var okVector = new[] { 0.3f };
        mocks.Embedding
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.Is<EmbeddingOptions?>(o => o!.Provider == "openai"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success(okVector));
        mocks.Embedding
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.Is<EmbeddingOptions?>(o => o!.Provider == "chat-only"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Failure("no embedding API"));
        mocks.VectorStore
            .Setup(v => v.SearchAsync(okVector, It.IsAny<int>(), kbOk, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>
            {
                new() { Id = Guid.NewGuid(), Content = "Vector hit", DocumentId = Guid.NewGuid(), KnowledgeBaseId = kbOk, Score = 0.8 }
            });
        mocks.Keyword
            .Setup(k => k.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), kbBroken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KeywordSearchResult>
            {
                new() { ChunkId = Guid.NewGuid(), Content = "Keyword hit", DocumentId = Guid.NewGuid(), KnowledgeBaseId = kbBroken, ChunkIndex = 1, Score = 3.0 }
            });
        mocks.Keyword
            .Setup(k => k.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), kbOk, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KeywordSearchResult>());
        PassThroughReranker(mocks);
        mocks.DocRepo.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(new List<KnowledgeDocument>().BuildMock());

        var results = (await service.SearchAsync("query", new TextSearchFilter { KnowledgeBaseIds = [kbOk, kbBroken] })).ToList();

        results.Select(r => r.Text).ShouldBe(["Vector hit", "Keyword hit"], ignoreOrder: true);
    }

    [Fact]
    public async Task SearchAsync_DisabledKnowledgeBase_IsSearchedByNeitherPath()
    {
        var (service, mocks) = CreateHybridSearchServiceWithMocks(new AIRagOptions());
        var enabled = Guid.NewGuid();
        var disabled = Guid.NewGuid();
        mocks.UseKnowledgeBases(
            new KnowledgeBase { Id = enabled, Name = "enabled" },
            new KnowledgeBase { Id = disabled, Name = "disabled", IsEnabled = false });
        mocks.Embedding
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success(new[] { 0.1f }));
        mocks.VectorStore
            .Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>());
        mocks.Keyword
            .Setup(k => k.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KeywordSearchResult>());

        await service.SearchAsync("query", new TextSearchFilter { KnowledgeBaseIds = [enabled, disabled] });

        mocks.VectorStore.Verify(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), disabled, It.IsAny<CancellationToken>()), Times.Never);
        mocks.Keyword.Verify(k => k.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), disabled, It.IsAny<CancellationToken>()), Times.Never);
        mocks.VectorStore.Verify(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), enabled, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_OnlyDisabledKnowledgeBases_ReturnsNothing()
    {
        var (service, mocks) = CreateHybridSearchServiceWithMocks(new AIRagOptions());
        var disabled = Guid.NewGuid();
        mocks.UseKnowledgeBases(new KnowledgeBase { Id = disabled, Name = "disabled", IsEnabled = false });

        var results = await service.SearchAsync("query", new TextSearchFilter { KnowledgeBaseIds = [disabled] });

        results.ShouldBeEmpty();
        mocks.Embedding.Verify(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        mocks.Keyword.Verify(k => k.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region 仅关键词命中的块索引

    [Fact]
    public async Task SearchAsync_KeywordOnlyHit_ExpandsAroundItsRealChunkIndex()
    {
        // 父文档扩展按 chunkIndex 取窗口：仅关键词命中的第 37 块不能被记成第 0 块（那会注入文档开头、丢掉命中段落）
        var parent = new Mock<IParentDocumentRetriever>();
        IReadOnlyList<RetrievalResult>? expanded = null;
        parent
            .Setup(p => p.RetrieveAsync(It.IsAny<IReadOnlyList<RetrievalResult>>(), It.IsAny<ParentRetrievalOptions?>(), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyList<RetrievalResult> matches, ParentRetrievalOptions? _, CancellationToken _) => expanded = matches)
            .ReturnsAsync(new List<ParentDocumentResult>());
        var options = new AIRagOptions { ParentDocumentRetrieval = new ParentDocumentRetrievalOptions { Enabled = true } };
        var (service, mocks) = CreateHybridSearchServiceWithMocks(options, parentDocumentRetriever: parent.Object);
        mocks.Embedding
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success(new[] { 0.1f }));
        mocks.VectorStore
            .Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>());
        mocks.Keyword
            .Setup(k => k.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KeywordSearchResult>
            {
                new() { ChunkId = Guid.NewGuid(), Content = "Clause 14.2", DocumentId = Guid.NewGuid(), KnowledgeBaseId = ScopedKb, ChunkIndex = 37, Score = 5.0 }
            });
        PassThroughReranker(mocks);
        mocks.DocRepo.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(new List<KnowledgeDocument>().BuildMock());

        var results = (await service.SearchAsync("clause 14.2", Scoped())).ToList();

        expanded.ShouldNotBeNull();
        expanded.ShouldHaveSingleItem().Metadata!["chunkIndex"].ShouldBe(37);
        results.ShouldHaveSingleItem().Metadata!["chunkIndex"].ShouldBe(37);
    }

    [Fact]
    public async Task SearchAsync_ProviderWithoutChunkIndex_IsLookedUpFromTheChunkTable()
    {
        var (service, mocks) = CreateHybridSearchServiceWithMocks(new AIRagOptions());
        var known = Guid.NewGuid();
        var deleted = Guid.NewGuid();
        mocks.Embedding
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<EmbeddingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success(new[] { 0.1f }));
        mocks.VectorStore
            .Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>());
        mocks.Keyword
            .Setup(k => k.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KeywordSearchResult>
            {
                new() { ChunkId = known, Content = "Known chunk", DocumentId = Guid.NewGuid(), KnowledgeBaseId = ScopedKb, Score = 5.0 },
                new() { ChunkId = deleted, Content = "Chunk of a deleted document", DocumentId = Guid.NewGuid(), KnowledgeBaseId = ScopedKb, Score = 4.0 }
            });
        mocks.ChunkRepo.Setup(r => r.AsQueryable(It.IsAny<bool>()))
            .Returns(new List<DocumentChunk> { new() { Id = known, KnowledgeBaseId = ScopedKb, ChunkIndex = 12, Content = "Known chunk" } }.BuildMock());
        PassThroughReranker(mocks);
        mocks.DocRepo.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(new List<KnowledgeDocument>().BuildMock());

        var results = (await service.SearchAsync("query", Scoped())).ToList();

        var only = results.ShouldHaveSingleItem();
        only.Text.ShouldBe("Known chunk");
        only.Metadata!["chunkIndex"].ShouldBe(12);
    }

    [Fact]
    public void FullTextSql_SelectsTheChunkIndex_InBothVariants()
    {
        PgFullTextSearchProvider.BuildSearchSql("RAG_DocumentChunk", "RAG_KnowledgeBase", hasKnowledgeBaseId: true, tenantId: null)
            .ShouldContain("\"ChunkIndex\"");
        PgFullTextSearchProvider.BuildSearchSql("RAG_DocumentChunk", "RAG_KnowledgeBase", hasKnowledgeBaseId: false, tenantId: null)
            .ShouldContain("c.\"ChunkIndex\"");
    }

    [Fact]
    public async Task InMemoryKeywordSearch_ReturnsTheChunkIndex()
    {
        var chunkRepo = new Mock<IRepository<DocumentChunk, Guid>>();
        chunkRepo.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(new List<DocumentChunk>
        {
            new() { Id = Guid.NewGuid(), KnowledgeBaseId = ScopedKb, ChunkIndex = 9, Content = "termination clause applies" },
            new() { Id = Guid.NewGuid(), KnowledgeBaseId = ScopedKb, ChunkIndex = 2, Content = "unrelated opening text" }
        }.BuildMock());

        var results = await new InMemoryKeywordSearchProvider(chunkRepo.Object, NullLogger<InMemoryKeywordSearchProvider>.Instance)
            .SearchAsync("termination", topK: 5, ScopedKb);

        results.ShouldHaveSingleItem().ChunkIndex.ShouldBe(9);
    }

    private static void PassThroughReranker(HybridMocks mocks)
        => mocks.Reranker
            .Setup(r => r.RerankAsync(It.IsAny<string>(), It.IsAny<List<VectorSearchResult>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, List<VectorSearchResult> fused, int topK, CancellationToken _) => fused.Take(topK).ToList());

    #endregion

    #region InMemoryKeywordSearch 测试

    [Fact]
    public void InMemoryKeywordSearch_Tokenize_WorksCorrectly()
    {
        var tokens = InMemoryKeywordSearchProvider.Tokenize("Hello, World! This is a test.");

        tokens.ShouldContain("hello");
        tokens.ShouldContain("world");
        tokens.ShouldContain("this");
        tokens.ShouldContain("test");

        // 单字符词 "a" 应该被过滤
        tokens.ShouldNotContain("a");
        // "is" 有 2 个字符，应该保留
        tokens.ShouldContain("is");
    }

    [Fact]
    public void InMemoryKeywordSearch_Tokenize_EmptyText_ReturnsEmpty()
    {
        InMemoryKeywordSearchProvider.Tokenize("").ShouldBeEmpty();
        InMemoryKeywordSearchProvider.Tokenize("   ").ShouldBeEmpty();
        InMemoryKeywordSearchProvider.Tokenize(null!).ShouldBeEmpty();
    }

    [Fact]
    public async Task InMemoryKeywordSearch_EmptyQuery_ReturnsEmpty()
    {
        var chunkRepoMock = new Mock<IRepository<DocumentChunk, Guid>>();
        var loggerMock = new Mock<ILogger<InMemoryKeywordSearchProvider>>();

        var provider = new InMemoryKeywordSearchProvider(chunkRepoMock.Object, loggerMock.Object);

        var results = await provider.SearchAsync("", topK: 5);
        results.ShouldBeEmpty();

        var results2 = await provider.SearchAsync("   ", topK: 5);
        results2.ShouldBeEmpty();
    }

    [Fact]
    public void InMemoryKeywordSearch_Tokenize_SpecialCharacters()
    {
        var tokens = InMemoryKeywordSearchProvider.Tokenize("C# is great; Python (v3.12) is also [amazing]");

        // 验证标点被正确分割
        tokens.ShouldContain("c#");
        tokens.ShouldContain("great");
        tokens.ShouldContain("python");
        tokens.ShouldContain("v3");
        tokens.ShouldContain("12");
        tokens.ShouldContain("also");
        tokens.ShouldContain("amazing");
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// agent 侧检索只在知识库范围内进行（没有范围恒答空，见 <c>AgentKnowledgeScopeFailClosedTests</c>），管线用例都带一个范围。
    /// </summary>
    private static readonly Guid ScopedKb = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static TextSearchFilter Scoped() => new() { KnowledgeBaseIds = [ScopedKb] };

    /// <summary>
    /// 创建 HybridSearchService 用于测试 RRF 融合逻辑
    /// </summary>
    private static HybridSearchService CreateHybridSearchService(AIRagOptions options)
    {
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(ILoggerFactory)))
            .Returns(NullLoggerFactory.Instance);

        var vectorStoreMock = new Mock<IVectorStore>();
        var keywordProviderMock = new Mock<IKeywordSearchProvider>();
        var embeddingServiceMock = new Mock<IEmbeddingService>();
        var rerankerMock = new Mock<IReranker>();
        var docRepoMock = new Mock<IRepository<KnowledgeDocument, Guid>>();
        var ragOptions = new StaticOptionsMonitor<AIRagOptions>(options);

        return new HybridSearchService(
            serviceProviderMock.Object,
            vectorStoreMock.Object,
            keywordProviderMock.Object,
            embeddingServiceMock.Object,
            rerankerMock.Object,
            Mock.Of<IRepository<KnowledgeBase, Guid>>(),
            docRepoMock.Object,
            Mock.Of<IRepository<DocumentChunk, Guid>>(),
            ragOptions);
    }

    /// <summary>
    /// HybridSearchService 的可配置 mock 集合（用于端到端 SearchAsync 测试）
    /// </summary>
    private sealed class HybridMocks
    {
        public required Mock<IVectorStore> VectorStore { get; init; }
        public required Mock<IKeywordSearchProvider> Keyword { get; init; }
        public required Mock<IEmbeddingService> Embedding { get; init; }
        public required Mock<IReranker> Reranker { get; init; }
        public required Mock<IRepository<KnowledgeDocument, Guid>> DocRepo { get; init; }
        public required Mock<IRepository<KnowledgeBase, Guid>> KbRepo { get; init; }
        public required Mock<IRepository<DocumentChunk, Guid>> ChunkRepo { get; init; }

        /// <summary>范围里的这些知识库存在且启用中（不在这里的 id 视为找不到）。</summary>
        public void UseKnowledgeBases(params KnowledgeBase[] knowledgeBases)
            => KbRepo.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(knowledgeBases.ToList().BuildMock());
    }

    /// <summary>
    /// 创建 HybridSearchService 并返回其底层 mock，便于端到端配置/断言
    /// </summary>
    private static (HybridSearchService Service, HybridMocks Mocks) CreateHybridSearchServiceWithMocks(
        AIRagOptions options,
        IGraphSearchService? graphSearchService = null,
        IParentDocumentRetriever? parentDocumentRetriever = null)
    {
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(ILoggerFactory)))
            .Returns(NullLoggerFactory.Instance);

        var mocks = new HybridMocks
        {
            VectorStore = new Mock<IVectorStore>(),
            Keyword = new Mock<IKeywordSearchProvider>(),
            Embedding = new Mock<IEmbeddingService>(),
            Reranker = new Mock<IReranker>(),
            DocRepo = new Mock<IRepository<KnowledgeDocument, Guid>>(),
            KbRepo = new Mock<IRepository<KnowledgeBase, Guid>>(),
            ChunkRepo = new Mock<IRepository<DocumentChunk, Guid>>()
        };
        mocks.UseKnowledgeBases(new KnowledgeBase { Id = ScopedKb, Name = "scoped" });

        var service = new HybridSearchService(
            serviceProviderMock.Object,
            mocks.VectorStore.Object,
            mocks.Keyword.Object,
            mocks.Embedding.Object,
            mocks.Reranker.Object,
            mocks.KbRepo.Object,
            mocks.DocRepo.Object,
            mocks.ChunkRepo.Object,
            new StaticOptionsMonitor<AIRagOptions>(options),
            graphSearchService,
            parentDocumentRetriever);

        return (service, mocks);
    }

    #endregion
}
