using Tnzi.EFCore.Extensions;

namespace Tnzi.EFCore.Tests;

/// <summary>
/// 作用域 <see cref="IUnitOfWork"/> 与工作单元管理器在同一个上下文上<b>都</b>启用事务：只能有一个物理事务。
/// </summary>
/// <remarks>
/// <para>
/// 两者是同一个上下文上的两个工作单元实例。此前作用域实例一旦自己启用了事务，就不再把保存转交管理器，
/// 首次保存在自己那里再 BEGIN 一次；管理器已经为这个上下文开着物理事务时，EF 抛
/// 「The connection is already in a transaction」。先启用作用域实例、后由管理器开事务的顺序同样冲突。
/// </para>
/// <para>
/// 规则是「先进入事务的那个持有物理事务」：管理器先开时，作用域实例的层是管理器的嵌套层
/// （提交只 flush，回滚撤销整个环境事务，即 <c>docs/coding-standards/service.md</c> 记载的嵌套语义）；
/// 作用域实例先开时，管理器的工作单元加入它的物理事务，管理器的提交后动作推迟到它真正提交之后。
/// </para>
/// </remarks>
public sealed class ScopedUnitOfWorkTransactionOwnerTests : IDisposable
{
    private readonly RepositoryWriteHost _host = new();

    public void Dispose() => _host.Dispose();

