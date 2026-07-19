using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tnzi.Chat.Entities;
using Tnzi.Chat.Services;
using Tnzi.Domain.Repositories;
using Tnzi.Security.Authorization;

namespace Tnzi.Chat.Tests.Services;

/// <summary>
/// 打开聊天访问门（chat.use 白名单）的测试基座：注册 <see cref="IFunctionAuthorizationService"/>
/// （在场信号）+ 一个仅对 <see cref="DeniedUserId"/> 拒绝 chat.use 的 <see cref="IPermissionChecker"/>，
/// 使 <c>ChatAccessService.GateActive</c> 为真、判定走 mock。
/// </summary>
public abstract class ChatAccessGatedTestBase : Integration.IntegrationTestBase
{
    protected abstract Guid DeniedUserId { get; }

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // Presence signals "Authorization loaded" to ChatAccessService. Stub the
        // super-admin lookup GroupService uses so member writes don't NRE on a bare mock.
        var funcAuth = new Mock<IFunctionAuthorizationService>();
        funcAuth.Setup(f => f.GetSuperAdminUserIdsAsync())
            .ReturnsAsync((IReadOnlySet<Guid>)new HashSet<Guid>());
        services.AddScoped(_ => funcAuth.Object);

        var checker = new Mock<IPermissionChecker>();
        checker.Setup(c => c.IsGrantedAsync(It.IsAny<Guid>(), ChatAccessService.UsePermission))
            .ReturnsAsync((Guid uid, string _) => uid != DeniedUserId);
        services.AddScoped(_ => checker.Object);
    }
}

/// <summary>直聊：发消息给无 chat.use 的用户被拦截（403）且不落库。</summary>
public class DirectChatInterceptionTests : ChatAccessGatedTestBase
{
    private static readonly Guid Disabled = Guid.NewGuid();
    protected override Guid DeniedUserId => Disabled;

    private IConversationService Conversations => ServiceProvider.GetRequiredService<IConversationService>();

    [Fact]
    public async Task Send_To_Disabled_User_Should_Fail_403_And_Not_Persist()
    {
        var conv = (await Conversations.GetOrCreateDirectAsync(Disabled)).Data!;
        var r = await Conversations.SendMessageAsync(conv.Id, new SendMessageDto { Content = "hi" });
        r.Succeeded.ShouldBeFalse();
        r.Code.ShouldBe(403);

        // Nothing persisted — my own view of the thread is empty.
        var thread = (await Conversations.GetMessagesAsync(conv.Id, new MessageThreadQueryDto())).Data!;
        thread.Messages.Count.ShouldBe(0);
    }
}

/// <summary>群聊：被禁成员写隔离行且不递增其未读；正常成员照常投递。</summary>
public class GroupIsolationTests : ChatAccessGatedTestBase
{
    private static readonly Guid Disabled = Guid.NewGuid();
    private static readonly Guid Normal = Guid.NewGuid();
    protected override Guid DeniedUserId => Disabled;

    private IGroupService Group => ServiceProvider.GetRequiredService<IGroupService>();
    private IConversationService Conversations => ServiceProvider.GetRequiredService<IConversationService>();
    private IRepository<MessageBlock, Guid> Blocks => ServiceProvider.GetRequiredService<IRepository<MessageBlock, Guid>>();
    private IRepository<ConversationMember, Guid> Members => ServiceProvider.GetRequiredService<IRepository<ConversationMember, Guid>>();

    private IRepository<Conversation, Guid> Convs => ServiceProvider.GetRequiredService<IRepository<Conversation, Guid>>();

    [Fact]
    public async Task Group_Message_Should_Isolate_Disabled_Member_Only()
    {
        // Create the group with only the still-enabled member, then seed the disabled
        // user directly as a member row. This models "joined while enabled, later lost
        // chat.use" — the only way a disabled user is in a group now that
        // CreateGroup/AddMembers drop currently-disabled ids (see GroupChatUseExclusionTests).
        var g = (await Group.CreateGroupAsync(new CreateGroupDto { Title = "T", MemberIds = new List<Guid> { Normal } })).Data!;
        await Members.InsertAsync(new ConversationMember { ConversationId = g.Id, UserId = Disabled, Role = MemberRole.Member });
        var conv = await Convs.AsQueryable(withTracking: true).FirstAsync(c => c.Id == g.Id);
        conv.MemberCount += 1;
        await Convs.UpdateAsync(conv);
        await DbContext.SaveChangesAsync();

        var send = await Conversations.SendMessageAsync(g.Id, new SendMessageDto { Content = "hello" });
        send.Succeeded.ShouldBeTrue(send.Message);
        var msgId = send.Data!.Id;

        // Isolation row for the disabled member, none for the normal member.
        var blocks = await Blocks.ToListAsync(b => b.MessageId == msgId);
        blocks.Select(b => b.UserId).ShouldBe(new[] { Disabled });

        // Unread bumped for the normal member, skipped for the disabled one.
        var disabledMember = await Members.FirstOrDefaultAsync(m => m.ConversationId == g.Id && m.UserId == Disabled);
        var normalMember = await Members.FirstOrDefaultAsync(m => m.ConversationId == g.Id && m.UserId == Normal);
        disabledMember!.UnreadCount.ShouldBe(0);
        normalMember!.UnreadCount.ShouldBe(1);
    }
}

