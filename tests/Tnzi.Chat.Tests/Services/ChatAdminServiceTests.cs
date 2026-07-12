using Shouldly;
using Tnzi.Data;

namespace Tnzi.Chat.Tests.Services;

public class ChatAdminServiceTests : Integration.IntegrationTestBase
{
    private IChatAdminService Admin => ServiceProvider.GetRequiredService<IChatAdminService>();

    private async Task<Conversation> SeedConversationAsync(
        ConversationType type, string? title, Guid? ownerId, DateTime? lastMessageAt, params Guid[] memberIds)
    {
        var conv = new Conversation
        {
            Id = Guid.NewGuid(),
            Type = type,
            Title = title,
            OwnerId = ownerId,
            MemberCount = memberIds.Length,
            LastMessageAt = lastMessageAt,
            DirectKey = type == ConversationType.Direct && memberIds.Length == 2
                ? $"{memberIds[0]:N}:{memberIds[1]:N}"
                : null
        };
        DbContext.Set<Conversation>().Add(conv);
        foreach (var uid in memberIds)
        {
            DbContext.Set<ConversationMember>().Add(new ConversationMember
            {
                Id = Guid.NewGuid(),
                ConversationId = conv.Id,
                UserId = uid,
                Role = ownerId == uid ? MemberRole.Owner : MemberRole.Member
            });
        }
        await DbContext.SaveChangesAsync();
        return conv;
    }

