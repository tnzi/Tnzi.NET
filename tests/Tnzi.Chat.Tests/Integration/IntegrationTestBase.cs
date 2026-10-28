using Mapster;
using MapsterMapper;
using Microsoft.Extensions.Options;
using Tnzi.Chat.Mappings;
using Tnzi.Data;
using Tnzi.Identity.Entities;
using Tnzi.Mapster;
using Tnzi.MultiTenancy;

namespace Tnzi.Chat.Tests.Integration;

public class IntegrationTestBase : IntegratedTestBase<ChatTestDbContext>, IDisposable
{
    protected static Guid CurrentUserId => TestHelper.DefaultTestUserId;

    public IntegrationTestBase()
    {
        var config = new TypeAdapterConfig();
        new ChatMappingConfig().Configure(config);
        MapperExtensions.SetMapper(new Mapper(config));
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<Conversation, Guid>>(sp =>
            new EFCoreRepository<ChatTestDbContext, Conversation, Guid>(sp.GetRequiredService<ChatTestDbContext>(), serviceProvider: sp));
        services.AddScoped<IRepository<ConversationMember, Guid>>(sp =>
            new EFCoreRepository<ChatTestDbContext, ConversationMember, Guid>(sp.GetRequiredService<ChatTestDbContext>(), serviceProvider: sp));
        services.AddScoped<IRepository<ChatMessage, Guid>>(sp =>
            new EFCoreRepository<ChatTestDbContext, ChatMessage, Guid>(sp.GetRequiredService<ChatTestDbContext>(), serviceProvider: sp));
        services.AddScoped<IRepository<UserPresence, Guid>>(sp =>
            new EFCoreRepository<ChatTestDbContext, UserPresence, Guid>(sp.GetRequiredService<ChatTestDbContext>(), serviceProvider: sp));
        services.AddScoped<IRepository<BroadcastLog, Guid>>(sp =>
            new EFCoreRepository<ChatTestDbContext, BroadcastLog, Guid>(sp.GetRequiredService<ChatTestDbContext>(), serviceProvider: sp));
        services.AddScoped<IRepository<MessageBlock, Guid>>(sp =>
            new EFCoreRepository<ChatTestDbContext, MessageBlock, Guid>(sp.GetRequiredService<ChatTestDbContext>(), serviceProvider: sp));

        var userRepo = new Mock<IRepository<User, Guid>>();
        userRepo.Setup(r => r.ToListAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<User>());
        // 本套用例拿 Guid.NewGuid() 当单聊对象：一律视为存在的账号。
        // 关心「对象不存在 / 是超管」的用例自己换掉它（见 DirectChatTargetTests）。
        userRepo.Setup(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        // 建群 / 加人 / 广播按 id 批量核对账号是否存在：同一口径，一律存在。
        userRepo.Setup(r => r.GetListAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<Guid> ids, CancellationToken _) => ExistingUsers(ids));
        services.AddScoped(_ => userRepo.Object);

        // ChatContactService now joins UserDetail for display name + avatar - register
        // an empty mock so DI resolves (names fall back to UserName, avatar null).
        var userDetailRepo = new Mock<IRepository<UserDetail, Guid>>();
        userDetailRepo.Setup(r => r.ToListAsync(It.IsAny<System.Linq.Expressions.Expression<Func<UserDetail, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserDetail>());
        services.AddScoped(_ => userDetailRepo.Object);

        // Multi-tenancy OFF by default (BroadcastService guards the All path on this).
        services.AddSingleton<IOptions<MultiTenancyOptions>>(Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions()));

        // 用户的租户边界（User 不是 IMultiTenant，聊天的目录 / 资料卡 / 建群 / 单聊 / 广播都要问它）。
        // 真实实现：多租户关闭时它不裁剪、不查库，与此前逐字相同；租户收口的用例自己换掉它。
        services.AddScoped<Tnzi.Identity.Services.IUserTenantScopeProvider, Tnzi.Identity.Services.UserTenantScopeProvider>();

        // Chat options (IOptionsSnapshot consumers). Tests override ConfigureChatOptions to
        // exercise the enforcement paths (disabled groups, member cap, file-message toggle).
        services.AddOptions<Tnzi.Chat.Options.ChatOptions>().Configure(o => ConfigureChatOptions(o));

        // Presence options - EnablePresence / AllowInvisible / auto-away now live on the Presence
        // module and are consumed by ChatConfigService + ChatAdminService via IOptionsSnapshot.
        services.AddOptions<Tnzi.Identity.Presence.Options.PresenceOptions>().Configure(o => ConfigurePresenceOptions(o));

        // Register real UnitOfWorkManager so ExecuteInUnitOfWorkAsync exercises the deferred-save path.
        // IEntityManager mock tells UnitOfWorkManager which DbContext types to discover.
        var entityManagerMock = new Mock<IEntityManager>();
        entityManagerMock.Setup(m => m.GetAllDbContextTypes()).Returns(new[] { typeof(ChatTestDbContext) });
        entityManagerMock.Setup(m => m.Initialize());
        services.AddSingleton(_ => entityManagerMock.Object);
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();

        services.AddScoped<IGroupService, GroupService>();
        services.AddScoped<IBroadcastService, BroadcastService>();

        // IPresenceService - mock returning empty list (presence enrichment not under test in integration suite)
        var presenceMock = new Mock<IPresenceService>();
        presenceMock.Setup(p => p.ResolveEffectiveAsync(It.IsAny<IReadOnlyCollection<Guid>>()))
            .ReturnsAsync(Array.Empty<UserPresenceDto>());
        services.AddScoped(_ => presenceMock.Object);

        // Chat access gate. No IPermissionChecker / IFunctionAuthorizationService is
        // registered in the integration suite, so ChatAccessService fails open (everyone
        // may use chat) - enforcement/isolation tests register mocks to flip it on.
        services.AddScoped<IChatAccessService, ChatAccessService>();

        services.AddScoped<IChatContactService, ChatContactService>();

        // 写入侧的文件归属校验（IFileReadAccessProbe，实现随 Tnzi.Storage 注册）。
        // 这里默认放行：本套用例绝大多数关心的是别的事，而没有它每一条带附件的消息
        // 都会答 501。关心这道校验本身的用例自己换掉它（见 ChatFileAttachmentGuardTests）。
        services.AddScoped(_ => PermissiveFileAccess());

        services.AddScoped<IConversationService, ConversationService>();

        // Admin maintenance service - IConnectionManager is an optional ctor dependency
        // (no SignalR in the integration suite), so it falls back to null and online
        // counts resolve to 0 / offline.
        services.AddScoped<IChatAdminService, ChatAdminService>();

        services.AddScoped<IChatConfigService, ChatConfigService>();
    }

