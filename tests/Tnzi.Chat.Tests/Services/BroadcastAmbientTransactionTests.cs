using Microsoft.EntityFrameworkCore.Diagnostics;
using Tnzi.Chat.Events;
using Tnzi.Chat.Tests.Integration;
using Tnzi.Data;
using Tnzi.Results;
using Tnzi.EventBus;

namespace Tnzi.Chat.Tests.Services;

/// <summary>
/// 广播的逐收件人隔离在调用方带着环境事务时也必须成立。
/// </summary>
/// <remarks>
/// <para>
/// 被保护的缺陷：每个收件人包在本服务作用域的 <c>ExecuteInUnitOfWorkAsync</c> 里，而框架的嵌套语义是
/// 内层「提交」只 flush、内层<b>异常回滚会整体撤销环境事务</b>。宿主开着 <c>AspNetCore:EnableGlobalUnitOfWork</c>
/// （或消费方在自己的工作单元里调 <c>NotifyUsersAsync</c>）时，第二个收件人失败会把第一个人的会话、消息与
/// 挂起的推送一起撤销；异常被吞、循环继续，第三个人落在自动提交模式下，返回的送达数仍把第一个人算作成功。
/// </para>
/// <para>
/// 这里在<b>测试作用域自己的</b>工作单元里开启事务再广播，然后从库里核对。SQLite 内存库尊重事务，所以看得见。
/// 推送用一个与真实总线同口径的事务感知替身：处在活跃事务里就推迟到提交后，回滚则丢弃。
/// </para>
/// </remarks>
public class BroadcastAmbientTransactionTests : Integration.IntegrationTestBase
{
    // 字段初始化先于基类构造函数执行，ConfigureDbContextOptions / ConfigureServices 被调用时它们已经就位
    private readonly FailingMemberInterceptor _interceptor = new();
    private readonly TransactionAwareCapturingBus _bus = new();

    protected override void ConfigureDbContextOptions(DbContextOptionsBuilder options)
        => options.AddInterceptors(_interceptor);

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddScoped<IEventBus>(_ => _bus);
        services.AddScoped<IPostCommitActionQueue, PostCommitActionQueue>();
    }

    [Fact]
    public async Task A_failing_recipient_inside_the_callers_transaction_leaves_the_other_recipients_delivered()
    {
        var first = Guid.NewGuid();
        var failing = Guid.NewGuid();
        var last = Guid.NewGuid();
        _interceptor.FailingUserId = failing;

        Result<int> result;
        using (var scope = ServiceProvider.CreateScope())
        {
            // 模拟 EnableGlobalUnitOfWork：请求级环境事务已经开着，广播在它里面跑。
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            unitOfWork.EnableTransaction();
            result = await scope.ServiceProvider.GetRequiredService<IBroadcastService>()
                .BroadcastToUsersAsync([first, failing, last], "Maintenance at 21:00 UTC");
            await unitOfWork.CommitTransactionAsync();
        }

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data.ShouldBe(2);
        result.Message!.ShouldContain("could not be delivered");

        // 报告说送到了的两个人，必须真的在库里：被回滚却记作成功是一次报告成功的静默丢失。
        var members = await ReadMemberUserIdsAsync();
        members.ShouldBe([first, last], ignoreOrder: true);
        (await ReadAsync(db => db.Set<ChatMessage>().CountAsync())).ShouldBe(2);
        (await ReadAsync(db => db.Set<BroadcastLog>().CountAsync())).ShouldBe(1);

        // 推送随各自的提交发出，失败的那一个没有推送。
        _bus.Sent.SelectMany(e => e.RecipientUserIds).ShouldBe([first, last], ignoreOrder: true);
    }

    /// <summary>
    /// 每个收件人在自己的事务里提交，所以调用方之后回滚撤不掉已经送达的通知，推送也照样发出。
    /// </summary>
    /// <remarks>
    /// 推送必须在收件人自己的提交之后、在它自己的执行流里发：交回调用方的执行流去发，
    /// 事务感知的总线会把它挂到调用方的事务上，调用方一回滚，已经落库的通知就再也不会被推送。
    /// </remarks>
    [Fact]
    public async Task Delivered_notifications_and_their_pushes_survive_the_callers_rollback()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();

        using (var scope = ServiceProvider.CreateScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            unitOfWork.EnableTransaction();
            var result = await scope.ServiceProvider.GetRequiredService<IBroadcastService>()
                .BroadcastToUsersAsync([alice, bob], "Quarterly results are out");
            result.Data.ShouldBe(2);

            // 调用方随后因为别的原因失败
            await unitOfWork.RollbackTransactionAsync();
        }

        (await ReadMemberUserIdsAsync()).ShouldBe([alice, bob], ignoreOrder: true);
        _bus.Sent.SelectMany(e => e.RecipientUserIds).ShouldBe([alice, bob], ignoreOrder: true);
    }

    private Task<List<Guid>> ReadMemberUserIdsAsync()
        => ReadAsync(db => db.Set<ConversationMember>().Select(m => m.UserId).ToListAsync());

    private async Task<T> ReadAsync<T>(Func<ChatTestDbContext, Task<T>> query)
    {
        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatTestDbContext>();
        return await query(db);
    }

    /// <summary>保存一个指定用户的新成员时抛异常，模拟提交阶段撞上唯一约束。</summary>
    private sealed class FailingMemberInterceptor : SaveChangesInterceptor
    {
        public Guid? FailingUserId { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var failing = eventData.Context?.ChangeTracker.Entries<ConversationMember>()
                .Any(e => e.State == EntityState.Added && e.Entity.UserId == FailingUserId) == true;
            if (failing)
            {
                throw new DbUpdateException("simulated unique constraint violation at commit");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    /// <summary>
    /// 与真实总线同口径的事务感知替身：处在活跃工作单元事务里时推迟到提交后（回滚则随队列一起丢弃），否则立即记录。
    /// </summary>
    private sealed class TransactionAwareCapturingBus : IEventBus
    {
        private readonly object _sync = new();

        public List<ConversationMessageSentEvent> Sent { get; } = new();

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : class, IEvent
        {
            var ambient = AmbientUnitOfWork.Current;
            if (ambient?.IsTransactionActive == true)
            {
                ambient.EnqueuePostCommit(_ =>
                {
                    Capture(@event);
                    return Task.CompletedTask;
                });
                return Task.CompletedTask;
            }

            Capture(@event);
            return Task.CompletedTask;
        }

        private void Capture(object @event)
        {
            if (@event is not ConversationMessageSentEvent sent) return;
            lock (_sync) Sent.Add(sent);
        }

        public Task PublishDelayedAsync<TEvent>(TEvent @event, TimeSpan delay, CancellationToken cancellationToken = default)
            where TEvent : class, IEvent => Task.CompletedTask;

        public bool HasHandlers<TEvent>() where TEvent : class, IEvent => false;
        public int GetHandlerCount<TEvent>() where TEvent : class, IEvent => 0;

        public void Subscribe<TEvent, THandler>()
            where TEvent : class, IEvent
            where THandler : class, IEventHandler<TEvent> { }

        public void Unsubscribe<TEvent, THandler>()
            where TEvent : class, IEvent
            where THandler : class, IEventHandler<TEvent> { }

        public void UnsubscribeAll<TEvent>() where TEvent : class, IEvent { }
    }
}
