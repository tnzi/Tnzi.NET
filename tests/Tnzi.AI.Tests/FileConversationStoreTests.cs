namespace Tnzi.AI.Tests;

/// <summary>
/// FileConversationStore 单元测试 — CRUD + 路径遍历防护
/// </summary>
public class FileConversationStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileConversationStore _store;

    public FileConversationStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tnzi-conv-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var options = Microsoft.Extensions.Options.Options.Create(new FileConversationStoreOptions
        {
            ProjectRoot = _tempDir,
            ConversationDirectory = "conversations"
        });

        _store = new FileConversationStore(options, Mock.Of<ILogger<FileConversationStore>>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    #region Path Traversal Security

    [Fact]
    public async Task GetOrCreate_PathTraversal_ThrowsArgumentException()
    {
        await Should.ThrowAsync<ArgumentException>(async () =>
            await _store.GetOrCreateAsync("../etc/passwd"));
    }

    [Fact]
    public async Task GetOrCreate_ForwardSlashInId_ThrowsArgumentException()
    {
        await Should.ThrowAsync<ArgumentException>(async () =>
            await _store.GetOrCreateAsync("some/nested/id"));
    }

    [Fact]
    public async Task GetOrCreate_BackslashInId_ThrowsArgumentException()
    {
        await Should.ThrowAsync<ArgumentException>(async () =>
            await _store.GetOrCreateAsync("some\\nested\\id"));
    }

    #endregion

    #region CRUD Operations

    [Fact]
    public async Task Save_AndGetOrCreate_RoundTrips()
    {
        var conversationId = "test-conv-1";
        var context = new ConversationContext();
        context.Messages.Add(new ChatMessage(ChatRole.User, "Hello"));
        context.Messages.Add(new ChatMessage(ChatRole.Assistant, "Hi there!"));

        await _store.SaveAsync(conversationId, context);

        var loaded = await _store.GetOrCreateAsync(conversationId);
        loaded.Messages.Count.ShouldBe(2);
        loaded.Messages[0].Role.ShouldBe(ChatRole.User);
        loaded.Messages[0].Text.ShouldBe("Hello");
        loaded.Messages[1].Role.ShouldBe(ChatRole.Assistant);
        loaded.Messages[1].Text.ShouldBe("Hi there!");
    }

    [Fact]
    public async Task GetOrCreate_NewConversation_ReturnsEmptyContext()
    {
        var context = await _store.GetOrCreateAsync("brand-new");

        context.ShouldNotBeNull();
        context.Messages.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Delete_RemovesConversation()
    {
        var conversationId = "to-delete";
        var context = new ConversationContext();
        context.Messages.Add(new ChatMessage(ChatRole.User, "Temp"));
        await _store.SaveAsync(conversationId, context);

        // 验证存在
        var loaded = await _store.GetOrCreateAsync(conversationId);
        loaded.Messages.Count.ShouldBe(1);

        // 删除
        await _store.DeleteAsync(conversationId);

        // 再次获取应返回空上下文
        var afterDelete = await _store.GetOrCreateAsync(conversationId);
        afterDelete.Messages.Count.ShouldBe(0);
    }

    [Fact]
    public async Task List_ReturnsAllConversations()
    {
        // 创建多个对话
        for (var i = 1; i <= 3; i++)
        {
            var ctx = new ConversationContext();
            ctx.Messages.Add(new ChatMessage(ChatRole.User, $"Message from conv {i}"));
            await _store.SaveAsync($"conv-{i}", ctx);
        }

        var summaries = await _store.ListAsync();

        summaries.Count.ShouldBe(3);
        summaries.ShouldContain(s => s.Id == "conv-1");
        summaries.ShouldContain(s => s.Id == "conv-2");
        summaries.ShouldContain(s => s.Id == "conv-3");
    }

    [Fact]
    public async Task AppendMessage_AddsToConversation()
    {
        var conversationId = "append-test";
        await _store.AppendMessageAsync(conversationId, "user", "First message");
        await _store.AppendMessageAsync(conversationId, "assistant", "Response");

        var loaded = await _store.GetOrCreateAsync(conversationId);
        loaded.Messages.Count.ShouldBe(2);
        loaded.Messages[0].Role.ShouldBe(ChatRole.User);
        loaded.Messages[1].Role.ShouldBe(ChatRole.Assistant);
    }

    #endregion
}
