using Mapster;
using MapsterMapper;
using Microsoft.Extensions.Options;
using Moq;
using Tnzi.Chat.Mappings;
using Tnzi.Chat.Services;
using Tnzi.Chat.Services.Interfaces;
using Tnzi.Data;
using Tnzi.Domain.Repositories;
using Tnzi.EFCore;
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

        var userRepo = new Mock<IRepository<User, Guid>>();
        userRepo.Setup(r => r.ToListAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<User>());
        services.AddScoped(_ => userRepo.Object);

        // ChatContactService now joins UserDetail for display name + avatar — register
        // an empty mock so DI resolves (names fall back to UserName, avatar null).
        var userDetailRepo = new Mock<IRepository<UserDetail, Guid>>();
        userDetailRepo.Setup(r => r.ToListAsync(It.IsAny<System.Linq.Expressions.Expression<Func<UserDetail, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserDetail>());
        services.AddScoped(_ => userDetailRepo.Object);

        // Multi-tenancy OFF by default (BroadcastService guards the All path on this).
        services.AddSingleton<IOptions<MultiTenancyOptions>>(Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions()));

        // Chat options (IOptionsSnapshot consumers). Tests override ConfigureChatOptions to
        // exercise the enforcement paths (disabled groups, member cap, file-message toggle).
        services.AddOptions<Tnzi.Chat.Options.ChatOptions>().Configure(o => ConfigureChatOptions(o));

        // Register real UnitOfWorkManager so ExecuteInUnitOfWorkAsync exercises the deferred-save path.
        // IEntityManager mock tells UnitOfWorkManager which DbContext types to discover.
        var entityManagerMock = new Mock<IEntityManager>();
        entityManagerMock.Setup(m => m.GetAllDbContextTypes()).Returns(new[] { typeof(ChatTestDbContext) });
        entityManagerMock.Setup(m => m.Initialize());
        services.AddSingleton(_ => entityManagerMock.Object);
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();

        services.AddScoped<IGroupService, GroupService>();
        services.AddScoped<IBroadcastService, BroadcastService>();

        // IPresenceService — mock returning empty list (presence enrichment not under test in integration suite)
        var presenceMock = new Mock<IPresenceService>();
        presenceMock.Setup(p => p.ResolveEffectiveAsync(It.IsAny<IReadOnlyCollection<Guid>>()))
            .ReturnsAsync(Array.Empty<UserPresenceDto>());
        services.AddScoped(_ => presenceMock.Object);

        services.AddScoped<IChatContactService, ChatContactService>();
        services.AddScoped<IConversationService, ConversationService>();

        // Admin maintenance service — IConnectionManager is an optional ctor dependency
        // (no SignalR in the integration suite), so it falls back to null and online
        // counts resolve to 0 / offline.
        services.AddScoped<IChatAdminService, ChatAdminService>();

        services.AddScoped<IChatConfigService, ChatConfigService>();
    }

    /// <summary>Override in a test class to change ChatOptions (defaults = everything enabled).</summary>
    protected virtual void ConfigureChatOptions(Tnzi.Chat.Options.ChatOptions options)
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
        modelBuilder.ApplyConfiguration(new Tnzi.Chat.Entities.Configs.UserPresenceConfiguration());
        modelBuilder.ApplyConfiguration(new Tnzi.Chat.Entities.Configs.BroadcastLogConfiguration());

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}
