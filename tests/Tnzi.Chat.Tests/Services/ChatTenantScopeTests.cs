using System.Linq.Expressions;
using Microsoft.Extensions.Options;
using MockQueryable;
using Tnzi.Identity.Entities;
using Tnzi.Identity.Services;
using Tnzi.MultiTenancy;

namespace Tnzi.Chat.Tests.Services;

/// <summary>
/// 多租户开启时，聊天的联系人目录、资料卡与所有「调用方给一批用户 id」的写入口都只碰得到本租户的账号。
/// </summary>
/// <remarks>
/// <para>
/// ★ <c>User</c> 刻意不实现 <c>IMultiTenant</c>（登录链路要跨租户找人），没有全局过滤器替它把关。
/// 此前聊天模块一处补偿都没有：租户 A 的普通员工凭 <c>chat.use</c> 换着关键字搜就能把全部租户的用户名单拖出来，
/// 再按 id 打开资料卡读到别家租户员工的邮箱与电话；拿这些 id 建群 / 发起单聊 / 广播同样畅通。
/// </para>
/// <para>
/// 这里的范围来自一个钉在租户 A 的 <see cref="IUserTenantScopeProvider"/>（真实实现的判定由 Identity 的测试守着），
/// 守的是聊天这一侧<b>每个入口都去问了它</b>。越界一律与「不存在」相同：404 / 静默剔除，不告诉对方「这个 id 存在」。
/// </para>
/// </remarks>
public class ChatTenantScopeTests : Integration.IntegrationTestBase
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly Guid Colleague = Guid.NewGuid();
    private static readonly Guid Foreign = Guid.NewGuid();

    private static readonly List<User> Users =
    [
        new() { Id = CurrentUserId, UserName = "me", TenantId = TenantA },
        new() { Id = Colleague, UserName = "fred-colleague", Email = "fred@a.example", PhoneNumber = "111", TenantId = TenantA },
        new() { Id = Foreign, UserName = "frank-foreign", Email = "frank@b.example", PhoneNumber = "222", TenantId = TenantB },
    ];

    private IChatContactService Contacts => ServiceProvider.GetRequiredService<IChatContactService>();
    private IGroupService Groups => ServiceProvider.GetRequiredService<IGroupService>();
    private IConversationService Conversations => ServiceProvider.GetRequiredService<IConversationService>();
    private IBroadcastService Broadcast => ServiceProvider.GetRequiredService<IBroadcastService>();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        var userRepo = new Mock<IRepository<User, Guid>>();
        userRepo.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(() => Users.BuildMock());
        userRepo.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<User, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<User, bool>> p, CancellationToken _) => Users.Where(p.Compile()).ToList());
        userRepo.Setup(r => r.GetListAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<Guid> ids, CancellationToken _) => Users.Where(u => ids.Contains(u.Id)).ToList());
        userRepo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<User, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<User, bool>> p, CancellationToken _) => Users.FirstOrDefault(p.Compile()));
        userRepo.Setup(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => Users.Any(u => u.Id == id));
        services.AddScoped(_ => userRepo.Object);

        // 一个钉在租户 A 的范围：本人与同租户同事在内，别家租户的账号不在。
        var tenant = new Mock<ICurrentTenant>();
        tenant.SetupGet(t => t.Id).Returns(TenantA);
        var me = new Mock<ICurrentUser>();
        me.SetupGet(u => u.Id).Returns(CurrentUserId);
        me.SetupGet(u => u.IsAuthenticated).Returns(true);
        var pinned = UserTenantScope.Resolve(true, tenant.Object, me.Object);

        var scope = new Mock<IUserTenantScopeProvider>();
        scope.SetupGet(s => s.Current).Returns(pinned);
        scope.Setup(s => s.ContainsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => Users.Any(u => u.Id == id && pinned.Contains(u)));
        scope.Setup(s => s.FilterAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                (IReadOnlyCollection<Guid>)ids.Distinct().Where(id => Users.Any(u => u.Id == id && pinned.Contains(u))).ToList());
        services.AddScoped(_ => scope.Object);

        services.AddSingleton<IOptions<MultiTenancyOptions>>(Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions { Enabled = true }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("f")]
    public async Task ContactSearch_NeverReturnsAnotherTenantsUser(string? keyword)
    {
        var result = await Contacts.SearchUsersAsync(keyword);

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Select(c => c.UserId).ShouldContain(Colleague);
        result.Data!.Select(c => c.UserId).ShouldNotContain(Foreign);
    }

    [Fact]
    public async Task ContactProfile_OfAnotherTenantsUser_IsNotFound()
    {
        var result = await Contacts.GetProfileAsync(Foreign);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
        result.Data.ShouldBeNull();
    }

    /// <summary>对照：同租户同事的资料卡照常可读。</summary>
    [Fact]
    public async Task ContactProfile_OfAColleague_IsReadable()
    {
        var result = await Contacts.GetProfileAsync(Colleague);

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Email.ShouldBe("fred@a.example");
    }

    [Fact]
    public async Task ResolveProfiles_OmitsAnotherTenantsUser()
    {
        var profiles = await Contacts.ResolveProfilesAsync([Colleague, Foreign]);

        profiles.Keys.ShouldContain(Colleague);
        profiles.Keys.ShouldNotContain(Foreign);
    }

    [Fact]
    public async Task CreateGroup_DropsAnotherTenantsUser()
    {
        var result = await Groups.CreateGroupAsync(new CreateGroupDto { Title = "Team", MemberIds = [Colleague, Foreign] });

        result.Succeeded.ShouldBeTrue(result.Message);
        var members = await DbContext.Set<ConversationMember>().Where(m => m.ConversationId == result.Data!.Id).Select(m => m.UserId).ToListAsync();
        members.ShouldBe([CurrentUserId, Colleague], ignoreOrder: true);
    }

    [Fact]
    public async Task AddMembers_DropsAnotherTenantsUser()
    {
        var group = (await Groups.CreateGroupAsync(new CreateGroupDto { Title = "Team", MemberIds = [Colleague] })).Data!;

        var result = await Groups.AddMembersAsync(group.Id, [Foreign]);

        result.Succeeded.ShouldBeFalse();
        (await DbContext.Set<ConversationMember>().AnyAsync(m => m.ConversationId == group.Id && m.UserId == Foreign)).ShouldBeFalse();
    }

    [Fact]
    public async Task DirectChat_WithAnotherTenantsUser_IsNotFound()
    {
        var result = await Conversations.GetOrCreateDirectAsync(Foreign);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
        (await DbContext.Set<ConversationMember>().AnyAsync(m => m.UserId == Foreign)).ShouldBeFalse();
    }

    /// <summary>对照：与同租户同事的单聊照常建立。</summary>
    [Fact]
    public async Task DirectChat_WithAColleague_IsCreated()
    {
        var result = await Conversations.GetOrCreateDirectAsync(Colleague);

        result.Succeeded.ShouldBeTrue(result.Message);
    }

    [Fact]
    public async Task Broadcast_ToExplicitUsers_SkipsAnotherTenantsUser()
    {
        var result = await Broadcast.BroadcastAsync(new BroadcastDto { Content = "Heads up", UserIds = [Colleague, Foreign] });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data.ShouldBe(1);
        (await DbContext.Set<ConversationMember>().IgnoreQueryFilters().AnyAsync(m => m.UserId == Foreign)).ShouldBeFalse();
    }

    [Fact]
    public async Task Notify_FromAPinnedCaller_ToAnotherTenantsUser_DeliversNothing()
    {
        var result = await Broadcast.NotifyUsersAsync([Foreign], new ChatNotification { Content = "Hello" });

        result.Data.ShouldBe(0);
        (await DbContext.Set<ConversationMember>().IgnoreQueryFilters().AnyAsync(m => m.UserId == Foreign)).ShouldBeFalse();
    }
}
