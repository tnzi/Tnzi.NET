using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 一条事件记不下来（数据库拒收那一行）：这一轮照常跑完，回复、终态都在，缺的那一条在它的序号上留一条说明。
/// </summary>
/// <remarks>
/// 此前落库异常从执行器一路冒到队列处理器的兜底：进程被终止，回复不写进线程，运行以
/// 「An error occurred while saving the entity changes」失败，几分钟的工作只因一条记录丢掉。
/// 这里用一个只拒收那一行的拦截器模拟数据库拒收（SQLite 本身什么都收）。
/// </remarks>
public class CliRunUnrecordedEventTests : CliChatTurnTestBase
{
    private const string Rejected = "the row the database refuses";

    protected override void ConfigureDbContextOptions(DbContextOptionsBuilder options)
        => options.AddInterceptors(new RejectingInterceptor());

    [Fact]
    public async Task AnEventTheDatabaseRefuses_LeavesANotice_AndTheTurnFinishesWithTheRestOfTheReply()
    {
        var (threadId, userMessageId) = await SeedThreadWithQuestionAsync("look at the binary");
        var run = await SeedClaimedRunAsync(threadId, userMessageId);
        var adapter = new ScriptedAdapter(
        [
            new CliAgentEvent { Type = CliAgentEventType.Text, Content = "Reading it." },
            new CliAgentEvent { Type = CliAgentEventType.Thinking, Content = "hmm" },
            new CliAgentEvent { Type = CliAgentEventType.Text, Content = Rejected },
            new CliAgentEvent { Type = CliAgentEventType.Thinking, Content = "hmm" },
            new CliAgentEvent { Type = CliAgentEventType.Text, Content = "Done." }
        ], output: "Done.");

        await CreateExecutor(adapter).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var persisted = await ReloadRunAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Completed);
        persisted.Error.ShouldBeNull();

        var reply = await DbContext.Set<AgentThreadMessage>().AsNoTracking()
            .SingleAsync(m => m.ThreadId == threadId && m.Role == MessageRole.Assistant);
        reply.Content.ShouldBe("Reading it.\n\nDone.");
        persisted.AssistantMessageId.ShouldBe(reply.Id);

        var rows = await DbContext.Set<CliRunMessage>().AsNoTracking().IgnoreQueryFilters()
            .Where(m => m.RunId == run.Id).OrderBy(m => m.Sequence).ToListAsync();
        rows.Select(m => m.Sequence).ShouldBe([1, 2, 3, 4, 5], "the missing event keeps its place in the sequence");
        var notice = rows[2];
        notice.Type.ShouldBe(CliAgentEventType.Log);
        notice.Level.ShouldBe(CliRunExecutor.UnrecordedEventLevel);
        notice.Content.ShouldBe("A Text event could not be recorded and is missing here.");
    }

    /// <summary>拒收内容是 <see cref="Rejected"/> 的事件行，像数据库拒收那一行时那样抛 <see cref="DbUpdateException"/>。</summary>
    private sealed class RejectingInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var rejected = eventData.Context!.ChangeTracker.Entries<CliRunMessage>()
                .Any(e => e.State == EntityState.Added && e.Entity.Content == Rejected);
            if (rejected)
            {
                throw new DbUpdateException("22021: invalid byte sequence for encoding \"UTF8\": 0x00");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
