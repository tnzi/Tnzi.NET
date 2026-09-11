using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.AspNetCore.Mvc.Filters;

namespace Tnzi.AspNetCore.Tests.Mvc;

/// <summary>
/// Action 抛异常时的回滚。
///
/// 守的是一条会让事故现场指向错误方向的线：回滚**自己**也会抛
/// （连接断了、事务已被服务端终止），而 catch 里直接 await 一个会抛的回滚，
/// 抛出去的就是那条**次生**故障，真正的病因连同堆栈一起消失。
/// 更麻烦的是次生故障看起来相当可信，没有人会怀疑它不是根因 ——
/// 而断连恰恰是「原始异常」与「回滚失败」最常见的共同成因，所以这不是罕见路径。
/// </summary>
public class UnitOfWorkFilterRollbackTests
{
    private sealed class ThrowingUnitOfWork(Exception onRollback) : IUnitOfWork
    {
        public bool RollbackAttempted { get; private set; }

        public bool IsEnabledTransaction { get; private set; }
        public int TransactionDepth => 0;

        public void EnableTransaction() => IsEnabledTransaction = true;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
        {
            RollbackAttempted = true;
            throw onRollback;
        }
    }

    private static (UnitOfWorkFilter Filter, ActionExecutingContext Context) Build(IUnitOfWork unitOfWork)
    {
        var services = new ServiceCollection();
        services.AddSingleton(unitOfWork);
        var provider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var context = new ActionExecutingContext(
            actionContext,
            [new UnitOfWorkAttribute()],
            new Dictionary<string, object?>(),
            controller: null!);
        context.ActionDescriptor.EndpointMetadata = [new UnitOfWorkAttribute()];

        var filter = new UnitOfWorkFilter(
            provider,
            NullLogger<UnitOfWorkFilter>.Instance,
            Microsoft.Extensions.Options.Options.Create(new AspNetCoreOptions()));

        return (filter, context);
    }

    [Fact]
    public async Task AFailingRollback_DoesNotReplaceTheOriginalException()
    {
        var original = new InvalidOperationException("the real cause");
        var unitOfWork = new ThrowingUnitOfWork(new IOException("connection reset while rolling back"));
        var (filter, context) = Build(unitOfWork);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            filter.OnActionExecutionAsync(context, () => throw original));

        Assert.Same(original, thrown);
        Assert.True(unitOfWork.RollbackAttempted, "the rollback must still be attempted");
    }

    [Fact]
    public async Task TheOriginalStackTraceSurvives()
    {
        // 换掉异常的同时也换掉了堆栈 —— 那是排障时真正在读的东西。
        var unitOfWork = new ThrowingUnitOfWork(new IOException("connection reset"));
        var (filter, context) = Build(unitOfWork);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            filter.OnActionExecutionAsync(context, () => ThrowFromAKnownFrame()));

        Assert.Contains(nameof(ThrowFromAKnownFrame), thrown.StackTrace);
    }

    private static Task<ActionExecutedContext> ThrowFromAKnownFrame()
        => throw new InvalidOperationException("the real cause");
}
