namespace Tnzi.EFCore.Tests;

/// <summary>
/// 工作单元回滚之后，被回滚的写入不得留在变更跟踪器里被同作用域下一次保存重放或持久化。
/// </summary>
/// <remarks>
/// <para>
/// 回滚只撤销数据库里的事务。EF 在 <c>SaveChanges</c> 失败时不接受变更（条目保持 <c>Added</c> / <c>Modified</c>），
/// flush 之后被回滚的条目则停在 <c>Unchanged</c>。同一作用域里「每一项各开一个工作单元、失败就记下继续」的循环
/// （逐个收件人投递、逐条导入）于是：第 k 项提交时撞唯一约束，它的实体留在跟踪器里，第 k+1 项的提交把它一起重放、
/// 同一异常再抛，此后每一项都失败；第 k 项若在提交之前就抛出，它的半成品写入会随第 k+1 项的提交真正落库；
/// flush 后被回滚的实体仍能经 <c>GetAsync</c> 从跟踪器里取到一个库里不存在的实例。
/// </para>
/// <para>
/// 真实 SQLite + 真实仓储与工作单元管理器（Mock 仓储复现不出跟踪器状态）；读回一律走新作用域。
/// </para>
/// </remarks>
public sealed class RollbackClearsChangeTrackerTests : IDisposable
{
    private readonly RepositoryWriteHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task UnitOfWorkFailingBeforeCommit_DoesNotLeakIntoTheNextUnitOfWork()
    {
        var abandoned = new WriteHostProbe { Code = "abandoned" };
        var next = new WriteHostProbe { Code = "next" };

        using (var scope = _host.CreateScope())
        {
            var manager = RepositoryWriteHost.Manager(scope);
            var repository = RepositoryWriteHost.Repository<WriteHostProbe>(scope);

            await RunAsync(manager, async () =>
            {
                await repository.InsertAsync(abandoned);
                throw new InvalidOperationException("business failure after the write");
            });

            await RunAsync(manager, () => repository.InsertAsync(next));
        }

        Assert.Null(await _host.ReadAsync<WriteHostProbe>(abandoned.Id));
        Assert.NotNull(await _host.ReadAsync<WriteHostProbe>(next.Id));
    }

    [Fact]
    public async Task UnitOfWorkFailingAtCommit_IsNotReplayedByTheNextUnitOfWork()
    {
        await _host.SeedAsync(new WriteHostProbe { Code = "taken" });
        var duplicate = new WriteHostProbe { Code = "taken" };
        var next = new WriteHostProbe { Code = "next" };

        using (var scope = _host.CreateScope())
        {
            var manager = RepositoryWriteHost.Manager(scope);
            var repository = RepositoryWriteHost.Repository<WriteHostProbe>(scope);

            await Assert.ThrowsAnyAsync<DbUpdateException>(() => RunAsync(manager, () => repository.InsertAsync(duplicate), swallow: false));

            // 第一单元提交时撞唯一约束；第二单元本身完全合法，必须能提交
            await RunAsync(manager, () => repository.InsertAsync(next), swallow: false);
        }

        Assert.NotNull(await _host.ReadAsync<WriteHostProbe>(next.Id));
    }

    [Fact]
    public async Task FlushedThenRolledBack_IsNoLongerServedFromTheChangeTracker()
    {
        var probe = new WriteHostProbe { Code = "flushed" };

        using var scope = _host.CreateScope();
        var manager = RepositoryWriteHost.Manager(scope);
        var repository = RepositoryWriteHost.Repository<WriteHostProbe>(scope);

        await RunAsync(manager, async () =>
        {
            await repository.InsertAsync(probe);
            await repository.SaveChangesAsync();
            throw new InvalidOperationException("business failure after the flush");
        });

        Assert.Null(await repository.GetAsync(probe.Id));
        Assert.Null(await _host.ReadAsync<WriteHostProbe>(probe.Id));
    }

    /// <summary>没有管理器参与、直接用作用域 <see cref="IUnitOfWork"/> 开的事务，回滚后同样不留下被回滚的写入。</summary>
    [Fact]
    public async Task ScopedUnitOfWorkRollback_DoesNotLeakIntoTheNextSave()
    {
        var abandoned = new WriteHostProbe { Code = "abandoned" };
        var next = new WriteHostProbe { Code = "next" };

        using (var scope = _host.CreateScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repository = RepositoryWriteHost.Repository<WriteHostProbe>(scope);

            unitOfWork.EnableTransaction();
            await repository.InsertAsync(abandoned);
            await unitOfWork.RollbackTransactionAsync();

            // 事务已结束，这次插入自动保存
            await repository.InsertAsync(next);
        }

        Assert.Null(await _host.ReadAsync<WriteHostProbe>(abandoned.Id));
        Assert.NotNull(await _host.ReadAsync<WriteHostProbe>(next.Id));
    }

    /// <summary>
    /// 与 <c>ApplicationService.ExecuteInUnitOfWorkAsync</c> 同一形状：启用、执行、提交，异常时回滚后重抛；
    /// <paramref name="swallow"/> 模拟「失败就记下、接着做下一项」的调用方。
    /// </summary>
    private static async Task RunAsync(IUnitOfWorkManager manager, Func<Task> action, bool swallow = true)
    {
        manager.EnableTransaction();
        try
        {
            await action();
            await manager.CommitTransactionAsync();
        }
        catch
        {
            await manager.RollbackTransactionAsync();
            if (!swallow)
            {
                throw;
            }
        }
    }
}
