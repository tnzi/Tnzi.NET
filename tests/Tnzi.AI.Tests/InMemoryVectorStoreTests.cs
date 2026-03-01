namespace Tnzi.AI.Tests;

/// <summary>
/// InMemoryVectorStore 单元测试 — 验证内存向量存储的搜索、插入、删除和线程安全
/// </summary>
public class InMemoryVectorStoreTests
{
    private readonly InMemoryVectorStore _store;

    public InMemoryVectorStoreTests()
    {
        var logger = new Mock<ILogger<InMemoryVectorStore>>();
        _store = new InMemoryVectorStore(logger.Object);
    }

    [Fact]
    public async Task Search_EmptyStore_ReturnsEmpty()
    {
        var queryVector = new float[] { 1.0f, 0.0f, 0.0f };

        var results = await _store.SearchAsync(queryVector, topK: 5);

        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task UpsertAndSearch_ReturnsMatchingResults()
    {
        var kbId = Guid.NewGuid();
        var docId = Guid.NewGuid();

        // 插入两个向量
        await _store.UpsertAsync(Guid.NewGuid(), [1.0f, 0.0f, 0.0f], docId, kbId, "Content A", 0);
        await _store.UpsertAsync(Guid.NewGuid(), [0.0f, 1.0f, 0.0f], docId, kbId, "Content B", 1);

        // 搜索与第一个向量相似的
        var results = await _store.SearchAsync([1.0f, 0.0f, 0.0f], topK: 2);

        results.Count.ShouldBe(2);
        results[0].Content.ShouldBe("Content A");
        results[0].Score.ShouldBeGreaterThan(0.99); // 完全匹配
    }

    [Fact]
    public async Task Search_ReturnsSortedBySimilarity()
    {
        var kbId = Guid.NewGuid();
        var docId = Guid.NewGuid();

        // 插入三个向量：与查询向量的相似度递减
        await _store.UpsertAsync(Guid.NewGuid(), [1.0f, 0.0f, 0.0f], docId, kbId, "Most similar", 0);
        await _store.UpsertAsync(Guid.NewGuid(), [0.7f, 0.7f, 0.0f], docId, kbId, "Somewhat similar", 1);
        await _store.UpsertAsync(Guid.NewGuid(), [0.0f, 0.0f, 1.0f], docId, kbId, "Least similar", 2);

        var results = await _store.SearchAsync([1.0f, 0.0f, 0.0f], topK: 3);

        results.Count.ShouldBe(3);
        results[0].Content.ShouldBe("Most similar");
        results[1].Content.ShouldBe("Somewhat similar");
        results[2].Content.ShouldBe("Least similar");

        // 验证分数递减
        results[0].Score.ShouldBeGreaterThan(results[1].Score);
        results[1].Score.ShouldBeGreaterThan(results[2].Score);
    }

    [Fact]
    public async Task Search_WithKnowledgeBaseFilter_ReturnsOnlyMatchingKb()
    {
        var kbId1 = Guid.NewGuid();
        var kbId2 = Guid.NewGuid();
        var docId = Guid.NewGuid();

        await _store.UpsertAsync(Guid.NewGuid(), [1.0f, 0.0f, 0.0f], docId, kbId1, "KB1 content", 0);
        await _store.UpsertAsync(Guid.NewGuid(), [1.0f, 0.0f, 0.0f], docId, kbId2, "KB2 content", 0);

        var results = await _store.SearchAsync([1.0f, 0.0f, 0.0f], topK: 10, knowledgeBaseId: kbId1);

        results.Count.ShouldBe(1);
        results[0].Content.ShouldBe("KB1 content");
    }

    [Fact]
    public async Task Search_TopKLimitsResults()
    {
        var kbId = Guid.NewGuid();
        var docId = Guid.NewGuid();

        for (var i = 0; i < 10; i++)
        {
            await _store.UpsertAsync(Guid.NewGuid(), [1.0f, 0.0f, 0.0f], docId, kbId, $"Content {i}", i);
        }

        var results = await _store.SearchAsync([1.0f, 0.0f, 0.0f], topK: 3);

        results.Count.ShouldBe(3);
    }

    [Fact]
    public async Task DeleteByDocument_RemovesCorrectEntries()
    {
        var kbId = Guid.NewGuid();
        var docId1 = Guid.NewGuid();
        var docId2 = Guid.NewGuid();

        await _store.UpsertAsync(Guid.NewGuid(), [1.0f, 0.0f, 0.0f], docId1, kbId, "Doc1 chunk", 0);
        await _store.UpsertAsync(Guid.NewGuid(), [0.0f, 1.0f, 0.0f], docId2, kbId, "Doc2 chunk", 0);

        await _store.DeleteByDocumentAsync(docId1);

        var results = await _store.SearchAsync([1.0f, 0.0f, 0.0f], topK: 10);

        results.Count.ShouldBe(1);
        results[0].Content.ShouldBe("Doc2 chunk");
        results[0].DocumentId.ShouldBe(docId2);
    }

    [Fact]
    public async Task DeleteByKnowledgeBase_RemovesCorrectEntries()
    {
        var kbId1 = Guid.NewGuid();
        var kbId2 = Guid.NewGuid();
        var docId = Guid.NewGuid();

        await _store.UpsertAsync(Guid.NewGuid(), [1.0f, 0.0f, 0.0f], docId, kbId1, "KB1 chunk A", 0);
        await _store.UpsertAsync(Guid.NewGuid(), [0.5f, 0.5f, 0.0f], docId, kbId1, "KB1 chunk B", 1);
        await _store.UpsertAsync(Guid.NewGuid(), [0.0f, 1.0f, 0.0f], docId, kbId2, "KB2 chunk", 0);

        await _store.DeleteByKnowledgeBaseAsync(kbId1);

        var results = await _store.SearchAsync([1.0f, 0.0f, 0.0f], topK: 10);

        results.Count.ShouldBe(1);
        results[0].Content.ShouldBe("KB2 chunk");
        results[0].KnowledgeBaseId.ShouldBe(kbId2);
    }

    [Fact]
    public async Task ConcurrentAccess_ThreadSafe()
    {
        var kbId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        const int concurrency = 50;

        // 并行插入
        var upsertTasks = Enumerable.Range(0, concurrency)
            .Select(i => _store.UpsertAsync(
                Guid.NewGuid(),
                [1.0f * i / concurrency, 1.0f - (1.0f * i / concurrency), 0.0f],
                docId, kbId, $"Concurrent content {i}", i))
            .ToArray();

        await Task.WhenAll(upsertTasks);

        // 并行搜索
        var searchTasks = Enumerable.Range(0, concurrency)
            .Select(_ => _store.SearchAsync([1.0f, 0.0f, 0.0f], topK: 10))
            .ToArray();

        var searchResults = await Task.WhenAll(searchTasks);

        // 所有搜索应返回结果且不抛异常
        foreach (var results in searchResults)
        {
            results.Count.ShouldBeGreaterThan(0);
            results.Count.ShouldBeLessThanOrEqualTo(10);
        }

        // 验证总条目数
        var allResults = await _store.SearchAsync([1.0f, 0.0f, 0.0f], topK: concurrency + 10);
        allResults.Count.ShouldBe(concurrency);
    }

    [Fact]
    public void CosineSimilarity_IdenticalVectors_ReturnsMaxScore()
    {
        var a = new float[] { 1.0f, 2.0f, 3.0f };
        var b = new float[] { 1.0f, 2.0f, 3.0f };

        var similarity = InMemoryVectorStore.CosineSimilarity(a, b);

        similarity.ShouldBeGreaterThan(0.999);
        similarity.ShouldBeLessThanOrEqualTo(1.0);
    }

    [Fact]
    public void CosineSimilarity_OrthogonalVectors_ReturnsZero()
    {
        var a = new float[] { 1.0f, 0.0f, 0.0f };
        var b = new float[] { 0.0f, 1.0f, 0.0f };

        var similarity = InMemoryVectorStore.CosineSimilarity(a, b);

        similarity.ShouldBe(0.0);
    }

    [Fact]
    public void CosineSimilarity_OppositeVectors_ReturnsNegativeOne()
    {
        var a = new float[] { 1.0f, 0.0f, 0.0f };
        var b = new float[] { -1.0f, 0.0f, 0.0f };

        var similarity = InMemoryVectorStore.CosineSimilarity(a, b);

        similarity.ShouldBeLessThan(-0.999);
    }

    [Fact]
    public void CosineSimilarity_ZeroVector_ReturnsZero()
    {
        var a = new float[] { 1.0f, 2.0f, 3.0f };
        var b = new float[] { 0.0f, 0.0f, 0.0f };

        var similarity = InMemoryVectorStore.CosineSimilarity(a, b);

        similarity.ShouldBe(0.0);
    }

    [Fact]
    public void CosineSimilarity_DifferentLength_ReturnsZero()
    {
        var a = new float[] { 1.0f, 0.0f };
        var b = new float[] { 1.0f, 0.0f, 0.0f };

        var similarity = InMemoryVectorStore.CosineSimilarity(a, b);

        similarity.ShouldBe(0.0);
    }

    [Fact]
    public void CosineSimilarity_EmptyVectors_ReturnsZero()
    {
        var similarity = InMemoryVectorStore.CosineSimilarity([], []);

        similarity.ShouldBe(0.0);
    }

    [Fact]
    public async Task Upsert_SameId_UpdatesEntry()
    {
        var chunkId = Guid.NewGuid();
        var kbId = Guid.NewGuid();
        var docId = Guid.NewGuid();

        await _store.UpsertAsync(chunkId, [1.0f, 0.0f, 0.0f], docId, kbId, "Original content", 0);
        await _store.UpsertAsync(chunkId, [0.0f, 1.0f, 0.0f], docId, kbId, "Updated content", 0);

        var results = await _store.SearchAsync([0.0f, 1.0f, 0.0f], topK: 10);

        results.Count.ShouldBe(1);
        results[0].Content.ShouldBe("Updated content");
        results[0].Id.ShouldBe(chunkId);
    }
}