    /// <summary>把每个 id 都当成一个存在的账号（单租户、无租户归属）。</summary>
    protected static List<User> ExistingUsers(IEnumerable<Guid> ids)
        => ids.Distinct().Select(id => new User { Id = id, UserName = $"user-{id:N}" }).ToList();

    /// <summary>一律放行的文件归属探针。</summary>
    private static IFileReadAccessProbe PermissiveFileAccess()
    {
        var probe = new Mock<IFileReadAccessProbe>();
        probe.Setup(p => p.CanReadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return probe.Object;
    }

    /// <summary>Override in a test class to change ChatOptions (defaults = everything enabled).</summary>
    protected virtual void ConfigureChatOptions(Tnzi.Chat.Options.ChatOptions options)
    {
    }

    /// <summary>Override in a test class to change PresenceOptions (defaults = presence + auto-away enabled).</summary>
    protected virtual void ConfigurePresenceOptions(Tnzi.Identity.Presence.Options.PresenceOptions options)
    {
    }
}

public class ChatTestDbContext : TnziDbContext<ChatTestDbContext>
{
    public ChatTestDbContext(
        DbContextOptions<ChatTestDbContext> options,
        Security.Claims.ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new Tnzi.Chat.Entities.Configs.ConversationConfiguration());
        modelBuilder.ApplyConfiguration(new Tnzi.Chat.Entities.Configs.ConversationMemberConfiguration());
        modelBuilder.ApplyConfiguration(new Tnzi.Chat.Entities.Configs.ChatMessageConfiguration());
        modelBuilder.ApplyConfiguration(new Tnzi.Identity.Presence.Entities.Configs.UserPresenceConfiguration());
        modelBuilder.ApplyConfiguration(new Tnzi.Chat.Entities.Configs.BroadcastLogConfiguration());
        modelBuilder.ApplyConfiguration(new Tnzi.Chat.Entities.Configs.MessageBlockConfiguration());

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}
