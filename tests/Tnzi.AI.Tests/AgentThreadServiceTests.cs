using Tnzi.Exceptions;
using AgentThreadEntity = Tnzi.AI.Entities.AgentThread;

namespace Tnzi.AI.Tests;

/// <summary>
/// AgentThreadService 单元测试 — 覆盖 GetOrCreateThreadAsync / GetByIdAsync / DeleteAsync / ExportAsync / SaveMessageAsync
/// </summary>
public class AgentThreadServiceTests
{
    #region Helpers

    private readonly Mock<IRepository<AgentThreadEntity, Guid>> _threadRepo = new();
    private readonly Mock<IRepository<AgentThreadMessage, Guid>> _messageRepo = new();
    private readonly Mock<IRepository<Agent, Guid>> _agentRepo = new();
    private readonly IServiceProvider _serviceProvider;

    public AgentThreadServiceTests()
    {
        // Initialize Mapster for MapTo<T> extension methods
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();
    }

    private AgentThreadService CreateService() => new(
        _threadRepo.Object,
        _messageRepo.Object,
        _agentRepo.Object,
        _serviceProvider);

    private static AgentThreadEntity MakeThread(
        Guid? id = null,
        Guid? agentId = null,
        Guid? creatorId = null,
        string? title = "Test Thread",
        string? serializedData = null) => new()
        {
            Id = id ?? Guid.NewGuid(),
            AgentId = agentId,
            CreatorId = creatorId,
            Title = title,
            LastActivityTime = DateTime.UtcNow,
            SerializedData = serializedData
        };

    private static AgentThreadMessage MakeMessage(
        Guid threadId,
        string role = MessageRole.User,
        string content = "Hello",
        int order = 1) => new()
        {
            Id = Guid.NewGuid(),
            ThreadId = threadId,
            Role = role,
            Content = content,
            Order = order
        };

    /// <summary>
    /// 设置消息仓储为 IQueryable（支持 LINQ 扩展方法直接调用，如 Where().CountAsync()）
    /// </summary>
    private void SetupMessageQueryable(List<AgentThreadMessage> data)
    {
        var mock = data.BuildMock();
        _messageRepo.As<IQueryable<AgentThreadMessage>>().Setup(q => q.Provider).Returns(mock.Provider);
        _messageRepo.As<IQueryable<AgentThreadMessage>>().Setup(q => q.Expression).Returns(mock.Expression);
        _messageRepo.As<IQueryable<AgentThreadMessage>>().Setup(q => q.ElementType).Returns(mock.ElementType);
        _messageRepo.As<IQueryable<AgentThreadMessage>>().Setup(q => q.GetEnumerator()).Returns(mock.GetEnumerator());
        _messageRepo.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(mock);
    }

    /// <summary>
    /// 设置线程仓储为 IQueryable（支持 LINQ 扩展方法）
    /// </summary>
    private void SetupThreadQueryable(List<AgentThreadEntity> data)
    {
        var mock = data.BuildMock();
        _threadRepo.As<IQueryable<AgentThreadEntity>>().Setup(q => q.Provider).Returns(mock.Provider);
        _threadRepo.As<IQueryable<AgentThreadEntity>>().Setup(q => q.Expression).Returns(mock.Expression);
        _threadRepo.As<IQueryable<AgentThreadEntity>>().Setup(q => q.ElementType).Returns(mock.ElementType);
        _threadRepo.As<IQueryable<AgentThreadEntity>>().Setup(q => q.GetEnumerator()).Returns(mock.GetEnumerator());
        _threadRepo.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(mock);
    }

    #endregion

    // -------------------------------------------------------------------------
    // GetByIdAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetByIdAsync_Found_ReturnsDto()
    {
        var threadId = Guid.NewGuid();
        var thread = MakeThread(threadId);
        _threadRepo.Setup(r => r.GetAsync(threadId, It.IsAny<CancellationToken>())).ReturnsAsync(thread);
        SetupMessageQueryable([]);

        var service = CreateService();
        var result = await service.GetByIdAsync(threadId);

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.Id.ShouldBe(threadId);
        result.Data.Title.ShouldBe("Test Thread");
        result.Data.MessageCount.ShouldBe(0);
    }