    private static IUnitOfWork Scoped(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

    private static WriteHostProbe Probe(string code, bool withEvent = false)
    {
        var probe = new WriteHostProbe { Code = code };
        if (withEvent)
        {
            probe.AddDomainEvent(new ScopedUnitOfWorkAmbientTransactionTests.ProbeSavedEvent());
        }

        return probe;
    }

    // ---------- 管理器先进入事务 ----------

    [Fact]
    public async Task ManagerFirst_ScopedEnableWriteCommit_JoinsTheManagersTransaction()
    {
        var outer = Probe("outer");
        var inner = Probe("inner");

        using (var scope = _host.CreateScope())
        {
            var manager = RepositoryWriteHost.Manager(scope);
            manager.EnableTransaction();

            // 管理器的物理事务已经开着（flush 让它 BEGIN）
            await RepositoryWriteHost.Repository<WriteHostProbe>(scope).InsertAsync(outer);
            await manager.SaveChangesAsync();
            Assert.NotNull(RepositoryWriteHost.Context(scope).Database.CurrentTransaction);

            // 被调服务经作用域 IUnitOfWork 开自己的事务
            var unitOfWork = Scoped(scope);
            unitOfWork.EnableTransaction();
            RepositoryWriteHost.Context(scope).Probes.Add(inner);
            await unitOfWork.SaveChangesAsync();
            await unitOfWork.CommitTransactionAsync();

            // 内层提交没有物理提交：外层仍在事务里
            Assert.True(manager.IsEnabledTransaction);
            Assert.NotNull(RepositoryWriteHost.Context(scope).Database.CurrentTransaction);

            await manager.CommitTransactionAsync();
        }

        Assert.NotNull(await _host.ReadAsync<WriteHostProbe>(outer.Id));
        Assert.NotNull(await _host.ReadAsync<WriteHostProbe>(inner.Id));
    }

    /// <summary>经 <c>ExecuteInTransactionAsync</c>（作用域实例的便捷方法）同样成立；写入随外层回滚一起撤销。</summary>
    [Fact]
    public async Task ManagerFirst_ScopedExecuteInTransaction_IsUndoneByTheOuterRollback()
    {
        var outer = Probe("outer");
        var inner = Probe("inner");

        using (var scope = _host.CreateScope())
        {
            var manager = RepositoryWriteHost.Manager(scope);
            manager.EnableTransaction();
            await RepositoryWriteHost.Repository<WriteHostProbe>(scope).InsertAsync(outer);
            await manager.SaveChangesAsync();

            await Scoped(scope).ExecuteInTransactionAsync(async () =>
            {
                RepositoryWriteHost.Context(scope).Probes.Add(inner);
                await Scoped(scope).SaveChangesAsync();
            });

            await manager.RollbackTransactionAsync();
        }

        Assert.Null(await _host.ReadAsync<WriteHostProbe>(outer.Id));
        Assert.Null(await _host.ReadAsync<WriteHostProbe>(inner.Id));
    }

    /// <summary>
    /// 内层（作用域实例）回滚撤销整个环境事务：外层到目前为止的写入一并撤销，外层随后的提交什么都提交不了、也不抛。
    /// </summary>
    [Fact]
    public async Task ManagerFirst_ScopedRollback_UndoesTheWholeAmbientTransaction()
    {
        var outer = Probe("outer");
        var inner = Probe("inner");

        using (var scope = _host.CreateScope())
        {
            var manager = RepositoryWriteHost.Manager(scope);
            manager.EnableTransaction();
            await RepositoryWriteHost.Repository<WriteHostProbe>(scope).InsertAsync(outer);
            await manager.SaveChangesAsync();

            var unitOfWork = Scoped(scope);
            unitOfWork.EnableTransaction();
            RepositoryWriteHost.Context(scope).Probes.Add(inner);
            await unitOfWork.SaveChangesAsync();
            await unitOfWork.RollbackTransactionAsync();

            Assert.False(manager.IsEnabledTransaction);
            Assert.False(unitOfWork.IsEnabledTransaction);
            await manager.CommitTransactionAsync();
        }

        Assert.Null(await _host.ReadAsync<WriteHostProbe>(outer.Id));
        Assert.Null(await _host.ReadAsync<WriteHostProbe>(inner.Id));
    }

    /// <summary>
    /// 作用域实例的层在提交时 flush 失败：与它自己的提交同一语义（提交失败即回滚），撤销整个环境事务。
    /// 这一层在转交时已经取走，调用方随后的回滚到不了管理器；不在这里回滚，外层就带着失败的写入继续走。
    /// </summary>
    [Fact]
    public async Task ManagerFirst_ScopedCommitFlushFails_RollsBackTheWholeAmbientTransaction()
    {
        await _host.SeedAsync(Probe("taken"));
        var outer = Probe("outer");

        using (var scope = _host.CreateScope())
        {
            var manager = RepositoryWriteHost.Manager(scope);
            manager.EnableTransaction();
            await RepositoryWriteHost.Repository<WriteHostProbe>(scope).InsertAsync(outer);
            await manager.SaveChangesAsync();

            var unitOfWork = Scoped(scope);
            unitOfWork.EnableTransaction();
            // 撞唯一索引：flush 在这一层的提交里才发生
            RepositoryWriteHost.Context(scope).Probes.Add(Probe("taken"));

            await Assert.ThrowsAnyAsync<DbUpdateException>(() => unitOfWork.CommitTransactionAsync());

            Assert.False(manager.IsEnabledTransaction);
            Assert.False(unitOfWork.IsEnabledTransaction);
            Assert.Null(RepositoryWriteHost.Context(scope).Database.CurrentTransaction);

            // 调用方照常回滚：无事可做，不抛
            await unitOfWork.RollbackTransactionAsync();
            await manager.RollbackTransactionAsync();
        }

        Assert.Null(await _host.ReadAsync<WriteHostProbe>(outer.Id));
    }

    /// <summary>作用域实例的层是管理器的嵌套层：在其中保存的领域事件跟着管理器的提交走，提交前不发布。</summary>
    [Fact]
    public async Task ManagerFirst_EventsSavedInTheScopedLevel_ArePublishedAfterTheOuterCommit()
    {
        var inner = Probe("inner", withEvent: true);

        using (var scope = _host.CreateScope())
        {
            var manager = RepositoryWriteHost.Manager(scope);
            manager.EnableTransaction();

            var unitOfWork = Scoped(scope);
            unitOfWork.EnableTransaction();
            RepositoryWriteHost.Context(scope).Probes.Add(inner);
            await unitOfWork.SaveChangesAsync();
            await unitOfWork.CommitTransactionAsync();
            Assert.Empty(_host.PublishedEvents);

            await manager.CommitTransactionAsync();
        }

        Assert.Single(_host.PublishedEvents.OfType<ScopedUnitOfWorkAmbientTransactionTests.ProbeSavedEvent>());
        Assert.NotNull(await _host.ReadAsync<WriteHostProbe>(inner.Id));
    }

    // ---------- 作用域实例先进入事务 ----------

    [Fact]
    public async Task ScopedFirst_ManagerTransactionInside_JoinsTheScopedTransaction()
    {
        var outer = Probe("outer");
        var inner = Probe("inner");

        using (var scope = _host.CreateScope())
        {
            var unitOfWork = Scoped(scope);
            unitOfWork.EnableTransaction();
            RepositoryWriteHost.Context(scope).Probes.Add(outer);
            await unitOfWork.SaveChangesAsync();
            Assert.NotNull(RepositoryWriteHost.Context(scope).Database.CurrentTransaction);

            // 被调服务经管理器开事务（ExecuteInUnitOfWorkAsync 的形状）
            var manager = RepositoryWriteHost.Manager(scope);
            manager.EnableTransaction();
            await RepositoryWriteHost.Repository<WriteHostProbe>(scope).InsertAsync(inner);
            await manager.SaveChangesAsync();
            await manager.CommitTransactionAsync();

            // 管理器的提交只 flush 进了外层事务
            Assert.NotNull(RepositoryWriteHost.Context(scope).Database.CurrentTransaction);
            await unitOfWork.CommitTransactionAsync();
        }

        Assert.NotNull(await _host.ReadAsync<WriteHostProbe>(outer.Id));
        Assert.NotNull(await _host.ReadAsync<WriteHostProbe>(inner.Id));
    }

    /// <summary>作用域实例先启用、还没保存：管理器的工作单元请它开启物理事务，而不是自己开一个先提交掉。</summary>
    [Fact]
    public async Task ScopedFirst_BeforeAnySave_TheOuterRollbackStillUndoesTheManagersWrites()
    {
        var inner = Probe("inner");

        using (var scope = _host.CreateScope())
        {
            var unitOfWork = Scoped(scope);
            unitOfWork.EnableTransaction();

            var manager = RepositoryWriteHost.Manager(scope);
            manager.EnableTransaction();
            await RepositoryWriteHost.Repository<WriteHostProbe>(scope).InsertAsync(inner);
            await manager.CommitTransactionAsync();

            await unitOfWork.RollbackTransactionAsync();
        }

        Assert.Null(await _host.ReadAsync<WriteHostProbe>(inner.Id));
    }

    /// <summary>内层（管理器）回滚撤销整个事务，包括外层作用域实例此前的写入；外层随后回滚不抛。</summary>
    [Fact]
    public async Task ScopedFirst_ManagerRollback_UndoesTheWholeTransaction()
    {
        var outer = Probe("outer");
        var inner = Probe("inner");

        using (var scope = _host.CreateScope())
        {
            var unitOfWork = Scoped(scope);
            unitOfWork.EnableTransaction();
            RepositoryWriteHost.Context(scope).Probes.Add(outer);
            await unitOfWork.SaveChangesAsync();

            var manager = RepositoryWriteHost.Manager(scope);
            manager.EnableTransaction();
            await RepositoryWriteHost.Repository<WriteHostProbe>(scope).InsertAsync(inner);
            await manager.SaveChangesAsync();
            await manager.RollbackTransactionAsync();

            Assert.Null(RepositoryWriteHost.Context(scope).Database.CurrentTransaction);
            await unitOfWork.RollbackTransactionAsync();
        }

        Assert.Null(await _host.ReadAsync<WriteHostProbe>(outer.Id));
        Assert.Null(await _host.ReadAsync<WriteHostProbe>(inner.Id));
    }

    /// <summary>
    /// 管理器的提交后动作推迟到外层真正提交之后：外层提交则恰好发布一次，外层回滚则一次都不发布。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ScopedFirst_ManagerPostCommitActions_FollowTheOuterTransaction(bool commitOuter)
    {
        var inner = Probe(commitOuter ? "commit" : "rollback", withEvent: true);

        using (var scope = _host.CreateScope())
        {
            var unitOfWork = Scoped(scope);
            unitOfWork.EnableTransaction();
            await unitOfWork.EnsureTransactionStartedAsync();

            var manager = RepositoryWriteHost.Manager(scope);
            manager.EnableTransaction();
            await RepositoryWriteHost.Repository<WriteHostProbe>(scope).InsertAsync(inner);
            await manager.CommitTransactionAsync();
            Assert.Empty(_host.PublishedEvents);

            if (commitOuter)
            {
                await unitOfWork.CommitTransactionAsync();
            }
            else
            {
                await unitOfWork.RollbackTransactionAsync();
            }
        }

        Assert.Equal(commitOuter ? 1 : 0, _host.PublishedEvents.OfType<ScopedUnitOfWorkAmbientTransactionTests.ProbeSavedEvent>().Count());
        Assert.Equal(commitOuter, await _host.ReadAsync<WriteHostProbe>(inner.Id) != null);
    }
}
