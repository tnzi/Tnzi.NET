using System.Linq.Expressions;
using Microsoft.Extensions.Options;
using MockQueryable;
using Tnzi.Identity.Entities;
using Tnzi.MultiTenancy;

namespace Tnzi.Chat.Tests.Services;

/// <summary>
/// 多租户开启时，程序化通知要落进<b>收件人自己租户</b>的系统会话，而不是调用方当时的环境租户。
/// </summary>
/// <remarks>
/// <para>
/// ★ 会话 / 成员 / 消息都是 <c>IMultiTenant</c>，全局过滤器是严格等值，新行的 <c>TenantId</c> 取环境租户。
/// 文档明确允许在后台任务、事件处理器里调 <c>NotifyUsersAsync</c>：那里没有租户上下文：按环境租户查，
/// 查不到收件人在租户 A 的系统会话，于是在 <c>TenantId = null</c> 下另建一套会话 + 成员 + 消息，
/// 收件人永远看不见，而返回值与 <c>BroadcastLog</c> 都报投递成功。
/// </para>
/// <para>
/// 刻意用一个<b>真的开了多租户</b>的 DbContext（租户列被映射、全局过滤器在场）：单租户模型上这件事根本照不出来。
/// </para>
/// </remarks>
public class BroadcastTenantDeliveryTests : Integration.IntegrationTestBase
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly Guid UserOfA = Guid.NewGuid();

    private static readonly List<User> Users =
    [
        new() { Id = UserOfA, UserName = "alice", TenantId = TenantA },
    ];

    private IBroadcastService Broadcast => ServiceProvider.GetRequiredService<IBroadcastService>();
    private ICurrentTenant CurrentTenant => ServiceProvider.GetRequiredService<ICurrentTenant>();

    protected override void ConfigureDbContextOptions(DbContextOptionsBuilder options)
        => options.UseTnziMultiTenancy(true);

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        services.AddSingleton<IOptions<MultiTenancyOptions>>(Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions { Enabled = true }));

        // 真实的租户上下文：Change 写进 AsyncLocal，DbContext 的过滤器与新行的租户戳都读它。
        // 测试用户不带租户（宿主侧 / 后台任务的形状）。
        services.AddScoped<ICurrentTenant>(sp => new Tnzi.MultiTenancy.CurrentTenant(sp.GetRequiredService<ICurrentUser>()));

        var userRepo = new Mock<IRepository<User, Guid>>();
        userRepo.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(() => Users.BuildMock());
        userRepo.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<User, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<User, bool>> p, CancellationToken _) => Users.Where(p.Compile()).ToList());
        userRepo.Setup(r => r.GetListAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<Guid> ids, CancellationToken _) => Users.Where(u => ids.Contains(u.Id)).ToList());
        services.AddScoped(_ => userRepo.Object);
    }

    private async Task<Conversation> SeedSystemConversationInTenantAAsync()
    {
        using (CurrentTenant.Change(TenantA))
        {
            var conversation = new Conversation
            {
                Type = ConversationType.System,
                DirectKey = $"system:{UserOfA:N}",
                Title = "System Notifications",
                MemberCount = 1
            };
            DbContext.Add(conversation);
            await DbContext.SaveChangesAsync();
            DbContext.Add(new ConversationMember { ConversationId = conversation.Id, UserId = UserOfA, Role = MemberRole.Member });
            await DbContext.SaveChangesAsync();
            DbContext.ChangeTracker.Clear();
            return conversation;
        }
    }

    [Fact]
    public async Task Notify_WithoutTenantContext_LandsInTheRecipientsOwnSystemConversation()
    {
        var existing = await SeedSystemConversationInTenantAAsync();

        var result = await Broadcast.NotifyUsersAsync([UserOfA], new ChatNotification { Content = "Your report is ready" });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data.ShouldBe(1);

        var conversations = await DbContext.Set<Conversation>().IgnoreQueryFilters()
            .Where(c => c.DirectKey == $"system:{UserOfA:N}").ToListAsync();
        conversations.ShouldHaveSingleItem().Id.ShouldBe(existing.Id);

        var message = await DbContext.Set<ChatMessage>().IgnoreQueryFilters().SingleAsync(m => m.ConversationId == existing.Id);
        message.TenantId.ShouldBe(TenantA);
        message.Content.ShouldBe("Your report is ready");
    }

    [Fact]
    public async Task Notify_WithoutTenantContext_CreatesTheConversationInTheRecipientsTenant()
    {
        var result = await Broadcast.NotifyUsersAsync([UserOfA], new ChatNotification { Content = "Welcome" });

        result.Data.ShouldBe(1);
        (await DbContext.Set<Conversation>().IgnoreQueryFilters().AnyAsync(c => c.TenantId == null)).ShouldBeFalse();
        (await DbContext.Set<ConversationMember>().IgnoreQueryFilters().SingleAsync(m => m.UserId == UserOfA)).TenantId.ShouldBe(TenantA);

        // 收件人在自己的租户里看得见它。
        using (CurrentTenant.Change(TenantA))
        {
            (await DbContext.Set<Conversation>().CountAsync(c => c.DirectKey == $"system:{UserOfA:N}")).ShouldBe(1);
        }
    }

    [Fact]
    public async Task Notify_ToAnUnknownUser_IsReportedAsNotDelivered_AndCreatesNothing()
    {
        var result = await Broadcast.NotifyUsersAsync([Guid.NewGuid()], new ChatNotification { Content = "Hello" });

        result.Data.ShouldBe(0);
        (await DbContext.Set<Conversation>().IgnoreQueryFilters().AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Notify_FromAnotherTenantsContext_DeliversNothing()
    {
        // 租户 B 的上下文（已认证的测试用户 + 租户 B）不得往租户 A 员工的系统通知里写消息。
        await SeedSystemConversationInTenantAAsync();

        Tnzi.Results.Result<int> result;
        using (CurrentTenant.Change(TenantB))
        {
            result = await Broadcast.NotifyUsersAsync([UserOfA], new ChatNotification { Content = "Hello from B" });
        }

        result.Data.ShouldBe(0);
        (await DbContext.Set<ChatMessage>().IgnoreQueryFilters().AnyAsync()).ShouldBeFalse();
    }
}
