namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 逾期欠费（PastDue）的自动重试必须按节奏分布在宽限期内，过期以宽限期为准。
/// </summary>
/// <remarks>
/// 续费扫描每隔 <c>BackgroundTaskIntervalMinutes</c>（默认 5 分钟）跑一轮。重试没有节奏时，一次失败之后
/// 每一轮都会再扣一次：默认 <c>MaxRetryCount = 3</c> 约 10 分钟就打满，同一轮的过期扫描随即把订阅停掉，
/// 用户十分钟内收到三封「请在三天内更新支付方式」的催款信后被停服，同一张卡被连扣三次。
/// 默认配置下宽限期 3 天、重试 3 次：失败在 T、T+1 天、T+2 天各扣一次，T+3 天到期才过期。
/// </remarks>
public class SubscriptionDunningCadenceTests : SubscriptionsIntegrationTestBase
{
    private async Task<Subscription> SeedDueRenewalWithoutCardAsync(string subscriptionNo)
    {
        var plan = new SubscriptionPlan
        {
            PlanCode = $"P{Guid.NewGuid():N}"[..12],
            PlanName = "Pro",
            Price = 30m,
            Currency = "USD",
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            IsActive = true
        };
        await SeedAsync(plan);

        var subscription = new Subscription
        {
            SubscriptionNo = subscriptionNo,
            UserId = Guid.NewGuid(),
            PlanId = plan.Id,
            Status = SubscriptionStatus.Active,
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            StartTime = DateTime.UtcNow.AddMonths(-1),
            NextBillingTime = DateTime.UtcNow.AddHours(-1),
            OriginalPrice = 30m,
            Currency = "USD",
            AutoRenew = true,
            ChannelCode = "Null",
            LastBillingTradeNo = $"PAY-SEED-{subscriptionNo}"
        };
        await SeedAsync(subscription);
        return subscription;
    }

    private Task<Result<int>> RenewAsync() =>
        InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.RenewExpiredSubscriptionsAsync());

    private Task<Result<int>> ExpireAsync() =>
        InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.ExpireOverdueSubscriptionsAsync());

    /// <summary>后台循环一轮：按注册顺序把六条订阅扫描都跑一遍（续费在过期之前）。</summary>
    private async Task RunOneBackgroundRoundAsync()
    {
        using var scope = ServiceProvider.CreateScope();
        foreach (var scan in scope.ServiceProvider.GetServices<IPaymentScheduledScan>())
            await scan.RunAsync();
    }

    private async Task MovePastDueSinceAsync(Guid subscriptionId, DateTime pastDueSince)
    {
        using var scope = ServiceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<SubscriptionsTestDbContext>();
        var entity = ctx.Set<Subscription>().First(s => s.Id == subscriptionId);
        entity.PastDueSince = pastDueSince;
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task ARenewalThatJustFailed_IsNotRetriedOnTheNextScan()
    {
        var subscription = await SeedDueRenewalWithoutCardAsync("SUB-DUN-1");

        await RenewAsync();
        var afterFirst = await ReloadAsync<Subscription>(subscription.Id);
        afterFirst!.Status.ShouldBe(SubscriptionStatus.PastDue);
        afterFirst.RenewalRetryCount.ShouldBe(1);

        // 五分钟后的下一轮：离下一次重试（PastDueSince + 1 天）还远，不该再扣
        await RenewAsync();

        (await ReloadAsync<Subscription>(subscription.Id))!.RenewalRetryCount.ShouldBe(1);
    }

    [Fact]
    public async Task APastDueSubscription_IsRetriedOnceTheRetryIntervalHasPassed()
    {
        var subscription = await SeedDueRenewalWithoutCardAsync("SUB-DUN-2");
        await RenewAsync();

        // 宽限期 3 天 / 3 次 = 每天一次：第一次失败满一天之后才轮到第二次
        await MovePastDueSinceAsync(subscription.Id, DateTime.UtcNow.AddDays(-1).AddMinutes(-1));
        await RenewAsync();
        (await ReloadAsync<Subscription>(subscription.Id))!.RenewalRetryCount.ShouldBe(2);

        // 第三次要等到第一次失败满两天
        await RenewAsync();
        (await ReloadAsync<Subscription>(subscription.Id))!.RenewalRetryCount.ShouldBe(2);
    }

    /// <summary>默认配置连跑三轮后台循环（约十五分钟）：订阅仍在宽限期里，没有被过期。</summary>
    [Fact]
    public async Task ConsecutiveBackgroundRounds_DoNotExpireTheSubscriptionWithinTheGracePeriod()
    {
        var subscription = await SeedDueRenewalWithoutCardAsync("SUB-DUN-3");

        for (var round = 0; round < 3; round++)
            await RunOneBackgroundRoundAsync();

        var reloaded = await ReloadAsync<Subscription>(subscription.Id);
        reloaded!.Status.ShouldBe(SubscriptionStatus.PastDue);
        reloaded.RenewalRetryCount.ShouldBe(1);
    }

    /// <summary>
    /// 重试次数用完只是不再自动扣款，不提前停服：催款信承诺的截止日是 PastDueSince + 宽限期。
    /// </summary>
    [Fact]
    public async Task ExhaustedRetries_DoNotCutTheGracePeriodShort()
    {
        var subscription = await SeedDueRenewalWithoutCardAsync("SUB-DUN-4");
        await RenewAsync();
        await MovePastDueSinceAsync(subscription.Id, DateTime.UtcNow.AddDays(-1).AddMinutes(-1));
        await RenewAsync();
        await MovePastDueSinceAsync(subscription.Id, DateTime.UtcNow.AddDays(-2).AddMinutes(-1));
        await RenewAsync();
        (await ReloadAsync<Subscription>(subscription.Id))!.RenewalRetryCount.ShouldBe(3);

        // 三次都失败了，但离截止日还有将近一天
        await ExpireAsync();
        (await ReloadAsync<Subscription>(subscription.Id))!.Status.ShouldBe(SubscriptionStatus.PastDue);

        // 次数已满：不再自动扣
        await RenewAsync();
        (await ReloadAsync<Subscription>(subscription.Id))!.RenewalRetryCount.ShouldBe(3);

        // 宽限期满才过期
        await MovePastDueSinceAsync(subscription.Id, DateTime.UtcNow.AddDays(-3).AddMinutes(-1));
        await ExpireAsync();
        (await ReloadAsync<Subscription>(subscription.Id))!.Status.ShouldBe(SubscriptionStatus.Expired);
    }

    /// <summary>
    /// 经别的路径进 PastDue（数据迁移、人工改状态）而 <c>PastDueSince</c> 为空的订阅：
    /// 不知道欠了多久，交给重试上限收口，与此前一致。
    /// </summary>
    [Fact]
    public async Task APastDueSubscriptionWithoutASince_StillExpiresOnTheRetryCap()
    {
        var subscription = await SeedDueRenewalWithoutCardAsync("SUB-DUN-5");
        using (var scope = ServiceProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<SubscriptionsTestDbContext>();
            var entity = ctx.Set<Subscription>().First(s => s.Id == subscription.Id);
            entity.Status = SubscriptionStatus.PastDue;
            entity.PastDueSince = null;
            entity.RenewalRetryCount = 3;
            await ctx.SaveChangesAsync();
        }

        await ExpireAsync();

        (await ReloadAsync<Subscription>(subscription.Id))!.Status.ShouldBe(SubscriptionStatus.Expired);
    }
}
