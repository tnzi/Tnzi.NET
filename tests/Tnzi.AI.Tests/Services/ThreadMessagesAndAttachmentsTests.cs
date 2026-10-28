using AgentThreadEntity = Tnzi.AI.Entities.AgentThread;

namespace Tnzi.AI.Tests.Services;

public class ThreadAttachmentDbContext : TnziDbContext<ThreadAttachmentDbContext>
{
    public ThreadAttachmentDbContext(DbContextOptions<ThreadAttachmentDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AgentConfiguration());
        modelBuilder.ApplyConfiguration(new ProviderConfiguration());
        modelBuilder.ApplyConfiguration(new AgentThreadConfiguration());
        modelBuilder.ApplyConfiguration(new AgentThreadMessageConfiguration());
        modelBuilder.ApplyConfiguration(new AgentAttachmentConfiguration());
        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

/// <summary>
/// Thread messages page backwards and carry their attachments; a rewind removes messages with their attachments;
/// attachment content is readable only by the owner of the thread it belongs to.
/// </summary>
/// <remarks>Real repositories on SQLite: the queries are what is under test.</remarks>
public class ThreadMessagesAndAttachmentsTests : IntegratedTestBase<ThreadAttachmentDbContext>
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01];

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<Agent, Guid>, EFCoreRepository<ThreadAttachmentDbContext, Agent, Guid>>();
        services.AddScoped<IRepository<AgentThreadEntity, Guid>, EFCoreRepository<ThreadAttachmentDbContext, AgentThreadEntity, Guid>>();
        services.AddScoped<IRepository<AgentThreadMessage, Guid>, EFCoreRepository<ThreadAttachmentDbContext, AgentThreadMessage, Guid>>();
        services.AddScoped<IRepository<AgentAttachment, Guid>, EFCoreRepository<ThreadAttachmentDbContext, AgentAttachment, Guid>>();
        services.AddScoped<AgentThreadService>();
        services.AddScoped<IAgentAttachmentService, AgentAttachmentService>();

        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
    }

    // ---------------------------------------------------------------- paging back

    [Fact]
    public async Task Messages_PageBackwards_OldestFirstWithinAPage_AndSayWhetherMoreRemain()
    {
        var threadId = await SeedThreadAsync();
        for (var i = 1; i <= 5; i++)
        {
            await Threads.SaveMessageAsync(threadId, MessageRole.User, $"m{i}");
        }

        var newest = (await Threads.GetMessagesAsync(threadId, beforeOrder: null, limit: 2)).Data!;
        newest.Messages.Select(m => m.Content).ShouldBe(["m4", "m5"]);
        newest.HasMore.ShouldBeTrue();

        var older = (await Threads.GetMessagesAsync(threadId, beforeOrder: newest.Messages[0].Order, limit: 2)).Data!;
        older.Messages.Select(m => m.Content).ShouldBe(["m2", "m3"]);
        older.HasMore.ShouldBeTrue();

        var oldest = (await Threads.GetMessagesAsync(threadId, beforeOrder: older.Messages[0].Order, limit: 2)).Data!;
        oldest.Messages.Select(m => m.Content).ShouldBe(["m1"]);
        oldest.HasMore.ShouldBeFalse();
    }

    [Fact]
    public async Task Messages_CarryTheirAttachmentsAndStatus_WithoutTheBytes()
    {
        var threadId = await SeedThreadAsync();
        var question = await Threads.SaveMessageAsync(threadId, MessageRole.User, "look");
        var reply = await Threads.SaveMessageAsync(threadId, MessageRole.Assistant, "half an answer");
        await DbContext.Set<AgentThreadMessage>().Where(m => m.Id == reply)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, ThreadMessageStatuses.Stopped));
        await Attachments.SaveAsync([Upload(threadId, question, "a.png")]);
        DbContext.ChangeTracker.Clear();

        var detail = (await Threads.GetDetailAsync(threadId)).Data!;

        var attachment = detail.Messages.Single(m => m.Id == question).Attachments.ShouldNotBeNull().ShouldHaveSingleItem();
        attachment.FileName.ShouldBe("a.png");
        attachment.MediaType.ShouldBe("image/png");
        attachment.Size.ShouldBe(Png.Length);
        attachment.Origin.ShouldBe(AttachmentOrigin.Upload);
        detail.Messages.Single(m => m.Id == reply).Status.ShouldBe(ThreadMessageStatuses.Stopped);
        detail.Messages.Single(m => m.Id == reply).Attachments.ShouldBeNull();
    }

    [Fact]
    public async Task AUserMessage_KeepsItsTurnMetadata_AndAUsageLimitReplyItsResetTime()
    {
        var threadId = await SeedThreadAsync();
        using var targets = JsonDocument.Parse("""["pc-1","pc-2"]""");
        var question = await Threads.SaveUserMessageAsync(threadId, "restart them",
            new Dictionary<string, JsonElement> { ["targets"] = targets.RootElement.Clone() });
        var reply = await Threads.SaveMessageAsync(threadId, MessageRole.Assistant, string.Empty);
        var resetsAt = new DateTime(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);
        await DbContext.Set<AgentThreadMessage>().Where(m => m.Id == reply)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, ThreadMessageStatuses.UsageLimit)
                .SetProperty(m => m.UsageLimitResetsAt, resetsAt));
        DbContext.ChangeTracker.Clear();

        var page = (await Threads.GetMessagesAsync(threadId, beforeOrder: null, limit: 10)).Data!;

        var asked = page.Messages.Single(m => m.Id == question);
        asked.Metadata.ShouldNotBeNull()["targets"].EnumerateArray().Select(e => e.GetString()).ShouldBe(["pc-1", "pc-2"]);
        var limited = page.Messages.Single(m => m.Id == reply);
        limited.Status.ShouldBe(ThreadMessageStatuses.UsageLimit);
        limited.UsageLimitResetsAt.ShouldBe(resetsAt);
        limited.Metadata.ShouldBeNull();

        (await Threads.FindUserMessageAsync(threadId, null))!.MetadataJson.ShouldNotBeNull().ShouldContain("pc-2");
    }

    // ---------------------------------------------------------------- rewind

    [Fact]
    public async Task Rewinding_RemovesTheMessageEverythingAfterIt_AndTheirAttachments()
    {
        var threadId = await SeedThreadAsync();
        var first = await Threads.SaveMessageAsync(threadId, MessageRole.User, "first");
        await Threads.SaveMessageAsync(threadId, MessageRole.Assistant, "first answer");
        var second = await Threads.SaveMessageAsync(threadId, MessageRole.User, "second");
        await Threads.SaveMessageAsync(threadId, MessageRole.Assistant, "second answer");
        await Attachments.SaveAsync([Upload(threadId, first, "keep.png"), Upload(threadId, second, "drop.png")]);

        var target = (await Threads.FindUserMessageAsync(threadId, second))!;
        var removed = await Threads.DeleteMessagesFromAsync(threadId, target.Order);

        removed.ShouldBe(2);
        (await DbContext.Set<AgentThreadMessage>().Select(m => m.Content).ToListAsync()).ShouldBe(["first", "first answer"]);
        (await DbContext.Set<AgentAttachment>().Select(a => a.FileName).ToListAsync()).ShouldBe(["keep.png"]);

        // the next message takes the freed place
        var again = await Threads.SaveMessageAsync(threadId, MessageRole.User, "second, edited");
        (await DbContext.Set<AgentThreadMessage>().SingleAsync(m => m.Id == again)).Order.ShouldBe(target.Order);
    }

    [Fact]
    public async Task FindUserMessage_FindsTheLastQuestion_AndOnlyUserMessagesOfTheThread()
    {
        var threadId = await SeedThreadAsync();
        await Threads.SaveMessageAsync(threadId, MessageRole.User, "q1");
        var answer = await Threads.SaveMessageAsync(threadId, MessageRole.Assistant, "a1");
        var q2 = await Threads.SaveMessageAsync(threadId, MessageRole.User, "q2");
        await Threads.SaveMessageAsync(threadId, MessageRole.Assistant, "a2");

        (await Threads.FindUserMessageAsync(threadId, null))!.Id.ShouldBe(q2);
        (await Threads.FindUserMessageAsync(threadId, answer)).ShouldBeNull("an assistant message cannot be edited");
        (await Threads.FindUserMessageAsync(Guid.NewGuid(), q2)).ShouldBeNull("nor a message of another thread");
    }

    // ---------------------------------------------------------------- reading attachments

    [Fact]
    public async Task AttachmentContent_IsReadableByTheThreadsOwner()
    {
        var threadId = await SeedThreadAsync();
        var question = await Threads.SaveMessageAsync(threadId, MessageRole.User, "look");
        var id = (await Attachments.SaveAsync([Upload(threadId, question, "a.png")])).Single();

        var content = await Attachments.GetContentAsync(threadId, id, TestHelper.DefaultTestUserId);

        content.Succeeded.ShouldBeTrue();
        content.Data!.Content.ShouldBe(Png);
        content.Data.MediaType.ShouldBe("image/png");
        content.Data.FileName.ShouldBe("a.png");
    }

    [Fact]
    public async Task AttachmentContent_IsNotFound_ForAnyoneElse_OrThroughAnotherThread()
    {
        var threadId = await SeedThreadAsync();
        var otherThread = await SeedThreadAsync();
        var question = await Threads.SaveMessageAsync(threadId, MessageRole.User, "look");
        var id = (await Attachments.SaveAsync([Upload(threadId, question, "a.png")])).Single();

        (await Attachments.GetContentAsync(threadId, id, Guid.NewGuid())).Code.ShouldBe(404);
        (await Attachments.GetContentAsync(otherThread, id, TestHelper.DefaultTestUserId)).Code.ShouldBe(404,
            "owning some thread is not owning this attachment's thread");
    }

    [Fact]
    public async Task ToolImages_AreBoundToTheReplyOnceItIsWritten()
    {
        var threadId = await SeedThreadAsync();
        var runId = Guid.NewGuid();
        await Attachments.SaveAsync(
        [
            new NewAgentAttachment
            {
                Origin = AttachmentOrigin.ToolResult, MediaType = "image/png", Content = Png, ThreadId = threadId, RunId = runId,
                OwnerId = TestHelper.DefaultTestUserId
            }
        ]);
        var reply = await Threads.SaveMessageAsync(threadId, MessageRole.Assistant, "here");

        await Attachments.BindToMessageAsync(runId, AttachmentOrigin.ToolResult, reply);

        (await DbContext.Set<AgentAttachment>().AsNoTracking().SingleAsync()).MessageId.ShouldBe(reply);
        (await Attachments.GetForRunAsync(runId, AttachmentOrigin.Upload)).ShouldBeEmpty("uploads and tool images are kept apart");
    }

    [Fact]
    public async Task DeletingTheThread_DeletesItsAttachments()
    {
        var threadId = await SeedThreadAsync();
        var question = await Threads.SaveMessageAsync(threadId, MessageRole.User, "look");
        await Attachments.SaveAsync([Upload(threadId, question, "a.png")]);

        var handler = new ThreadCleanupHandler(
            NullLogger<ThreadCleanupHandler>.Instance,
            messageRepository: ServiceProvider.GetRequiredService<IRepository<AgentThreadMessage, Guid>>(),
            attachmentRepository: ServiceProvider.GetRequiredService<IRepository<AgentAttachment, Guid>>());
        await handler.HandleAsync(new ThreadDeletedEvent { ThreadId = threadId });

        (await DbContext.Set<AgentAttachment>().CountAsync()).ShouldBe(0);
    }

    // ---------------------------------------------------------------- helpers

    private AgentThreadService Threads => ServiceProvider.GetRequiredService<AgentThreadService>();

    private IAgentAttachmentService Attachments => ServiceProvider.GetRequiredService<IAgentAttachmentService>();

    private static NewAgentAttachment Upload(Guid threadId, Guid messageId, string fileName) => new()
    {
        Origin = AttachmentOrigin.Upload,
        MediaType = "image/png",
        FileName = fileName,
        Content = Png,
        ThreadId = threadId,
        MessageId = messageId,
        OwnerId = TestHelper.DefaultTestUserId
    };

    private async Task<Guid> SeedThreadAsync()
    {
        var thread = new AgentThreadEntity { Title = "t", LastActivityTime = DateTime.UtcNow };
        DbContext.Set<AgentThreadEntity>().Add(thread);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return thread.Id;
    }
}
