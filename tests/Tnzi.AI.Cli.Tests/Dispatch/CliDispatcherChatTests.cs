namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// The dispatcher's side of a chat turn: attachments are checked and stored with the run and the message they came
/// with, a run only answers a user message of its own thread, active runs can be listed for reattaching, and tool
/// output keeps its blocks on the way back out. Also: write-back credentials tell consumer tools which thread they act for.
/// </summary>
public class CliDispatcherChatTests : IntegratedTestBase<CliChatDbContext>
{
    private static readonly Guid AgentId = Guid.Parse("11111111-1111-1111-1111-1111111111d1");
    private static readonly Guid RuntimeId = Guid.Parse("22222222-2222-2222-2222-2222222222d2");
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x02];

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<CliRun, Guid>, EFCoreRepository<CliChatDbContext, CliRun, Guid>>();
        services.AddScoped<IRepository<CliRunMessage, Guid>, EFCoreRepository<CliChatDbContext, CliRunMessage, Guid>>();
        services.AddScoped<IRepository<CliAgentBinding, Guid>, EFCoreRepository<CliChatDbContext, CliAgentBinding, Guid>>();
        services.AddScoped<IRepository<CliRuntime, Guid>, EFCoreRepository<CliChatDbContext, CliRuntime, Guid>>();
        services.AddScoped<IRepository<Agent, Guid>, EFCoreRepository<CliChatDbContext, Agent, Guid>>();
        services.AddScoped<IRepository<AgentThread, Guid>, EFCoreRepository<CliChatDbContext, AgentThread, Guid>>();
        services.AddScoped<IRepository<AgentThreadMessage, Guid>, EFCoreRepository<CliChatDbContext, AgentThreadMessage, Guid>>();
        services.AddScoped<IRepository<AgentAttachment, Guid>, EFCoreRepository<CliChatDbContext, AgentAttachment, Guid>>();
        services.AddScoped<IAgentAttachmentService, AgentAttachmentService>();
        services.AddScoped<CliRunTokenService>();

        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
    }

    [Fact]
    public async Task Attachments_AreStoredWithTheRunAndTheMessage_BeforeTheRunIsQueued()
    {
        var (threadId, messageId) = await SeedThreadWithQuestionAsync();

        var result = await CreateDispatcher().EnqueueAsync(new CliRunRequestDto
        {
            AgentId = AgentId, ThreadId = threadId, UserMessageId = messageId, Prompt = "look",
            Attachments = [new ImageContentPartDto { Base64Data = Convert.ToBase64String(Png), FileName = "a.png" }]
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        var stored = await DbContext.Set<AgentAttachment>().AsNoTracking().SingleAsync();
        stored.RunId.ShouldBe(result.Data);
        stored.MessageId.ShouldBe(messageId);
        stored.ThreadId.ShouldBe(threadId);
        stored.Origin.ShouldBe(AttachmentOrigin.Upload);
        stored.OwnerId.ShouldBe(TestHelper.DefaultTestUserId);
        (await DbContext.Set<CliRun>().AsNoTracking().SingleAsync()).UserMessageId.ShouldBe(messageId);
    }

    [Fact]
    public async Task Attachments_ForAProviderThatCannotReceiveThem_AreRefused_AndNothingIsQueued()
    {
        var (threadId, messageId) = await SeedThreadWithQuestionAsync();

        var result = await CreateDispatcher(acceptsAttachments: false).EnqueueAsync(new CliRunRequestDto
        {
            AgentId = AgentId, ThreadId = threadId, UserMessageId = messageId, Prompt = "look",
            Attachments = [new ImageContentPartDto { Base64Data = Convert.ToBase64String(Png) }]
        });

        result.Code.ShouldBe(415);
        result.ErrorCode.ShouldBe(ErrorCodes.UnsupportedMediaType);
        (await DbContext.Set<CliRun>().CountAsync()).ShouldBe(0);
        (await DbContext.Set<AgentAttachment>().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task AUserMessageFromAnotherThread_IsRefused_SoAttachmentsCannotBeHungOnSomeoneElsesConversation()
    {
        var (threadId, _) = await SeedThreadWithQuestionAsync();
        var (_, foreignMessage) = await SeedThreadWithQuestionAsync();

        var result = await CreateDispatcher().EnqueueAsync(new CliRunRequestDto
        {
            AgentId = AgentId, ThreadId = threadId, UserMessageId = foreignMessage, Prompt = "x"
        });

        result.Code.ShouldBe(400);
        (await DbContext.Set<CliRun>().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task ActiveOnly_ListsTheThreadsUnfinishedRuns()
    {
        var (threadId, messageId) = await SeedThreadWithQuestionAsync();
        await SeedRunAsync(threadId, CliRunStatus.Completed);
        var running = await SeedRunAsync(threadId, CliRunStatus.Running);
        var queued = await SeedRunAsync(threadId, CliRunStatus.Queued);

        var page = await CreateDispatcher().GetListAsync(new CliRunQueryDto { ThreadId = threadId, ActiveOnly = true });

        page.Data!.Items.Select(r => r.Id).OrderBy(id => id).ShouldBe(new[] { running, queued }.OrderBy(id => id));
        messageId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task StreamAndMessages_GiveBackToolOutputBlocks_SequenceAndTime()
    {
        var (threadId, _) = await SeedThreadWithQuestionAsync();
        var runId = await SeedRunAsync(threadId, CliRunStatus.Completed);
        var attachmentId = Guid.NewGuid();
        DbContext.Set<CliRunMessage>().Add(new CliRunMessage
        {
            RunId = runId, Sequence = 1, Type = CliAgentEventType.ToolResult, CallId = "c1", Tool = "shot",
            Output = "ok\n[image: image/png]",
            OutputBlocksJson = $$"""[{"type":"text","text":"ok"},{"type":"image","mediaType":"image/png","attachmentId":"{{attachmentId}}"}]"""
        });
        await DbContext.SaveChangesAsync();

        var events = new List<CliAgentEvent>();
        await foreach (var evt in CreateDispatcher().StreamAsync(runId))
        {
            events.Add(evt);
        }

        var result = events.ShouldHaveSingleItem();
        result.Sequence.ShouldBe(1);
        result.CreatedAt.ShouldNotBeNull();
        var blocks = result.OutputBlocks.ShouldNotBeNull();
        blocks.Select(b => b.Type).ShouldBe(["text", "image"]);
        blocks[1].AttachmentId.ShouldBe(attachmentId);

        var messages = (await CreateDispatcher().GetMessagesAsync(runId)).Data!;
        messages.Single().OutputBlocks!.Last().AttachmentId.ShouldBe(attachmentId);
    }

    [Fact]
    public async Task AWriteBackCredential_SaysWhichThreadItsRunActsFor()
    {
        var (threadId, _) = await SeedThreadWithQuestionAsync();
        var runId = await SeedRunAsync(threadId, CliRunStatus.Running);
        var tokens = ServiceProvider.GetRequiredService<CliRunTokenService>();
        var run = await DbContext.Set<CliRun>().AsNoTracking().SingleAsync(r => r.Id == runId);

        var token = await tokens.IssueAsync(run, TimeSpan.FromHours(1), CancellationToken.None);
        var credential = await tokens.ValidateAsync(token);

        credential.ShouldNotBeNull().ThreadId.ShouldBe(threadId);
    }

    [Fact]
    public async Task TheBinding_SaysWhetherItsProviderTakesAttachments()
    {
        await SeedBindingAsync();
        var service = new CliAgentBindingService(
            ServiceProvider.GetRequiredService<IRepository<CliAgentBinding, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRuntime, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<Agent, Guid>>(),
            ServiceProvider,
            Registry(acceptsAttachments: true));

        (await service.GetByAgentIdAsync(AgentId))!.AcceptsAttachments.ShouldBeTrue();
    }

    // ---------------------------------------------------------------- helpers

    private CliAgentDispatcher CreateDispatcher(bool acceptsAttachments = true)
    {
        var options = new Mock<IOptionsMonitor<CliAgentOptions>>();
        options.Setup(m => m.CurrentValue).Returns(new CliAgentOptions { Enabled = true, PollInterval = TimeSpan.FromMilliseconds(20) });

        return new CliAgentDispatcher(
            ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRunMessage, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliAgentBinding, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRuntime, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<AgentThread, Guid>>(),
            TestAgents.Repository(AgentId),
            new CliRunSignalHub(),
            new CliRunCancellationRegistry(),
            options.Object,
            ServiceProvider,
            providerRegistry: Registry(acceptsAttachments),
            attachmentService: ServiceProvider.GetRequiredService<IAgentAttachmentService>(),
            threadMessageRepository: ServiceProvider.GetRequiredService<IRepository<AgentThreadMessage, Guid>>());
    }

    private static ICliProviderRegistry Registry(bool acceptsAttachments)
    {
        var registry = new Mock<ICliProviderRegistry>();
        registry.Setup(r => r.Find("claude")).Returns(CliBuiltInProviders.All["claude"] with { AcceptsAttachments = acceptsAttachments });
        return registry.Object;
    }

    private async Task SeedBindingAsync()
    {
        if (await DbContext.Set<CliAgentBinding>().AnyAsync())
        {
            return;
        }

        DbContext.Set<Agent>().Add(new Agent { Id = AgentId, Name = "cli-bound", Provider = "claude" });
        DbContext.Set<CliAgentBinding>().Add(new CliAgentBinding { AgentId = AgentId, CliRuntimeId = RuntimeId });
        DbContext.Set<CliRuntime>().Add(new CliRuntime
        {
            Id = RuntimeId, HostId = "TEST-HOST", ProviderKey = "claude", Name = "claude", ExecutablePath = "/x/claude",
            Status = CliRuntimeStatus.Online
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private async Task<(Guid ThreadId, Guid MessageId)> SeedThreadWithQuestionAsync()
    {
        await SeedBindingAsync();
        var thread = new AgentThread { AgentId = AgentId, Title = "t", LastActivityTime = DateTime.UtcNow };
        DbContext.Set<AgentThread>().Add(thread);
        var message = new AgentThreadMessage { ThreadId = thread.Id, Role = MessageRole.User, Content = "q", Order = 1 };
        await DbContext.SaveChangesAsync();
        message.ThreadId = thread.Id;
        DbContext.Set<AgentThreadMessage>().Add(message);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return (thread.Id, message.Id);
    }

    private async Task<Guid> SeedRunAsync(Guid threadId, CliRunStatus status)
    {
        var run = new CliRun { AgentId = AgentId, CliRuntimeId = RuntimeId, ThreadId = threadId, Status = status, Prompt = "p" };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return run.Id;
    }
}
