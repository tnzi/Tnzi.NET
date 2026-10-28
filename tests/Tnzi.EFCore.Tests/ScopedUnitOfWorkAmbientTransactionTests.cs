using Tnzi.EventBus;

namespace Tnzi.EFCore.Tests;

/// <summary>
/// 直接注入的作用域 <see cref="IUnitOfWork"/> 在工作单元管理器的事务里保存时，必须加入那个事务。
/// </summary>
/// <remarks>
/// <para>
/// 作用域 <c>IUnitOfWork</c> 与管理器为同一个上下文建的工作单元是两个实例。管理器启用事务
/// （全局工作单元过滤器 / <c>ExecuteInUnitOfWorkAsync</c>）时作用域那个并没有启用，
/// 它的 <c>SaveChangesAsync</c> 于是直接 <c>DbContext.SaveChangesAsync</c>：物理事务是延迟开启的、此刻还没 BEGIN，
/// EF 以隐式事务立即提交主上下文里<b>全部</b>挂起变更，连同调用方缓冲在事务里的写入。之后调用方回滚，
/// 没有物理事务可回滚：数据已落库，而这次保存派发的领域事件被排进了提交后队列、随回滚被清掉。
/// </para>
/// <para>
/// 框架内直接注入它并保存的是通知模块；任何开了全局工作单元的宿主上经过通知创建的请求都是这个形态。
/// </para>
/// </remarks>
public sealed class ScopedUnitOfWorkAmbientTransactionTests : IDisposable
{
    private readonly RepositoryWriteHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task ScopedUnitOfWorkSave_InsideManagerTransaction_IsUndoneByRollback()
    {
        var callerWrite = new WriteHostProbe { Code = "caller" };
        var serviceWrite = new WriteHostProbe { Code = "service" };

        using (var scope = _host.CreateScope())
        {
            var manager = RepositoryWriteHost.Manager(scope);
            var repository = RepositoryWriteHost.Repository<WriteHostProbe>(scope);
            manager.EnableTransaction();

            // 调用方在事务里的写入：仓储检测到事务后缓冲，不落库
            await repository.InsertAsync(callerWrite);

            // 被调服务注入作用域 IUnitOfWork 保存自己的写入
            await repository.InsertAsync(serviceWrite);
            var affected = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            Assert.Equal(2, affected);
            Assert.NotNull(RepositoryWriteHost.Context(scope).Database.CurrentTransaction);

            // 调用方后段失败
            await manager.RollbackTransactionAsync();
        }

        Assert.Null(await _host.ReadAsync<WriteHostProbe>(callerWrite.Id));
        Assert.Null(await _host.ReadAsync<WriteHostProbe>(serviceWrite.Id));
    }

    [Fact]
    public async Task ScopedUnitOfWorkSave_InsideManagerTransaction_IsPersistedByCommit()
    {
        var probe = new WriteHostProbe { Code = "committed" };

        using (var scope = _host.CreateScope())
        {
            var manager = RepositoryWriteHost.Manager(scope);
            manager.EnableTransaction();

            await RepositoryWriteHost.Repository<WriteHostProbe>(scope).InsertAsync(probe);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

            await manager.CommitTransactionAsync();
        }

        Assert.NotNull(await _host.ReadAsync<WriteHostProbe>(probe.Id));
    }

    /// <summary>
    /// 领域事件与数据同进退：回滚则不发布，提交则恰好发布一次。
    /// 逃出事务时恰好相反（数据已落库、事件被回滚清掉）。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ScopedUnitOfWorkSave_DomainEventsFollowTheManagerTransaction(bool commit)
    {
        var probe = new WriteHostProbe { Code = commit ? "commit" : "rollback" };
        probe.AddDomainEvent(new ProbeSavedEvent());

        using (var scope = _host.CreateScope())
        {
            var manager = RepositoryWriteHost.Manager(scope);
            manager.EnableTransaction();

            await RepositoryWriteHost.Repository<WriteHostProbe>(scope).InsertAsync(probe);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            Assert.Empty(_host.PublishedEvents);

            if (commit)
            {
                await manager.CommitTransactionAsync();
            }
            else
            {
                await manager.RollbackTransactionAsync();
            }
        }

        Assert.Equal(commit ? 1 : 0, _host.PublishedEvents.OfType<ProbeSavedEvent>().Count());
        Assert.Equal(commit, await _host.ReadAsync<WriteHostProbe>(probe.Id) != null);
    }

    /// <summary>没有事务时作用域 IUnitOfWork 照旧直接保存（自动提交是对的）。</summary>
    [Fact]
    public async Task ScopedUnitOfWorkSave_WithoutTransaction_SavesImmediately()
    {
        var probe = new WriteHostProbe { Code = "autocommit" };

        using (var scope = _host.CreateScope())
        {
            RepositoryWriteHost.Context(scope).Probes.Add(probe);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        Assert.NotNull(await _host.ReadAsync<WriteHostProbe>(probe.Id));
    }

    /// <summary>事务内的裸 SQL 之前调 <c>EnsureTransactionStartedAsync</c>，经作用域工作单元也必须真的开启物理事务。</summary>
    [Fact]
    public async Task ScopedUnitOfWorkEnsureTransactionStarted_InsideManagerTransaction_BeginsThePhysicalTransaction()
    {
        using var scope = _host.CreateScope();
        var manager = RepositoryWriteHost.Manager(scope);
        manager.EnableTransaction();

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().EnsureTransactionStartedAsync();

        Assert.NotNull(RepositoryWriteHost.Context(scope).Database.CurrentTransaction);
        await manager.RollbackTransactionAsync();
    }

    public sealed class ProbeSavedEvent : EventBase
    {
    }
}
