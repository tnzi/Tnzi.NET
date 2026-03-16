using Tnzi.AI.Infrastructure.Memory;
using Tnzi.AI.Memory;

namespace Tnzi.AI.Tests.Memory;

/// <summary>
/// DatabaseMemoryStore 单元测试 — MemoryScope 隔离 + 修剪 + 过期 + 搜索优化
/// </summary>
public class DatabaseMemoryStoreTests
{
    #region ReadAsync

    [Fact]
    public async Task ReadAsync_ExistingScope_ReturnsContent()
    {
        var entries = new List<MemoryEntry>
        {
            new() { Scope = "default", Content = "Line 1", CreationTime = DateTime.UtcNow.AddMinutes(-2) },
            new() { Scope = "default", Content = "Line 2", CreationTime = DateTime.UtcNow.AddMinutes(-1) }
        };

        var store = CreateStore(entries);
        var result = await store.ReadAsync("default");

        result.ShouldNotBeNull();
        result.ShouldContain("Line 1");
        result.ShouldContain("Line 2");
    }

    [Fact]
    public async Task ReadAsync_NonExistentScope_ReturnsNull()
    {
        var store = CreateStore([]);
        var result = await store.ReadAsync("nonexistent");
        result.ShouldBeNull();
    }

    [Fact]
    public async Task ReadAsync_WithExpiration_FiltersExpired()
    {
        var entries = new List<MemoryEntry>
        {
            new() { Scope = "default", Content = "Old", CreationTime = DateTime.UtcNow.AddDays(-10) },
            new() { Scope = "default", Content = "Recent", CreationTime = DateTime.UtcNow.AddMinutes(-5) }
        };

        var memoryOptions = new MemoryOptions { EntryExpiration = TimeSpan.FromDays(1) };
        var store = CreateStore(entries, memoryOptions: memoryOptions);
        var result = await store.ReadAsync("default");

        result.ShouldNotBeNull();
        result.ShouldContain("Recent");
        result.ShouldNotContain("Old");
    }

    #endregion

    #region WriteAsync (string scope)

