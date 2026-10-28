using Hangfire;
using Hangfire.InMemory;
using Tnzi.Hangfire.Controllers.Admin;
using Tnzi.Hangfire.Options;

namespace Tnzi.Hangfire.Tests;

/// <summary>
/// 「立即触发」一个不存在的周期任务：Hangfire 的 <c>RecurringJob.TriggerJob</c> 此时返回 null 而不抛异常，
/// 控制器此前丢掉返回值答 200，管理端提示「已触发」而后台什么都没入队（列表陈旧、另一位管理员刚删掉、手输了 id）。
/// </summary>
[Collection(JobStorageCollection.Name)]
public class ScheduledJobTriggerTests
{
    private static DefaultScheduledJobAdminController CreateController()
        => new(Microsoft.Extensions.Options.Options.Create(new HangfireOptions { Enabled = true }));

    [Fact]
    public void TriggeringAnUnknownRecurringJob_Answers404()
    {
        JobStorage.Current = new InMemoryStorage();

        var result = CreateController().Trigger("no-such-job");

        Assert.Equal(404, result.Code);
    }

    [Fact]
    public void TriggeringAnExistingRecurringJob_EnqueuesIt()
    {
        // 正控制：守卫不能做成「永远 404」
        var storage = new InMemoryStorage();
        JobStorage.Current = storage;
        RecurringJob.AddOrUpdate("nightly-tick", () => ScheduledJobTriggerTestJobs.Tick(), Cron.Daily());

        var result = CreateController().Trigger("nightly-tick");

        Assert.Equal(200, result.Code);
        Assert.Equal(1, storage.GetMonitoringApi().EnqueuedCount("default"));
    }
}

/// <summary>周期任务的目标方法：Hangfire 序列化调用表达式，目标须是公开类型上的公开方法。</summary>
public static class ScheduledJobTriggerTestJobs
{
    public static void Tick() { }
}
