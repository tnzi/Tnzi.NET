using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Tnzi.Chat.Tests.Integration;

namespace Tnzi.Chat.Tests.Services;

/// <summary>
/// 广播复用已有的系统会话时，只写「最后一条消息」那两列，不把读出来的快照整行写回。
/// </summary>
/// <remarks>
/// <para>
/// 整行写回的后果：读出会话与写回之间管理员删掉了这个会话（<c>IsDeleted = true</c>），写回把它改回 false，
/// 一次删除被一条系统通知悄悄撤销。这里在读出之后、写会话之前插入那次删除（同一事务里，紧挨着会话的条件写），
/// 然后核对删除仍然有效。
/// </para>
/// <para>
/// 删除仍然有效还不够：这条通知不能写进那个已删除的会话、还被计作送达（收件人永远看不见它）。
/// 会话的条件写一行都没写到时，按「没有会话」新开一个，通知落在新会话里。
/// </para>
/// </remarks>
public class BroadcastConversationColumnWriteTests : Integration.IntegrationTestBase
{
    // 字段初始化先于基类构造函数执行，ConfigureDbContextOptions 被调用时它已经就位
    private readonly SoftDeleteDuringDeliveryInterceptor _interceptor = new();

    protected override void ConfigureDbContextOptions(DbContextOptionsBuilder options)
        => options.AddInterceptors(_interceptor);

    private IBroadcastService Broadcasts => ServiceProvider.GetRequiredService<IBroadcastService>();

    [Fact]
    public async Task A_conversation_deleted_while_a_broadcast_reuses_it_stays_deleted()
    {
        var user = Guid.NewGuid();
        (await Broadcasts.BroadcastToUsersAsync([user], "First notice")).Data.ShouldBe(1);
        var key = $"system:{user:N}";

        var original = await ReadConversationAsync(key);

        _interceptor.DeleteDirectKey = key;
        var second = await Broadcasts.BroadcastToUsersAsync([user], "Second notice");
        _interceptor.DeleteDirectKey.ShouldBeNull("the interleaved delete must actually have run");

        (await ReadConversationAsync(original.Id)).IsDeleted
            .ShouldBeTrue("an admin delete that landed between the read and the write must not be undone");

        // 通知落在一个收件人看得见的会话里，而不是那个已删除的会话：送达数说的是真话。
        second.Data.ShouldBe(1);
        var live = await ReadLiveConversationAsync(key);
        live.ShouldNotBeNull("the notice must land in a conversation the recipient can see");
        live.Id.ShouldNotBe(original.Id);
        live.LastMessagePreview.ShouldBe("Second notice");
        (await ReadMessagesAsync(live.Id)).ShouldBe(["Second notice"]);
        (await ReadMessagesAsync(original.Id)).ShouldBe(["First notice"]);
        (await ReadUnreadCountAsync(live.Id, user)).ShouldBe(1);
    }

    /// <summary>对照：没有并发删除时，复用路径照常刷新最后一条消息，别的列不动。</summary>
    [Fact]
    public async Task Reusing_the_conversation_refreshes_the_last_message_columns()
    {
        var user = Guid.NewGuid();
        await Broadcasts.BroadcastToUsersAsync([user], "First notice");
        var key = $"system:{user:N}";
        var before = await ReadConversationAsync(key);

        await Broadcasts.BroadcastToUsersAsync([user], "Second notice");

        var after = await ReadConversationAsync(key);
        after.Id.ShouldBe(before.Id);
        after.LastMessagePreview.ShouldBe("Second notice");
        after.LastMessageAt!.Value.ShouldBeGreaterThanOrEqualTo(before.LastMessageAt!.Value);
        after.Title.ShouldBe(before.Title);
        after.IsDeleted.ShouldBeFalse();
    }

    private async Task<Conversation> ReadConversationAsync(string key)
    {
        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatTestDbContext>();
        return await db.Set<Conversation>().IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.DirectKey == key);
    }

    private async Task<Conversation> ReadConversationAsync(Guid id)
    {
        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatTestDbContext>();
        return await db.Set<Conversation>().IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == id);
    }

    /// <summary>收件人看得见的（未删除的）那个系统会话；没有为 null。</summary>
    private async Task<Conversation?> ReadLiveConversationAsync(string key)
    {
        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatTestDbContext>();
        return await db.Set<Conversation>().AsNoTracking().SingleOrDefaultAsync(c => c.DirectKey == key);
    }

    private async Task<List<string>> ReadMessagesAsync(Guid conversationId)
    {
        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatTestDbContext>();
        return await db.Set<ChatMessage>().IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .Select(m => m.Content!)
            .ToListAsync();
    }

    private async Task<int> ReadUnreadCountAsync(Guid conversationId, Guid userId)
    {
        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatTestDbContext>();
        return await db.Set<ConversationMember>().AsNoTracking()
            .Where(m => m.ConversationId == conversationId && m.UserId == userId)
            .Select(m => m.UnreadCount)
            .SingleAsync();
    }

    /// <summary>
    /// 在复用路径刷新会话最后一条消息的那条条件写执行之前，在同一连接、同一事务里把会话软删掉。
    /// 那一刻会话已经读出，会话本身还没写：正是并发删除落进读写窗口的形状。
    /// </summary>
    private sealed class SoftDeleteDuringDeliveryInterceptor : DbCommandInterceptor
    {
        public string? DeleteDirectKey { get; set; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (DeleteDirectKey is { } key
                && command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("\"LastMessagePreview\"", StringComparison.Ordinal))
            {
                DeleteDirectKey = null;
                var table = eventData.Context!.Model.FindEntityType(typeof(Conversation))!.GetTableName();

                await using var delete = command.Connection!.CreateCommand();
                delete.Transaction = command.Transaction;
                delete.CommandText = $"UPDATE \"{table}\" SET \"IsDeleted\" = 1 WHERE \"DirectKey\" = $key";
                var parameter = delete.CreateParameter();
                parameter.ParameterName = "$key";
                parameter.Value = key;
                delete.Parameters.Add(parameter);
                (await delete.ExecuteNonQueryAsync(cancellationToken)).ShouldBe(1);
            }

            return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