    [Fact]
    public async Task WriteAsync_InsertsEntry()
    {
        var mockRepo = CreateMockRepo([]);
        var store = CreateStore(mockRepo);

        await store.WriteAsync("test-scope", "Hello World");

        mockRepo.Verify(r => r.InsertAsync(
            It.Is<MemoryEntry>(e => e.Scope == "test-scope" && e.Content == "Hello World"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region WriteAsync (MemoryScope)

    [Fact]
    public async Task WriteAsync_MemoryScope_WritesUserIdAndAgentId()
    {
        var userId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var scope = new MemoryScope("notes", userId, agentId);

        var mockRepo = CreateMockRepo([]);
        var store = CreateStore(mockRepo);

        await store.WriteAsync(scope, "User-specific content");

        mockRepo.Verify(r => r.InsertAsync(
            It.Is<MemoryEntry>(e =>
                e.UserId == userId &&
                e.AgentId == agentId &&
                e.Content == "User-specific content"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region AppendAsync (MemoryScope)

    [Fact]
    public async Task AppendAsync_MemoryScope_WritesUserIdAndAgentId()
    {
        var userId = Guid.NewGuid();
        var scope = new MemoryScope("log", userId);

        var mockRepo = CreateMockRepo([]);
        var store = CreateStore(mockRepo);

        await store.AppendAsync(scope, "New entry");

        mockRepo.Verify(r => r.InsertAsync(
            It.Is<MemoryEntry>(e => e.UserId == userId && e.Content == "New entry"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region TrimExcessEntries

    [Fact]
    public async Task AppendAsync_ExceedsMaxEntries_DeletesOldest()
    {
        var scopeKey = "trim-scope";
        var entries = Enumerable.Range(1, 5).Select(i => new MemoryEntry
        {
            Id = Guid.NewGuid(),
            Scope = scopeKey,
            Content = $"Entry {i}",
            CreationTime = DateTime.UtcNow.AddMinutes(-i)
        }).ToList();

        var mockRepo = CreateMockRepo(entries);
        var memoryOptions = new MemoryOptions { MaxEntriesPerScope = 3 };
        var store = CreateStore(mockRepo, memoryOptions: memoryOptions);

        await store.AppendAsync(scopeKey, "New entry");

        // 应该删除最旧的条目（超出限制）
        mockRepo.Verify(r => r.DeleteManyAsync(
            It.Is<IEnumerable<MemoryEntry>>(list => list.Any()),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task AppendAsync_NoMaxEntries_DoesNotTrim()
    {
        var entries = Enumerable.Range(1, 10).Select(i => new MemoryEntry
        {
            Id = Guid.NewGuid(),
            Scope = "no-limit",
            Content = $"Entry {i}",
            CreationTime = DateTime.UtcNow.AddMinutes(-i)
        }).ToList();

        var mockRepo = CreateMockRepo(entries);
        var memoryOptions = new MemoryOptions { MaxEntriesPerScope = null };
        var store = CreateStore(mockRepo, memoryOptions: memoryOptions);

        await store.AppendAsync("no-limit", "New entry");

        // 不应删除
        mockRepo.Verify(r => r.DeleteManyAsync(
            It.IsAny<IEnumerable<MemoryEntry>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region SearchAsync — keyword pre-filtering

    [Fact]
    public async Task SearchAsync_MatchingKeyword_ReturnsResults()
    {
        var entries = new List<MemoryEntry>
        {
            new() { Scope = "search", Content = "The architecture uses microservices", CreationTime = DateTime.UtcNow },
            new() { Scope = "search", Content = "Simple hello world app", CreationTime = DateTime.UtcNow }
        };

        var store = CreateStore(entries);
        var results = await store.SearchAsync("search", "architecture");

        results.Count.ShouldBeGreaterThan(0);
        results[0].Content.ShouldContain("architecture");
    }

    [Fact]
    public async Task SearchAsync_NoMatch_FallsBackAndReturnsEmpty()
    {
        var entries = new List<MemoryEntry>
        {
            new() { Scope = "search", Content = "Unrelated content xyz", CreationTime = DateTime.UtcNow }
        };

        var store = CreateStore(entries);
        var results = await store.SearchAsync("search", "completely different query");

        // 预过滤无结果时全量加载，但关键词得分为 0 → 返回空
        results.Count.ShouldBe(0);
    }

    [Fact]
    public async Task SearchAsync_RespectsMaxResults()
    {
        var entries = Enumerable.Range(1, 10).Select(i => new MemoryEntry
        {
            Scope = "search", Content = $"Entry about coding {i}", CreationTime = DateTime.UtcNow
        }).ToList();

        var store = CreateStore(entries);
        var results = await store.SearchAsync("search", "coding", maxResults: 3);

        results.Count.ShouldBeLessThanOrEqualTo(3);
    }

    #endregion

    #region UpdateEntryAsync

    [Fact]
    public async Task UpdateEntryAsync_ExistingEntry_UpdatesContentAndEmbedding()
    {
        var entryId = Guid.NewGuid();
        var entry = new MemoryEntry { Id = entryId, Scope = "update-scope", Content = "original content", CreationTime = DateTime.UtcNow };

        var mockRepo = CreateMockRepo([entry]);
        mockRepo.Setup(r => r.GetAsync(entryId, It.IsAny<CancellationToken>())).ReturnsAsync(entry);
        var store = CreateStore(mockRepo);

        await store.UpdateEntryAsync("update-scope", entryId, "new content");

        mockRepo.Verify(r => r.UpdateAsync(
            It.Is<MemoryEntry>(e => e.Content == "new content"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateEntryAsync_WrongScope_DoesNotUpdate()
    {
        var entryId = Guid.NewGuid();
        var entry = new MemoryEntry { Id = entryId, Scope = "other-scope", Content = "content", CreationTime = DateTime.UtcNow };

        var mockRepo = CreateMockRepo([entry]);
        mockRepo.Setup(r => r.GetAsync(entryId, It.IsAny<CancellationToken>())).ReturnsAsync(entry);
        var store = CreateStore(mockRepo);

        await store.UpdateEntryAsync("wrong-scope", entryId, "new content");

        mockRepo.Verify(r => r.UpdateAsync(It.IsAny<MemoryEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateEntryAsync_NotFound_DoesNotUpdate()
    {
        var mockRepo = CreateMockRepo([]);
        mockRepo.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((MemoryEntry?)null);
        var store = CreateStore(mockRepo);

        await store.UpdateEntryAsync("scope", Guid.NewGuid(), "new content");

        mockRepo.Verify(r => r.UpdateAsync(It.IsAny<MemoryEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region DeleteEntryAsync

    [Fact]
    public async Task DeleteEntryAsync_ExistingEntry_Deletes()
    {
        var entryId = Guid.NewGuid();
        var entry = new MemoryEntry { Id = entryId, Scope = "delete-scope", Content = "to delete", CreationTime = DateTime.UtcNow };

        var mockRepo = CreateMockRepo([entry]);
        mockRepo.Setup(r => r.GetAsync(entryId, It.IsAny<CancellationToken>())).ReturnsAsync(entry);
        var store = CreateStore(mockRepo);

        await store.DeleteEntryAsync("delete-scope", entryId);

        mockRepo.Verify(r => r.DeleteAsync(
            It.Is<MemoryEntry>(e => e.Id == entryId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteEntryAsync_WrongScope_DoesNotDelete()
    {
        var entryId = Guid.NewGuid();
        var entry = new MemoryEntry { Id = entryId, Scope = "real-scope", Content = "content", CreationTime = DateTime.UtcNow };

        var mockRepo = CreateMockRepo([entry]);
        mockRepo.Setup(r => r.GetAsync(entryId, It.IsAny<CancellationToken>())).ReturnsAsync(entry);
        var store = CreateStore(mockRepo);

        await store.DeleteEntryAsync("wrong-scope", entryId);

        mockRepo.Verify(r => r.DeleteAsync(It.IsAny<MemoryEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteEntryAsync_NotFound_DoesNotDelete()
    {
        var mockRepo = CreateMockRepo([]);
        mockRepo.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((MemoryEntry?)null);
        var store = CreateStore(mockRepo);

        await store.DeleteEntryAsync("scope", Guid.NewGuid());

        mockRepo.Verify(r => r.DeleteAsync(It.IsAny<MemoryEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region SearchAsync — importance scoring + Id/Category population

    [Fact]
    public async Task SearchAsync_AppliesImportanceBoost()
    {
        var entries = new List<MemoryEntry>
        {
            new() { Scope = "score-test", Content = "important memory about project", Importance = 0.9, CreationTime = DateTime.UtcNow },
            new() { Scope = "score-test", Content = "trivial memory about project", Importance = 0.1, CreationTime = DateTime.UtcNow }
        };

        var store = CreateStore(entries);
        var results = await store.SearchAsync("score-test", "project", maxResults: 2);

        results.Count.ShouldBe(2);
        results[0].Score.ShouldBeGreaterThanOrEqualTo(results[1].Score);
        results[0].Content.ShouldContain("important");
    }

    [Fact]
    public async Task SearchAsync_PopulatesIdAndCategory()
    {
        var id = Guid.NewGuid();
        var entries = new List<MemoryEntry>
        {
            new() { Id = id, Scope = "id-test", Content = "test content for id", Category = "fact", CreationTime = DateTime.UtcNow }
        };

        var store = CreateStore(entries);
        var results = await store.SearchAsync("id-test", "test", maxResults: 1);

        results.Count.ShouldBe(1);
        results[0].Id.ShouldBe(id);
        results[0].Category.ShouldBe("fact");
    }

    #endregion

    #region SearchByCategoryAsync

    [Fact]
    public async Task SearchByCategoryAsync_FiltersCorrectly()
    {
        var entries = new List<MemoryEntry>
        {
            new() { Scope = "cat-scope", Content = "user likes dark mode", Category = "preference", CreationTime = DateTime.UtcNow },
            new() { Scope = "cat-scope", Content = "user is a developer", Category = "fact", CreationTime = DateTime.UtcNow }
        };

        var store = CreateStore(entries);
        var results = await store.SearchByCategoryAsync("cat-scope", "user", "preference", maxResults: 10);

        results.Count.ShouldBe(1);
        results[0].Content.ShouldContain("dark mode");
        results[0].Category.ShouldBe("preference");
    }

    [Fact]
    public async Task SearchByCategoryAsync_NoMatchingCategory_ReturnsEmpty()
    {
        var entries = new List<MemoryEntry>
        {
            new() { Scope = "cat-scope2", Content = "some content", Category = "fact", CreationTime = DateTime.UtcNow }
        };

        var store = CreateStore(entries);
        var results = await store.SearchByCategoryAsync("cat-scope2", "content", "preference", maxResults: 10);

        results.Count.ShouldBe(0);
    }

    #endregion

    #region ClearAsync

    [Fact]
    public async Task ClearAsync_DeletesAllEntriesInScope()
    {
        var entries = new List<MemoryEntry>
        {
            new() { Id = Guid.NewGuid(), Scope = "clear-me", Content = "A" },
            new() { Id = Guid.NewGuid(), Scope = "clear-me", Content = "B" },
            new() { Id = Guid.NewGuid(), Scope = "keep-me", Content = "C" }
        };

        var mockRepo = CreateMockRepo(entries);
        var store = CreateStore(mockRepo);

        await store.ClearAsync("clear-me");

        mockRepo.Verify(r => r.DeleteManyAsync(
            It.Is<IEnumerable<MemoryEntry>>(list => list.Count() == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Helpers

    private static Mock<IRepository<MemoryEntry, Guid>> CreateMockRepo(List<MemoryEntry> data)
    {
        var mockRepo = new Mock<IRepository<MemoryEntry, Guid>>();
        var mockQueryable = data.BuildMock();
        mockRepo.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(mockQueryable);
        return mockRepo;
    }

    private static DatabaseMemoryStore CreateStore(List<MemoryEntry> data, MemoryOptions? memoryOptions = null)
    {
        var mockRepo = CreateMockRepo(data);
        return CreateStore(mockRepo, memoryOptions);
    }

    private static DatabaseMemoryStore CreateStore(Mock<IRepository<MemoryEntry, Guid>> mockRepo, MemoryOptions? memoryOptions = null)
    {
        IOptions<AIOptions>? aiOptions = null;
        if (memoryOptions != null)
        {
            aiOptions = Microsoft.Extensions.Options.Options.Create(new AIOptions
            {
                ContextProviders = new ContextProvidersOptions { Memory = memoryOptions }
            });
        }

        return new DatabaseMemoryStore(
            mockRepo.Object,
            Mock.Of<ILogger<DatabaseMemoryStore>>(),
            aiOptions: aiOptions);
    }

    #endregion
}