/// <summary>GetMessages 永久排除隔离消息——解禁后不回填（隔离行是唯一真值源）。</summary>
public class MessageBlockExclusionTests : Integration.IntegrationTestBase
{
    private IGroupService Group => ServiceProvider.GetRequiredService<IGroupService>();
    private IConversationService Conversations => ServiceProvider.GetRequiredService<IConversationService>();

    [Fact]
    public async Task GetMessages_Should_Exclude_Blocked_Messages_For_Me()
    {
        // Fail-open base (no gate): messages land normally. Then isolate one message for
        // me (as if received while I was disabled) and confirm it stays hidden even though
        // I can use chat now (re-enabled) — the block row, not the current chat.use, decides.
        var g = (await Group.CreateGroupAsync(new CreateGroupDto { Title = "H", MemberIds = new List<Guid> { Guid.NewGuid() } })).Data!;
        var m1 = (await Conversations.SendMessageAsync(g.Id, new SendMessageDto { Content = "one" })).Data!;
        var m2 = (await Conversations.SendMessageAsync(g.Id, new SendMessageDto { Content = "two" })).Data!;

        // Isolate m1 for me — as if it arrived while I was disabled.
        var blocks = ServiceProvider.GetRequiredService<IRepository<MessageBlock, Guid>>();
        await blocks.InsertAsync(new MessageBlock { MessageId = m1.Id, UserId = CurrentUserId });
        await DbContext.SaveChangesAsync();

        // m1 stays hidden even though I can use chat now; m2 (and the group-created
        // system notice) remain visible. The block row, not the current chat.use, decides.
        var thread = (await Conversations.GetMessagesAsync(g.Id, new MessageThreadQueryDto())).Data!;
        var ids = thread.Messages.Select(x => x.Id).ToList();
        ids.ShouldNotContain(m1.Id);
        ids.ShouldContain(m2.Id);
    }
}

/// <summary>被禁用户的客户端配置 Enabled=false（前端据此隐藏聊天入口）。</summary>
public class ChatDisabledConfigTests : ChatAccessGatedTestBase
{
    protected override Guid DeniedUserId => CurrentUserId; // deny the caller itself
    private IChatConfigService Config => ServiceProvider.GetRequiredService<IChatConfigService>();

    [Fact]
    public async Task GetClientConfig_Should_Report_Disabled()
    {
        var r = await Config.GetClientConfigAsync();
        r.Succeeded.ShouldBeTrue(r.Message);
        r.Data!.Enabled.ShouldBeFalse();
    }
}

/// <summary>建群/加成员静默剔除当前无 chat.use 的用户（堵绕过选择器直传 id 的后门）。</summary>
public class GroupChatUseExclusionTests : ChatAccessGatedTestBase
{
    private static readonly Guid Disabled = Guid.NewGuid();
    private static readonly Guid Normal = Guid.NewGuid();
    protected override Guid DeniedUserId => Disabled;

    private IGroupService Group => ServiceProvider.GetRequiredService<IGroupService>();
    private IRepository<ConversationMember, Guid> Members => ServiceProvider.GetRequiredService<IRepository<ConversationMember, Guid>>();

    [Fact]
    public async Task CreateGroup_Should_Drop_Disabled_Member()
    {
        var g = (await Group.CreateGroupAsync(new CreateGroupDto { Title = "T", MemberIds = new List<Guid> { Disabled, Normal } })).Data!;
        var ids = (await Members.ToListAsync(m => m.ConversationId == g.Id && m.RemovedAt == null)).Select(m => m.UserId).ToList();
        ids.ShouldContain(Normal);
        ids.ShouldNotContain(Disabled);
    }

    [Fact]
    public async Task AddMembers_Should_Drop_Disabled_Member()
    {
        var g = (await Group.CreateGroupAsync(new CreateGroupDto { Title = "T", MemberIds = new List<Guid> { Normal } })).Data!;
        var other = Guid.NewGuid();
        var r = await Group.AddMembersAsync(g.Id, new List<Guid> { Disabled, other });
        r.Succeeded.ShouldBeTrue(r.Message); // `other` is addable, so the call succeeds

        var ids = (await Members.ToListAsync(m => m.ConversationId == g.Id && m.RemovedAt == null)).Select(m => m.UserId).ToList();
        ids.ShouldContain(other);
        ids.ShouldNotContain(Disabled);
    }
}

/// <summary>会话列表：Direct peer 失去 chat.use → PeerDisabled=true（前端换特殊「不可用」标识）。</summary>
public class PeerDisabledListTests : ChatAccessGatedTestBase
{
    private static readonly Guid DisabledPeer = Guid.NewGuid();
    private static readonly Guid NormalPeer = Guid.NewGuid();
    protected override Guid DeniedUserId => DisabledPeer;

    private IConversationService Conversations => ServiceProvider.GetRequiredService<IConversationService>();

    [Fact]
    public async Task GetMyConversations_Should_Flag_Disabled_Direct_Peer()
    {
        var withDisabled = (await Conversations.GetOrCreateDirectAsync(DisabledPeer)).Data!;
        var withNormal = (await Conversations.GetOrCreateDirectAsync(NormalPeer)).Data!;

        var list = (await Conversations.GetMyConversationsAsync()).Data!;
        var disabledItem = list.First(i => i.Id == withDisabled.Id);
        var normalItem = list.First(i => i.Id == withNormal.Id);

        // The peer who lost chat.use is flagged; a normal peer is not.
        disabledItem.PeerDisabled.ShouldBeTrue();
        normalItem.PeerDisabled.ShouldBeFalse();
    }
}
