using Tnzi.AI.Infrastructure.Memory;

namespace Tnzi.AI.Tests.Memory;

/// <summary>
/// DatabaseEntityMemoryStore 单元测试 - AgentId 过滤 + 批量 Upsert + 过期
/// </summary>
public class DatabaseEntityMemoryStoreTests
{
    #region GetRelevantEntitiesAsync - AgentId filtering

    [Fact]
    public async Task GetRelevantEntitiesAsync_FiltersById_ReturnsMatching()
    {
        var userId = Guid.NewGuid();
        var data = new List<EntityMemory>
        {
            new() { EntityName = "Alice", EntityType = "person", UserId = userId, LastMentioned = DateTime.UtcNow, MentionCount = 3, Properties = "{}" },
            new() { EntityName = "Bob", EntityType = "person", UserId = Guid.NewGuid(), LastMentioned = DateTime.UtcNow, MentionCount = 1, Properties = "{}" }
        };

        var store = CreateStore(data);
        var result = await store.GetRelevantEntitiesAsync(userId);

        result.Count.ShouldBe(1);
        result[0].EntityName.ShouldBe("Alice");
    }

    [Fact]
    public async Task GetRelevantEntitiesAsync_WithAgentId_FiltersCorrectly()
    {
        var userId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var data = new List<EntityMemory>
        {
            new() { EntityName = "Alice", EntityType = "person", UserId = userId, AgentId = agentId, LastMentioned = DateTime.UtcNow, Properties = "{}" },
            new() { EntityName = "Bob", EntityType = "person", UserId = userId, AgentId = Guid.NewGuid(), LastMentioned = DateTime.UtcNow, Properties = "{}" },
            new() { EntityName = "Charlie", EntityType = "person", UserId = userId, AgentId = null, LastMentioned = DateTime.UtcNow, Properties = "{}" }
        };

        var store = CreateStore(data);
        var result = await store.GetRelevantEntitiesAsync(userId, agentId);

        result.Count.ShouldBe(1);
        result[0].EntityName.ShouldBe("Alice");
    }

    [Fact]
    public async Task GetRelevantEntitiesAsync_NullUserId_ReturnsGlobalEntities()
    {
        var data = new List<EntityMemory>
        {
            new() { EntityName = "Global", EntityType = "concept", UserId = null, LastMentioned = DateTime.UtcNow, Properties = "{}" },
            new() { EntityName = "UserSpecific", EntityType = "person", UserId = Guid.NewGuid(), LastMentioned = DateTime.UtcNow, Properties = "{}" }
        };

        var store = CreateStore(data);
        var result = await store.GetRelevantEntitiesAsync(null);

        result.Count.ShouldBe(1);
        result[0].EntityName.ShouldBe("Global");
    }

    [Fact]
    public async Task GetRelevantEntitiesAsync_RespectsMaxEntities()
    {
        var userId = Guid.NewGuid();
        var data = Enumerable.Range(1, 10).Select(i => new EntityMemory
        {
            EntityName = $"Entity{i}",
            EntityType = "concept",
            UserId = userId,
            LastMentioned = DateTime.UtcNow.AddMinutes(-i),
            Properties = "{}"
        }).ToList();

        var store = CreateStore(data);
        var result = await store.GetRelevantEntitiesAsync(userId, maxEntities: 3);

        result.Count.ShouldBe(3);
    }

    [Fact]
    public async Task GetRelevantEntitiesAsync_OrdersByLastMentionedDesc()
    {
        var userId = Guid.NewGuid();
        var data = new List<EntityMemory>
        {
            new() { EntityName = "Old", UserId = userId, LastMentioned = DateTime.UtcNow.AddDays(-5), Properties = "{}" },
            new() { EntityName = "Recent", UserId = userId, LastMentioned = DateTime.UtcNow, Properties = "{}" }
        };

        var store = CreateStore(data);
        var result = await store.GetRelevantEntitiesAsync(userId);

        result[0].EntityName.ShouldBe("Recent");
    }

    #endregion

    #region GetRelevantEntitiesAsync - Expiration

    [Fact]
    public async Task GetRelevantEntitiesAsync_WithExpiration_FiltersExpired()
    {
        var userId = Guid.NewGuid();
        var data = new List<EntityMemory>
        {
            new() { EntityName = "Expired", UserId = userId, LastMentioned = DateTime.UtcNow.AddDays(-30), Properties = "{}" },
            new() { EntityName = "Fresh", UserId = userId, LastMentioned = DateTime.UtcNow.AddHours(-1), Properties = "{}" }
        };

        var entityOptions = new EntityMemoryOptions { EntityExpiration = TimeSpan.FromDays(7) };
        var store = CreateStore(data, entityOptions);
        var result = await store.GetRelevantEntitiesAsync(userId);

        result.Count.ShouldBe(1);
        result[0].EntityName.ShouldBe("Fresh");
    }

