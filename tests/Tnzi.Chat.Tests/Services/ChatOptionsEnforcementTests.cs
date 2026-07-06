using Tnzi.Chat.Options;
using Tnzi.Domain.Repositories;

namespace Tnzi.Chat.Tests.Services;

/// <summary>ChatConfigService 投影默认配置。</summary>
public class ChatConfigServiceTests : Integration.IntegrationTestBase
{
    private IChatConfigService Config => ServiceProvider.GetRequiredService<IChatConfigService>();

    [Fact]
    public void GetClientConfig_Should_Return_Defaults()
    {
        var r = Config.GetClientConfig();
        r.Succeeded.ShouldBeTrue(r.Message);
        r.Data!.EnableGroups.ShouldBeTrue();
        r.Data.MaxGroupMembers.ShouldBe(200);
        r.Data.GroupAvatarMemberCount.ShouldBe(9);
        r.Data.EnablePresence.ShouldBeTrue();
        r.Data.EnableMessageSound.ShouldBeTrue();
        r.Data.EnableFileMessages.ShouldBeTrue();
    }
}

/// <summary>EnableGroups=false 时群写操作被服务端拒绝。</summary>
public class ChatGroupsDisabledTests : Integration.IntegrationTestBase
{
    private IGroupService Group => ServiceProvider.GetRequiredService<IGroupService>();

    protected override void ConfigureChatOptions(ChatOptions options) => options.EnableGroups = false;

    [Fact]
    public async Task CreateGroup_Should_Fail_403_When_Groups_Disabled()
    {
        var r = await Group.CreateGroupAsync(new CreateGroupDto { Title = "G", MemberIds = new List<Guid> { Guid.NewGuid() } });
        r.Succeeded.ShouldBeFalse();
        r.Code.ShouldBe(403);
    }

    [Fact]
    public async Task AddMembers_Should_Fail_403_When_Groups_Disabled()
    {
        var r = await Group.AddMembersAsync(Guid.NewGuid(), new[] { Guid.NewGuid() });
        r.Succeeded.ShouldBeFalse();
        r.Code.ShouldBe(403);
    }
}

/// <summary>MaxGroupMembers 上限在建群与加人两条路径强制。</summary>
public class ChatGroupMemberCapTests : Integration.IntegrationTestBase
{
    private IGroupService Group => ServiceProvider.GetRequiredService<IGroupService>();

    protected override void ConfigureChatOptions(ChatOptions options) => options.MaxGroupMembers = 3;

    [Fact]
    public async Task CreateGroup_Exceeding_Cap_Should_Fail_400()
    {
        // owner + 3 members = 4 > cap 3
        var r = await Group.CreateGroupAsync(new CreateGroupDto
        {
            Title = "G",
            MemberIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() }
        });
        r.Succeeded.ShouldBeFalse();
        r.Code.ShouldBe(400);
    }

    [Fact]
    public async Task AddMembers_Exceeding_Cap_Should_Fail_400()
    {
        // owner + 2 members = 3 = cap; adding one more must fail
        var g = (await Group.CreateGroupAsync(new CreateGroupDto
        {
            Title = "G",
            MemberIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() }
        })).Data!;

        var r = await Group.AddMembersAsync(g.Id, new[] { Guid.NewGuid() });
        r.Succeeded.ShouldBeFalse();
        r.Code.ShouldBe(400);
    }

    [Fact]
    public async Task AddMembers_Already_Active_Should_Not_Count_Toward_Cap()
    {
        var existing = Guid.NewGuid();
        var g = (await Group.CreateGroupAsync(new CreateGroupDto
        {
            Title = "G",
            MemberIds = new List<Guid> { existing, Guid.NewGuid() }
        })).Data!;

        // Re-adding an active member adds nobody new — must NOT trip the cap.
        var r = await Group.AddMembersAsync(g.Id, new[] { existing });
        r.Succeeded.ShouldBeTrue(r.Message);
    }
}

/// <summary>EnableFileMessages=false 时媒体消息被服务端拒绝（文本不受影响）。</summary>
public class ChatFileMessagesDisabledTests : Integration.IntegrationTestBase
{
    private IConversationService Conversations => ServiceProvider.GetRequiredService<IConversationService>();

    protected override void ConfigureChatOptions(ChatOptions options) => options.EnableFileMessages = false;

    [Fact]
    public async Task Send_Image_Should_Fail_403_When_File_Messages_Disabled()
    {
        var conv = (await Conversations.GetOrCreateDirectAsync(Guid.NewGuid())).Data!;
        var r = await Conversations.SendMessageAsync(conv.Id, new SendMessageDto
        {
            ContentType = MessageContentType.Image,
            FileId = Guid.NewGuid().ToString(),
            FileName = "a.png",
            FileSize = 10
        });
        r.Succeeded.ShouldBeFalse();
        r.Code.ShouldBe(403);
    }

