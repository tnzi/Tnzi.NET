using Hangfire;
using Hangfire.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.BackgroundJobs;
using Tnzi.MultiTenancy;

namespace Tnzi.Hangfire.Tests;

/// <summary>
/// 入队时捕获的租户必须是<b>调用方此刻所在</b>的租户，包括经 <c>ICurrentTenant.Change()</c> 建立的那种。
///
/// <c>CaptureTenantContext</c> 从一个新建的作用域里解析 <c>ICurrentTenant</c>（Singleton 不能持有 Scoped）。
/// 当 <c>CurrentTenant</c> 把 <c>Change()</c> 的覆盖存在<b>实例字段</b>上时，新作用域里的新实例什么都看不见，
/// 只有 JWT claim 来源的租户能穿过去：<c>TenantResolverMiddleware</c> 的 header / 子域 / <c>DefaultTenantId</c>
/// 解析、按租户轮转的后台服务全走 <c>Change()</c>，它们入队的任务 <c>TenantId</c> 一律为 null，
/// 执行侧跳过租户切换、EF 过滤器按 null 过滤 —— 上传答 200、文档永远停在 Processing。
/// 覆盖改到静态 <c>AsyncLocal</c> 之后才修好；这条用例守的是 Hangfire 这一侧的捕获路径本身。
/// </summary>
/// <remarks>
/// 入队走静态门面 <c>BackgroundJob.Enqueue</c>，读的是进程级的 <c>JobStorage.Current</c>；
/// 同程序集另一个测试类会把它置 null 再重新初始化。xUnit 默认跨类并行，两个类必须在同一个集合里串行，
/// 否则偶发的 "JobStorage.Current property value has not been initialized" 与被测缺陷毫无关系。
/// </remarks>
[Collection(JobStorageCollection.Name)]
public class HangfireTenantCaptureTests
{
    private sealed class TenantAwareArgs : ITenantAwareJobArgs
    {
        public Guid? TenantId { get; set; }
    }

    [Fact]
    public void Enqueue_InsideCurrentTenantChange_CapturesChangedTenant()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICurrentTenant, CurrentTenant>();
        services.AddSingleton<IBackgroundJobManager, HangfireBackgroundJobManager>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        JobStorage.Current = new InMemoryStorage();

        var tenant = Guid.NewGuid();
        var args = new TenantAwareArgs();

        using var scope = provider.CreateScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        var manager = provider.GetRequiredService<IBackgroundJobManager>();

        using (currentTenant.Change(tenant))
        {
            manager.Enqueue(args);
        }

        Assert.Equal(tenant, args.TenantId);
    }

    [Fact]
    public void Enqueue_OutsideAnyTenant_LeavesTenantIdNull()
    {
        // 防锈：上一条不是因为「随便什么都能捕到」才绿。没有租户就是没有租户。
        var services = new ServiceCollection();
        services.AddScoped<ICurrentTenant, CurrentTenant>();
        services.AddSingleton<IBackgroundJobManager, HangfireBackgroundJobManager>();
        using var provider = services.BuildServiceProvider();

        JobStorage.Current = new InMemoryStorage();

        var args = new TenantAwareArgs();
        provider.GetRequiredService<IBackgroundJobManager>().Enqueue(args);

        Assert.Null(args.TenantId);
    }
}