    private async Task<ChatMessage> SeedMessageAsync(Guid convId, Guid? senderId, string content, DateTime sentAt)
    {
        var msg = new ChatMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = convId,
            SenderId = senderId,
            ContentType = MessageContentType.Text,
            Content = content,
            SentAt = sentAt
        };
        DbContext.Set<ChatMessage>().Add(msg);
        await DbContext.SaveChangesAsync();
        return msg;
    }

    [Fact]
    public async Task GetStatistics_Should_Count_By_Type_And_Members()
    {
        var u1 = CurrentUserId;
        var u2 = Guid.NewGuid();
        var u3 = Guid.NewGuid();
        var u4 = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var g1 = await SeedConversationAsync(ConversationType.Group, "G1", u1, now, u1, u2, u3);
        await SeedConversationAsync(ConversationType.Direct, null, null, now, u1, u4);
        await SeedMessageAsync(g1.Id, u2, "hi", now);
        await SeedMessageAsync(g1.Id, u1, "yo", now);

        var r = await Admin.GetStatisticsAsync();
        r.Succeeded.ShouldBeTrue();
        r.Data!.TotalConversations.ShouldBe(2);
        r.Data.GroupConversations.ShouldBe(1);
        r.Data.DirectConversations.ShouldBe(1);
        r.Data.SystemConversations.ShouldBe(0);
        r.Data.TotalMessages.ShouldBe(2);
        r.Data.MessagesToday.ShouldBe(2);
        r.Data.ActiveMembers.ShouldBe(4); // u1, u2, u3, u4
        r.Data.OnlineUsers.ShouldBe(0);   // no SignalR in test
    }

    [Fact]
    public async Task GetConversations_Should_Filter_By_Type()
    {
        var u1 = CurrentUserId;
        var now = DateTime.UtcNow;
        await SeedConversationAsync(ConversationType.Group, "G1", u1, now, u1, Guid.NewGuid(), Guid.NewGuid());
        await SeedConversationAsync(ConversationType.Direct, null, null, now, u1, Guid.NewGuid());

        var r = await Admin.GetConversationsAsync(new AdminConversationQueryDto { Type = ConversationType.Group });
        r.Succeeded.ShouldBeTrue();
        r.Data!.TotalCount.ShouldBe(1);
        r.Data.Items[0].Type.ShouldBe(ConversationType.Group);
        r.Data.Items[0].MemberCount.ShouldBe(3);
        r.Data.Items[0].Title.ShouldBe("G1");
    }

    [Fact]
    public async Task GetConversations_Should_Filter_By_Participant()
    {
        var u1 = CurrentUserId;
        var target = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await SeedConversationAsync(ConversationType.Group, "G1", u1, now, u1, Guid.NewGuid());
        await SeedConversationAsync(ConversationType.Direct, null, null, now, u1, target);

        var r = await Admin.GetConversationsAsync(new AdminConversationQueryDto { UserId = target });
        r.Succeeded.ShouldBeTrue();
        r.Data!.TotalCount.ShouldBe(1);
        r.Data.Items[0].Type.ShouldBe(ConversationType.Direct);
    }

    [Fact]
    public async Task GetConversations_By_Unknown_Participant_Should_Be_Empty()
    {
        var u1 = CurrentUserId;
        await SeedConversationAsync(ConversationType.Group, "G1", u1, DateTime.UtcNow, u1);

        var r = await Admin.GetConversationsAsync(new AdminConversationQueryDto { UserId = Guid.NewGuid() });
        r.Succeeded.ShouldBeTrue();
        r.Data!.TotalCount.ShouldBe(0);
        r.Data.Items.Count.ShouldBe(0);
    }

    [Fact]
    public async Task GetConversationDetail_Should_Return_Members_And_MessageCount()
    {
        var u1 = CurrentUserId;
        var u2 = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var g1 = await SeedConversationAsync(ConversationType.Group, "G1", u1, now, u1, u2);
        await SeedMessageAsync(g1.Id, u1, "a", now);
        await SeedMessageAsync(g1.Id, u2, "b", now);

        var r = await Admin.GetConversationDetailAsync(g1.Id);
        r.Succeeded.ShouldBeTrue();
        r.Data!.MemberCount.ShouldBe(2);
        r.Data.MessageCount.ShouldBe(2);
        r.Data.Members.Count.ShouldBe(2);
        r.Data.OwnerId.ShouldBe(u1);
        r.Data.Members.ShouldContain(m => m.UserId == u1 && m.Role == MemberRole.Owner);
    }

    [Fact]
    public async Task GetConversationDetail_Unknown_Should_404()
    {
        var r = await Admin.GetConversationDetailAsync(Guid.NewGuid());
        r.Succeeded.ShouldBeFalse();
        r.Code.ShouldBe(404);
    }

    [Fact]
    public async Task GetConversationMessages_Admin_Should_Ignore_Membership()
    {
        // Current user is NOT a member of this conversation.
        var u5 = Guid.NewGuid();
        var u6 = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var d2 = await SeedConversationAsync(ConversationType.Direct, null, null, now, u5, u6);
        await SeedMessageAsync(d2.Id, u5, "private", now);

        var r = await Admin.GetConversationMessagesAsync(d2.Id, new MessageThreadQueryDto { Limit = 30 });
        r.Succeeded.ShouldBeTrue();
        r.Data!.Messages.Count.ShouldBe(1);
        r.Data.Messages[0].Content.ShouldBe("private");
    }

    [Fact]
    public async Task DeleteConversation_Should_SoftDelete_And_Drop_From_List()
    {
        var u1 = CurrentUserId;
        var conv = await SeedConversationAsync(ConversationType.Group, "G1", u1, DateTime.UtcNow, u1);

        var r = await Admin.DeleteConversationAsync(conv.Id);
        r.Succeeded.ShouldBeTrue();

        var deleted = await DbContext.Set<Conversation>().IgnoreQueryFilters().FirstAsync(c => c.Id == conv.Id);
        deleted.IsDeleted.ShouldBeTrue();

        var list = await Admin.GetConversationsAsync(new AdminConversationQueryDto());
        list.Data!.Items.ShouldNotContain(i => i.Id == conv.Id);
    }

    [Fact]
    public async Task DeleteMessage_Admin_Should_Recall_Any_Sender()
    {
        var u1 = CurrentUserId;
        var u2 = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var g1 = await SeedConversationAsync(ConversationType.Group, "G1", u1, now, u1, u2);
        var msg = await SeedMessageAsync(g1.Id, u2, "violation", now); // sent by another user

        var r = await Admin.DeleteMessageAsync(msg.Id);
        r.Succeeded.ShouldBeTrue();

        var deleted = await DbContext.Set<ChatMessage>().IgnoreQueryFilters().FirstAsync(m => m.Id == msg.Id);
        deleted.IsDeleted.ShouldBeTrue();
    }

    [Fact]
    public async Task GetPresenceOverview_Should_Aggregate_Effective_Status()
    {
        var now = DateTime.UtcNow;
        DbContext.Set<UserPresence>().AddRange(
            new UserPresence { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Status = UserPresenceStatus.Online, LastSeenAt = now, LastChangedAt = now },
            new UserPresence { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Status = UserPresenceStatus.Away, LastSeenAt = now, LastChangedAt = now },
            new UserPresence { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Status = UserPresenceStatus.Invisible, LastSeenAt = now, LastChangedAt = now });
        await DbContext.SaveChangesAsync();

        var r = await Admin.GetPresenceOverviewAsync(new PresenceOverviewQueryDto());
        r.Succeeded.ShouldBeTrue();
        r.Data!.Total.ShouldBe(3);
        // No connection manager → manual-only mode: intent shown as-is; Invisible → Offline.
        r.Data.Online.ShouldBe(1);
        r.Data.Away.ShouldBe(1);
        r.Data.Busy.ShouldBe(0);
        r.Data.Offline.ShouldBe(1); // the Invisible user
    }

    [Fact]
    public async Task GetPresenceOverview_OnlineOnly_Should_Exclude_Offline()
    {
        var now = DateTime.UtcNow;
        DbContext.Set<UserPresence>().AddRange(
            new UserPresence { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Status = UserPresenceStatus.Online, LastSeenAt = now, LastChangedAt = now },
            new UserPresence { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Status = UserPresenceStatus.Invisible, LastSeenAt = now, LastChangedAt = now });
        await DbContext.SaveChangesAsync();

        var r = await Admin.GetPresenceOverviewAsync(new PresenceOverviewQueryDto { OnlineOnly = true });
        r.Succeeded.ShouldBeTrue();
        r.Data!.Users.Count.ShouldBe(1);
        r.Data.Users[0].EffectiveStatus.ShouldBe(UserPresenceStatus.Online);
    }

    [Fact]
    public async Task Broadcast_Should_Record_Log_And_GetBroadcasts_Returns_It()
    {
        var broadcast = ServiceProvider.GetRequiredService<IBroadcastService>();
        // Multi-tenancy is off in the test base → All path enumerates the (mocked, empty)
        // user repo, delivers to 0 users, but still records the broadcast log.
        var r = await broadcast.BroadcastAsync(new BroadcastDto { Content = "Hello all", All = true });
        r.Succeeded.ShouldBeTrue();

        var logs = await Admin.GetBroadcastsAsync(new PagedQueryDto());
        logs.Succeeded.ShouldBeTrue();
        logs.Data!.TotalCount.ShouldBe(1);
        logs.Data.Items[0].Content.ShouldBe("Hello all");
        logs.Data.Items[0].TargetType.ShouldBe(BroadcastTargetType.All);
        logs.Data.Items[0].TargetSummary.ShouldBe("All users");
    }

    [Fact]
    public async Task GetBroadcasts_Empty_Should_Return_Empty_Page()
    {
        var logs = await Admin.GetBroadcastsAsync(new PagedQueryDto());
        logs.Succeeded.ShouldBeTrue();
        logs.Data!.TotalCount.ShouldBe(0);
        logs.Data.Items.Count.ShouldBe(0);
    }

    [Fact]
    public async Task GetBroadcasts_Should_Project_Source_For_Programmatic_Notify()
    {
        var broadcast = ServiceProvider.GetRequiredService<IBroadcastService>();
        await broadcast.NotifyUsersAsync(new[] { Guid.NewGuid() }, new ChatNotification
        {
            Content = "Programmatic notice",
            Source = "BillingModule"
        });

        var logs = await Admin.GetBroadcastsAsync(new PagedQueryDto());
        logs.Succeeded.ShouldBeTrue();
        logs.Data!.TotalCount.ShouldBe(1);
        logs.Data.Items[0].Source.ShouldBe("BillingModule");
        logs.Data.Items[0].TargetType.ShouldBe(BroadcastTargetType.Users);
    }
}