    #endregion

    #region UpsertEntityAsync

    [Fact]
    public async Task UpsertEntityAsync_NewEntity_Inserts()
    {
        var mockRepo = CreateMockRepo([]);
        var store = CreateStore(mockRepo);

        var entry = new EntityMemoryEntry
        {
            EntityName = "Alice",
            EntityType = "person",
            Properties = new Dictionary<string, string> { { "role", "developer" } }
        };

        await store.UpsertEntityAsync(entry);

        mockRepo.Verify(r => r.InsertAsync(
            It.Is<EntityMemory>(e => e.EntityName == "Alice" && e.MentionCount >= 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpsertEntityAsync_ExistingEntity_UpdatesViaExecuteUpdate()
    {
        // 验证：已有实体时走 ExecuteUpdateAsync 路径（绕过 ChangeTracker）
        var existing = new EntityMemory
        {
            Id = Guid.NewGuid(),
            EntityName = "Alice",
            EntityType = "person",
            UserId = null,
            AgentId = null,
            Properties = """{"role":"developer"}""",
            MentionCount = 3,
            LastMentioned = DateTime.UtcNow.AddHours(-1)
        };

        var mockRepo = CreateMockRepo([existing]);
        var store = CreateStore(mockRepo);

        var entry = new EntityMemoryEntry
        {
            EntityName = "Alice",
            EntityType = "person",
            Properties = new Dictionary<string, string> { { "role", "senior developer" }, { "team", "backend" } }
        };

        // 不应抛异常：Select 查询到 existingProps，然后 ExecuteUpdateAsync 更新
        await store.UpsertEntityAsync(entry);

        // 验证不走 Insert 路径（已有实体直接 ExecuteUpdate）
        mockRepo.Verify(r => r.InsertAsync(It.IsAny<EntityMemory>(), It.IsAny<CancellationToken>()), Times.Never);
        // 验证 AsQueryable 至少被调用 2 次（Select 查询 + ExecuteUpdate）
        mockRepo.Verify(r => r.AsQueryable(It.IsAny<bool>()), Times.AtLeast(2));
    }

    [Fact]
    public async Task UpsertEntityAsync_WithAgentId_SetsAgentId()
    {
        var agentId = Guid.NewGuid();
        var mockRepo = CreateMockRepo([]);
        var store = CreateStore(mockRepo);

        var entry = new EntityMemoryEntry
        {
            EntityName = "Project X",
            EntityType = "concept",
            AgentId = agentId,
            Properties = new Dictionary<string, string>()
        };

        await store.UpsertEntityAsync(entry);

        mockRepo.Verify(r => r.InsertAsync(
            It.Is<EntityMemory>(e => e.AgentId == agentId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region UpsertEntitiesAsync - Batch

    [Fact]
    public async Task UpsertEntitiesAsync_EmptyList_DoesNothing()
    {
        var mockRepo = CreateMockRepo([]);
        var store = CreateStore(mockRepo);

        await store.UpsertEntitiesAsync([]);

        mockRepo.Verify(r => r.InsertAsync(It.IsAny<EntityMemory>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpsertEntitiesAsync_SingleItem_DelegatesToUpsertEntity()
    {
        var mockRepo = CreateMockRepo([]);
        var store = CreateStore(mockRepo);

        var entries = new List<EntityMemoryEntry>
        {
            new() { EntityName = "Solo", EntityType = "person", Properties = new Dictionary<string, string>() }
        };

        await store.UpsertEntitiesAsync(entries);

        // 单条走 UpsertEntityAsync（InsertAsync）
        mockRepo.Verify(r => r.InsertAsync(
            It.Is<EntityMemory>(e => e.EntityName == "Solo"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpsertEntitiesAsync_AllNew_InsertsEach()
    {
        // 批量 upsert 现在逐条委托给 UpsertEntityAsync，使用 ExecuteUpdateAsync 绕过 ChangeTracker
        // mock queryable 不支持 ExecuteUpdateAsync，但对于全新实体（Select 返回 null），走 Insert 路径
        var mockRepo = CreateMockRepo([]);
        var store = CreateStore(mockRepo);

        var entries = new List<EntityMemoryEntry>
        {
            new() { EntityName = "Alice", EntityType = "person", Properties = new Dictionary<string, string>() },
            new() { EntityName = "Bob", EntityType = "person", Properties = new Dictionary<string, string>() }
        };

        await store.UpsertEntitiesAsync(entries);

        // 两条都是新插入
        mockRepo.Verify(r => r.InsertAsync(
            It.Is<EntityMemory>(e => e.EntityName == "Alice"),
            It.IsAny<CancellationToken>()), Times.Once);

        mockRepo.Verify(r => r.InsertAsync(
            It.Is<EntityMemory>(e => e.EntityName == "Bob"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region SearchEntitiesAsync

    [Fact]
    public async Task SearchEntitiesAsync_ByName_ReturnsMatching()
    {
        var data = new List<EntityMemory>
        {
            new() { EntityName = "Microsoft", EntityType = "organization", UserId = null, MentionCount = 5, Properties = "{}" },
            new() { EntityName = "Google", EntityType = "organization", UserId = null, MentionCount = 3, Properties = "{}" }
        };

        var store = CreateStore(data);
        var result = await store.SearchEntitiesAsync("Microsoft", null);

        result.Count.ShouldBe(1);
        result[0].EntityName.ShouldBe("Microsoft");
    }

    [Fact]
    public async Task SearchEntitiesAsync_WithAgentId_FiltersCorrectly()
    {
        var agentId = Guid.NewGuid();
        var data = new List<EntityMemory>
        {
            new() { EntityName = "Alice", EntityType = "person", UserId = null, AgentId = agentId, MentionCount = 1, Properties = "{}" },
            new() { EntityName = "Alice", EntityType = "person", UserId = null, AgentId = Guid.NewGuid(), MentionCount = 1, Properties = "{}" }
        };

        var store = CreateStore(data);
        var result = await store.SearchEntitiesAsync("Alice", null, agentId);

        result.Count.ShouldBe(1);
        result[0].AgentId.ShouldBe(agentId);
    }

    #endregion

    #region MapToEntry - Properties deserialization

    [Fact]
    public async Task GetRelevantEntitiesAsync_DeserializesProperties()
    {
        var data = new List<EntityMemory>
        {
            new()
            {
                EntityName = "Alice",
                EntityType = "person",
                UserId = null,
                LastMentioned = DateTime.UtcNow,
                Properties = """{"role":"developer","team":"backend"}"""
            }
        };

        var store = CreateStore(data);
        var result = await store.GetRelevantEntitiesAsync(null);

        result[0].Properties["role"].ShouldBe("developer");
        result[0].Properties["team"].ShouldBe("backend");
    }

    [Fact]
    public async Task GetRelevantEntitiesAsync_InvalidJson_ReturnsEmptyProperties()
    {
        var data = new List<EntityMemory>
        {
            new()
            {
                EntityName = "Broken",
                EntityType = "thing",
                UserId = null,
                LastMentioned = DateTime.UtcNow,
                Properties = "not json at all"
            }
        };

        var store = CreateStore(data);
        var result = await store.GetRelevantEntitiesAsync(null);

        result[0].Properties.ShouldBeEmpty();
    }

    #endregion

    #region Helpers

    private static Mock<IRepository<EntityMemory, Guid>> CreateMockRepo(List<EntityMemory> data)
    {
        var mockRepo = new Mock<IRepository<EntityMemory, Guid>>();
        var mockQueryable = data.BuildMock();
        mockRepo.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(mockQueryable);
        return mockRepo;
    }

    private static DatabaseEntityMemoryStore CreateStore(List<EntityMemory> data, EntityMemoryOptions? entityOptions = null)
    {
        var mockRepo = CreateMockRepo(data);
        return CreateStore(mockRepo, entityOptions);
    }

    private static DatabaseEntityMemoryStore CreateStore(Mock<IRepository<EntityMemory, Guid>> mockRepo, EntityMemoryOptions? entityOptions = null)
    {
        IOptionsMonitor<AIOptions>? aiOptions = null;
        if (entityOptions != null)
        {
            aiOptions = new StaticOptionsMonitor<AIOptions>(new AIOptions
            {
                ContextProviders = new ContextProvidersOptions { EntityMemory = entityOptions }
            });
        }

        return new DatabaseEntityMemoryStore(
            mockRepo.Object,
            Mock.Of<ILogger<DatabaseEntityMemoryStore>>(),
            aiOptions);
    }

    #endregion
}
