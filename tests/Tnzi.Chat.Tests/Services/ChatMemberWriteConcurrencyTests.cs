using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Tnzi.Chat.Tests.Integration;

namespace Tnzi.Chat.Tests.Services;

/// <summary>
/// 成员行的并发写：一个请求读到的快照不能在它提交时把别的请求刚写下的列整行写回去。
/// </summary>
/// <remarks>
/// <para>
/// 发消息在读出其余成员之后、提交之前还要问一次「谁不能用聊天」（权限库查询），这正是并发窗口。
/// 这里把那次查询换成一个钩子，在窗口里用<b>另一个作用域</b>（另一个 DbContext，同一个 SQLite 连接）
/// 提交一笔并发写，模拟另一个请求。
/// </para>
/// <para>
/// 断言一律经新作用域、不跟踪地读库：被测服务与基类的 <c>DbContext</c> 是同一个实例，
/// 跟踪着的旧实体会把库里的真值挡住。
/// </para>
/// </remarks>
public class ChatMemberWriteConcurrencyTests : Integration.IntegrationTestBase
{
    private static readonly Guid PeerB = Guid.Parse("00000000-0000-0000-0000-0000000000b0");
    private static readonly Guid PeerC = Guid.Parse("00000000-0000-0000-0000-0000000000c0");