    [Fact]
    public async Task GetByIdAsync_Found_MessageCountIsCorrect()
    {
        var threadId = Guid.NewGuid();
        var thread = MakeThread(threadId);
        var messages = new List<AgentThreadMessage>
        {
            MakeMessage(threadId, order: 1),
            MakeMessage(threadId, order: 2),
            MakeMessage(threadId, order: 3)
        };
        _threadRepo.Setup(r => r.GetAsync(threadId, It.IsAny<CancellationToken>())).ReturnsAsync(thread);
        SetupMessageQueryable(messages);

        var service = CreateService();
        var result = await service.GetByIdAsync(threadId);

        result.Succeeded.ShouldBeTrue();
        result.Data!.MessageCount.ShouldBe(3);
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_Returns404()
    {
        var threadId = Guid.NewGuid();
        _threadRepo.Setup(r => r.GetAsync(threadId, It.IsAny<CancellationToken>())).ReturnsAsync((AgentThreadEntity?)null);

        var service = CreateService();
        var result = await service.GetByIdAsync(threadId);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    // -------------------------------------------------------------------------
    // DeleteAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeleteAsync_Found_Succeeds()
    {
        var threadId = Guid.NewGuid();
        var thread = MakeThread(threadId);
        _threadRepo.Setup(r => r.GetAsync(threadId, It.IsAny<CancellationToken>())).ReturnsAsync(thread);
        _threadRepo.Setup(r => r.DeleteAsync(thread, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var service = CreateService();
        var result = await service.DeleteAsync(threadId);

        result.Succeeded.ShouldBeTrue();
        _threadRepo.Verify(r => r.DeleteAsync(thread, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_NotFound_Returns404()
    {
        var threadId = Guid.NewGuid();
        _threadRepo.Setup(r => r.GetAsync(threadId, It.IsAny<CancellationToken>())).ReturnsAsync((AgentThreadEntity?)null);

        var service = CreateService();
        var result = await service.DeleteAsync(threadId);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
        _threadRepo.Verify(r => r.DeleteAsync(It.IsAny<AgentThreadEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -------------------------------------------------------------------------
    // ExportAsJsonAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExportAsJsonAsync_Found_ReturnsExportDto()
    {
        var threadId = Guid.NewGuid();
        var thread = MakeThread(threadId, title: "My Thread");
        var messages = new List<AgentThreadMessage>
        {
            MakeMessage(threadId, MessageRole.User, "Hello", 1),
            MakeMessage(threadId, MessageRole.Assistant, "Hi there!", 2)
        };

        SetupThreadQueryable([thread]);
        SetupMessageQueryable(messages);

        var service = CreateService();
        var result = await service.ExportAsJsonAsync(threadId);

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.Id.ShouldBe(threadId);
        result.Data.Title.ShouldBe("My Thread");
        result.Data.MessageCount.ShouldBe(2);
        result.Data.Messages.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ExportAsJsonAsync_NotFound_Returns404()
    {
        var threadId = Guid.NewGuid();
        SetupThreadQueryable([]);

        var service = CreateService();
        var result = await service.ExportAsJsonAsync(threadId);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    // -------------------------------------------------------------------------
    // ExportAsMarkdownAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExportAsMarkdownAsync_Found_ReturnsMarkdownString()
    {
        var threadId = Guid.NewGuid();
        var thread = MakeThread(threadId, title: "Markdown Thread");
        var messages = new List<AgentThreadMessage>
        {
            MakeMessage(threadId, MessageRole.User, "User question", 1),
            MakeMessage(threadId, MessageRole.Assistant, "Assistant answer", 2)
        };

        SetupThreadQueryable([thread]);
        SetupMessageQueryable(messages);

        var service = CreateService();
        var result = await service.ExportAsMarkdownAsync(threadId);

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.ShouldContain("# Markdown Thread");
        result.Data.ShouldContain("User question");
        result.Data.ShouldContain("Assistant answer");
        result.Data.ShouldContain("### User (#1)");
        result.Data.ShouldContain("### Assistant (#2)");
    }

    [Fact]
    public async Task ExportAsMarkdownAsync_NotFound_Returns404()
    {
        var threadId = Guid.NewGuid();
        SetupThreadQueryable([]);

        var service = CreateService();
        var result = await service.ExportAsMarkdownAsync(threadId);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task ExportAsMarkdownAsync_EmptyMessages_ReturnsHeaderOnly()
    {
        var threadId = Guid.NewGuid();
        var thread = MakeThread(threadId, title: "Empty Thread");

        SetupThreadQueryable([thread]);
        SetupMessageQueryable([]);

        var service = CreateService();
        var result = await service.ExportAsMarkdownAsync(threadId);

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldContain("# Empty Thread");
        result.Data.ShouldContain("**Messages**: 0");
    }

    // -------------------------------------------------------------------------
    // GetOrCreateThreadAsync — new thread (threadId == null)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetOrCreateThreadAsync_NullThreadId_CreatesNewThread()
    {
        AgentThreadEntity? inserted = null;
        _threadRepo.Setup(r => r.InsertAsync(It.IsAny<AgentThreadEntity>(), It.IsAny<CancellationToken>()))
            .Callback<AgentThreadEntity, CancellationToken>((e, _) =>
            {
                e.Id = Guid.NewGuid(); // Simulate framework ID generation
                inserted = e;
            })
            .Returns(Task.CompletedTask);
        // GetAsync called for SaveThreadSerializedData
        _threadRepo.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => inserted != null && inserted.Id == id ? inserted : null);
        _threadRepo.Setup(r => r.UpdateAsync(It.IsAny<AgentThreadEntity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();
        var (context, threadId, isNew) = await service.GetOrCreateThreadAsync(null, null);

        isNew.ShouldBeTrue();
        threadId.ShouldNotBe(Guid.Empty);
        context.ShouldNotBeNull();
        _threadRepo.Verify(r => r.InsertAsync(It.IsAny<AgentThreadEntity>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetOrCreateThreadAsync_ValidThreadId_ReturnsExistingThread()
    {
        var threadId = Guid.NewGuid();
        var thread = MakeThread(threadId);
        _threadRepo.Setup(r => r.GetAsync(threadId, It.IsAny<CancellationToken>())).ReturnsAsync(thread);

        // No serialized data → rebuild from history
        SetupMessageQueryable([]);

        var service = CreateService();
        var (context, resultId, isNew) = await service.GetOrCreateThreadAsync(threadId, null);

        isNew.ShouldBeFalse();
        resultId.ShouldBe(threadId);
        context.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetOrCreateThreadAsync_ThreadNotFound_ThrowsBusinessException()
    {
        var threadId = Guid.NewGuid();
        _threadRepo.Setup(r => r.GetAsync(threadId, It.IsAny<CancellationToken>())).ReturnsAsync((AgentThreadEntity?)null);

        var service = CreateService();
        await Should.ThrowAsync<BusinessException>(() => service.GetOrCreateThreadAsync(threadId, null));
    }

    [Fact]
    public async Task GetOrCreateThreadAsync_AgentIdMismatch_ThrowsBusinessException()
    {
        var threadId = Guid.NewGuid();
        var agentIdA = Guid.NewGuid();
        var agentIdB = Guid.NewGuid();
        // Thread belongs to agentIdA, but caller requests agentIdB
        var thread = MakeThread(threadId, agentId: agentIdA);
        _agentRepo.Setup(r => r.GetAsync(agentIdB, It.IsAny<CancellationToken>())).ReturnsAsync(new Agent { Id = agentIdB, Name = "B" });
        _threadRepo.Setup(r => r.GetAsync(threadId, It.IsAny<CancellationToken>())).ReturnsAsync(thread);

        var service = CreateService();
        await Should.ThrowAsync<BusinessException>(() => service.GetOrCreateThreadAsync(threadId, agentIdB));
    }

    [Fact]
    public async Task GetOrCreateThreadAsync_AgentBoundThread_NoAgentId_ThrowsBusinessException()
    {
        var threadId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        // Thread is agent-bound but caller does not supply agentId
        var thread = MakeThread(threadId, agentId: agentId);
        _threadRepo.Setup(r => r.GetAsync(threadId, It.IsAny<CancellationToken>())).ReturnsAsync(thread);

        var service = CreateService();
        await Should.ThrowAsync<BusinessException>(() => service.GetOrCreateThreadAsync(threadId, null));
    }

    [Fact]
    public async Task GetOrCreateThreadAsync_AgentNotFound_ThrowsBusinessException()
    {
        var agentId = Guid.NewGuid();
        _agentRepo.Setup(r => r.GetAsync(agentId, It.IsAny<CancellationToken>())).ReturnsAsync((Agent?)null);

        var service = CreateService();
        await Should.ThrowAsync<BusinessException>(() => service.GetOrCreateThreadAsync(null, agentId));
    }

    [Fact]
    public async Task GetOrCreateThreadAsync_WithSerializedData_DeserializesContext()
    {
        var threadId = Guid.NewGuid();
        var context = new ConversationContext();
        context.Messages.Add(new ChatMessage(ChatRole.User, "Previous message"));
        var serialized = context.Serialize();

        var thread = MakeThread(threadId, serializedData: serialized);
        _threadRepo.Setup(r => r.GetAsync(threadId, It.IsAny<CancellationToken>())).ReturnsAsync(thread);

        var service = CreateService();
        var (resultContext, resultId, isNew) = await service.GetOrCreateThreadAsync(threadId, null);

        isNew.ShouldBeFalse();
        resultId.ShouldBe(threadId);
        resultContext.Messages.Count.ShouldBe(1);
        resultContext.Messages[0].Text.ShouldBe("Previous message");
    }

    // -------------------------------------------------------------------------
    // SaveMessageAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SaveMessageAsync_ThreadExists_SavesMessageWithCorrectOrder()
    {
        var threadId = Guid.NewGuid();

        // For AnyAsync check in SaveMessageAsync
        SetupThreadQueryable([MakeThread(threadId)]);

        // Existing messages — max order = 3
        var existingMessages = new List<AgentThreadMessage>
        {
            MakeMessage(threadId, order: 1),
            MakeMessage(threadId, order: 2),
            MakeMessage(threadId, order: 3)
        };
        SetupMessageQueryable(existingMessages);

        AgentThreadMessage? saved = null;
        _messageRepo.Setup(r => r.InsertAsync(It.IsAny<AgentThreadMessage>(), It.IsAny<CancellationToken>()))
            .Callback<AgentThreadMessage, CancellationToken>((m, _) => saved = m)
            .Returns(Task.CompletedTask);

        var service = CreateService();
        await service.SaveMessageAsync(threadId, MessageRole.User, "New message");

        _messageRepo.Verify(r => r.InsertAsync(It.IsAny<AgentThreadMessage>(), It.IsAny<CancellationToken>()), Times.Once);
        saved.ShouldNotBeNull();
        saved!.Order.ShouldBe(4);
        saved.Role.ShouldBe(MessageRole.User);
        saved.Content.ShouldBe("New message");
        saved.ThreadId.ShouldBe(threadId);
    }

    [Fact]
    public async Task SaveMessageAsync_FirstMessage_OrderIs1()
    {
        var threadId = Guid.NewGuid();

        SetupThreadQueryable([MakeThread(threadId)]);
        SetupMessageQueryable([]); // No existing messages

        AgentThreadMessage? saved = null;
        _messageRepo.Setup(r => r.InsertAsync(It.IsAny<AgentThreadMessage>(), It.IsAny<CancellationToken>()))
            .Callback<AgentThreadMessage, CancellationToken>((m, _) => saved = m)
            .Returns(Task.CompletedTask);

        var service = CreateService();
        await service.SaveMessageAsync(threadId, MessageRole.Assistant, "Hello!");

        saved.ShouldNotBeNull();
        saved!.Order.ShouldBe(1);
    }

    [Fact]
    public async Task SaveMessageAsync_ThreadNotFound_ReturnsWithoutSaving()
    {
        var threadId = Guid.NewGuid();
        SetupThreadQueryable([]);

        var service = CreateService();
        await service.SaveMessageAsync(threadId, MessageRole.User, "Ghost message");

        _messageRepo.Verify(r => r.InsertAsync(It.IsAny<AgentThreadMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