    [Fact]
    public async Task Send_Text_Should_Still_Work()
    {
        var conv = (await Conversations.GetOrCreateDirectAsync(Guid.NewGuid())).Data!;
        var r = await Conversations.SendMessageAsync(conv.Id, new SendMessageDto { Content = "hi" });
        r.Succeeded.ShouldBeTrue(r.Message);
    }
}

/// <summary>群聊会话列表返回成员头像拼合数据。</summary>
public class ConversationListMemberAvatarsTests : Integration.IntegrationTestBase
{
    private IGroupService Group => ServiceProvider.GetRequiredService<IGroupService>();
    private IConversationService Conversations => ServiceProvider.GetRequiredService<IConversationService>();

    [Fact]
    public async Task Group_Item_Should_Carry_MemberAvatars()
    {
        var m1 = Guid.NewGuid();
        var m2 = Guid.NewGuid();
        var g = (await Group.CreateGroupAsync(new CreateGroupDto { Title = "Team", MemberIds = new List<Guid> { m1, m2 } })).Data!;

        var list = (await Conversations.GetMyConversationsAsync()).Data!;
        var item = list.Single(i => i.Id == g.Id);

        item.MemberAvatars.ShouldNotBeNull();
        item.MemberAvatars!.Count.ShouldBe(3);
        item.MemberAvatars.Select(a => a.UserId).ShouldBe(
            new[] { CurrentUserId, m1, m2 }, ignoreOrder: true);
    }

    [Fact]
    public async Task Direct_Item_Should_Not_Carry_MemberAvatars()
    {
        var conv = (await Conversations.GetOrCreateDirectAsync(Guid.NewGuid())).Data!;
        var list = (await Conversations.GetMyConversationsAsync()).Data!;
        list.Single(i => i.Id == conv.Id).MemberAvatars.ShouldBeNull();
    }

    [Fact]
    public async Task Owner_Should_Always_Lead_MemberAvatars_And_Detail_Members()
    {
        var m1 = Guid.NewGuid();
        var m2 = Guid.NewGuid();
        var g = (await Group.CreateGroupAsync(new CreateGroupDto { Title = "Order", MemberIds = new List<Guid> { m1, m2 } })).Data!;

        // A member added in a LATER operation must sort after the creation batch.
        var late = Guid.NewGuid();
        var lateRow = new ConversationMember { ConversationId = g.Id, UserId = late, Role = MemberRole.Member };
        await ServiceProvider.GetRequiredService<IRepository<ConversationMember, Guid>>().InsertAsync(lateRow);
        lateRow = await DbContext.Set<ConversationMember>().FirstAsync(m => m.ConversationId == g.Id && m.UserId == late);
        lateRow.CreationTime = DateTime.UtcNow.AddHours(1);

        // Force the owner's membership row to look like the VERY LATEST join — the
        // display order must still put the owner first (owner rank beats join time).
        var ownerRow = await DbContext.Set<ConversationMember>()
            .FirstAsync(m => m.ConversationId == g.Id && m.UserId == CurrentUserId);
        ownerRow.CreationTime = DateTime.UtcNow.AddDays(1);
        await DbContext.SaveChangesAsync();

        var list = (await Conversations.GetMyConversationsAsync()).Data!;
        var item = list.Single(i => i.Id == g.Id);
        item.MemberAvatars![0].UserId.ShouldBe(CurrentUserId);
        item.MemberAvatars.Last().UserId.ShouldBe(late);

        var detail = (await Conversations.GetByIdAsync(g.Id)).Data!;
        detail.Members[0].UserId.ShouldBe(CurrentUserId);
        detail.Members.Last().UserId.ShouldBe(late);
        // The creation batch shares one timestamp — set equality only.
        detail.Members.Skip(1).Take(2).Select(m => m.UserId).ShouldBe(new[] { m1, m2 }, ignoreOrder: true);
    }
}

/// <summary>GroupAvatarMemberCount 限制拼合选取数量。</summary>
public class GroupAvatarMemberCountTests : Integration.IntegrationTestBase
{
    private IGroupService Group => ServiceProvider.GetRequiredService<IGroupService>();
    private IConversationService Conversations => ServiceProvider.GetRequiredService<IConversationService>();

    protected override void ConfigureChatOptions(ChatOptions options) => options.GroupAvatarMemberCount = 2;

    [Fact]
    public async Task MemberAvatars_Should_Be_Capped_By_Option()
    {
        var g = (await Group.CreateGroupAsync(new CreateGroupDto
        {
            Title = "Big",
            MemberIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() }
        })).Data!;

        var list = (await Conversations.GetMyConversationsAsync()).Data!;
        var item = list.Single(i => i.Id == g.Id);
        item.MemberAvatars!.Count.ShouldBe(2);
    }
}