    private readonly CommandLog _commands = new();
    private Func<IServiceProvider, Task>? _duringDisabledCheck;

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // 只在发消息问「谁不能用聊天」的那一刻跑一次钩子，然后照常放行所有人。
        services.AddScoped<IChatAccessService>(sp => new HookedAccess(sp, () =>
        {
            var hook = _duringDisabledCheck;
            _duringDisabledCheck = null;
            return hook;
        }));
    }

    protected override void ConfigureDbContextOptions(DbContextOptionsBuilder options)
        => options.AddInterceptors(_commands);

    [Fact]
    public async Task SendMessage_DoesNotReinstateAMemberRemovedWhileItWasSending()
    {
        var groupId = await SeedGroupAsync(CurrentUserId, PeerB, PeerC);

        _duringDisabledCheck = async sp =>
        {
            // 另一个请求：群主把 B 移出群并提交。
            using var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ChatTestDbContext>();
            var b = await db.Set<ConversationMember>().FirstAsync(m => m.ConversationId == groupId && m.UserId == PeerB);
            b.RemovedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        };

        var sent = await ServiceProvider.GetRequiredService<IConversationService>()
            .SendMessageAsync(groupId, new SendMessageDto { Content = "hello" });
        sent.Succeeded.ShouldBeTrue(sent.Message);

        var b = await ReadMemberAsync(groupId, PeerB);
        b.RemovedAt.ShouldNotBeNull("the send must not write B's row back from its stale snapshot");
        b.UnreadCount.ShouldBe(0, "a member removed before the send commits is no longer a recipient");

        (await ReadMemberAsync(groupId, PeerC)).UnreadCount.ShouldBe(1);
    }

    [Fact]
    public async Task ConcurrentSends_EachIncrementTheUnreadCount()
    {
        var groupId = await SeedGroupAsync(CurrentUserId, PeerB, PeerC);

        _duringDisabledCheck = async sp =>
        {
            // 另一个请求在这个窗口里发出并提交了自己的消息。
            using var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
            var other = scope.ServiceProvider.GetRequiredService<IConversationService>();
            var r = await other.SendMessageAsync(groupId, new SendMessageDto { Content = "first" });
            r.Succeeded.ShouldBeTrue(r.Message);
        };

        var sent = await ServiceProvider.GetRequiredService<IConversationService>()
            .SendMessageAsync(groupId, new SendMessageDto { Content = "second" });
        sent.Succeeded.ShouldBeTrue(sent.Message);

        (await ReadMemberAsync(groupId, PeerB)).UnreadCount.ShouldBe(2);
        (await ReadMemberAsync(groupId, PeerC)).UnreadCount.ShouldBe(2);
    }

    [Fact]
    public async Task SendMessage_DoesNotRevertAConcurrentDeleteForMe()
    {
        var groupId = await SeedGroupAsync(CurrentUserId, PeerB);
        var clearedAt = DateTime.UtcNow.AddMinutes(-1);

        _duringDisabledCheck = async sp =>
        {
            // B 刚执行了「删除聊天」：清空水位与静音同时落库。
            using var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ChatTestDbContext>();
            var b = await db.Set<ConversationMember>().FirstAsync(m => m.ConversationId == groupId && m.UserId == PeerB);
            b.ClearedAt = clearedAt;
            b.IsMuted = true;
            await db.SaveChangesAsync();
        };

        (await ServiceProvider.GetRequiredService<IConversationService>()
            .SendMessageAsync(groupId, new SendMessageDto { Content = "hello" })).Succeeded.ShouldBeTrue();

        var b = await ReadMemberAsync(groupId, PeerB);
        b.ClearedAt.ShouldNotBeNull();
        b.IsMuted.ShouldBeTrue();
        b.UnreadCount.ShouldBe(1);
    }

    /// <summary>
    /// 成员自己的写（已读、设置、删除聊天、清空历史）只写它改的那几列：
    /// 整行写回会把并发的「移出群」（RemovedAt）撤销：被移出的人一次标已读就回到了群里。
    /// </summary>
    [Fact]
    public async Task MemberSelfWrites_NeverWriteTheRemovalColumn()
    {
        var groupId = await SeedGroupAsync(CurrentUserId, PeerB);
        var service = ServiceProvider.GetRequiredService<IConversationService>();

        // 让自己有一条未读，MarkRead 才会真的写。
        await using (var scope = ServiceProvider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChatTestDbContext>();
            var mine = await db.Set<ConversationMember>().FirstAsync(m => m.ConversationId == groupId && m.UserId == CurrentUserId);
            mine.UnreadCount = 3;
            await db.SaveChangesAsync();
        }

        _commands.Clear();
        (await service.MarkReadAsync(groupId)).Succeeded.ShouldBeTrue();
        (await service.UpdateMemberSettingsAsync(groupId, new ConversationMemberSettingsDto { IsMuted = true, Remark = "r" })).Succeeded.ShouldBeTrue();
        (await service.ClearHistoryAsync(groupId)).Succeeded.ShouldBeTrue();
        (await service.DeleteForMeAsync(groupId)).Succeeded.ShouldBeTrue();

        var memberUpdates = _commands.Updates("ConversationMember");
        memberUpdates.ShouldNotBeEmpty();
        memberUpdates.ShouldAllBe(sql => !sql.Contains("\"RemovedAt\""));
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private async Task<Guid> SeedGroupAsync(params Guid[] memberIds)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatTestDbContext>();
        var conv = new Conversation
        {
            Id = Guid.NewGuid(),
            Type = ConversationType.Group,
            Title = "g",
            OwnerId = memberIds[0],
            MemberCount = memberIds.Length
        };
        db.Set<Conversation>().Add(conv);
        foreach (var userId in memberIds)
        {
            db.Set<ConversationMember>().Add(new ConversationMember
            {
                Id = Guid.NewGuid(),
                ConversationId = conv.Id,
                UserId = userId,
                Role = userId == memberIds[0] ? MemberRole.Owner : MemberRole.Member
            });
        }

        await db.SaveChangesAsync();
        return conv.Id;
    }

    private async Task<ConversationMember> ReadMemberAsync(Guid conversationId, Guid userId)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatTestDbContext>();
        return await db.Set<ConversationMember>().AsNoTracking()
            .FirstAsync(m => m.ConversationId == conversationId && m.UserId == userId);
    }

    /// <summary>放行所有人；第一次被问时先跑一次钩子（模拟发生在这个窗口里的另一个请求）。</summary>
    private sealed class HookedAccess(IServiceProvider services, Func<Func<IServiceProvider, Task>?> takeHook) : IChatAccessService
    {
        public Task<bool> CanUseAsync(Guid userId) => Task.FromResult(true);

        public Task<bool> CanCurrentUserUseAsync() => Task.FromResult(true);

        public async Task<IReadOnlySet<Guid>> FilterDisabledAsync(IEnumerable<Guid> userIds)
        {
            var hook = takeHook();
            if (hook != null)
            {
                await hook(services);
            }

            return new HashSet<Guid>();
        }
    }

    /// <summary>记下发往数据库的 UPDATE 语句。</summary>
    private sealed class CommandLog : DbCommandInterceptor
    {
        private readonly List<string> _commands = [];

        public void Clear()
        {
            lock (_commands) _commands.Clear();
        }

        public List<string> Updates(string table)
        {
            lock (_commands)
            {
                return _commands
                    .Where(c => c.Contains("UPDATE", StringComparison.OrdinalIgnoreCase) && c.Contains($"{table}\""))
                    .ToList();
            }
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Record(DbCommand command)
        {
            lock (_commands) _commands.Add(command.CommandText);
        }
    }
}
