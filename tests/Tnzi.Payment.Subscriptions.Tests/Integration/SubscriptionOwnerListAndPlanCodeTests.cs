namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 两处契约上的小缺口：「我的订阅」只回前 100 条且不能翻页；计划代码撞重答的是促销的错误码。
/// </summary>
public class SubscriptionOwnerListAndPlanCodeTests : SubscriptionsIntegrationTestBase
{
    private static SubscriptionPlanDto Plan(string code) => new()
    {
        PlanCode = code,
        PlanName = code,
        ProductCode = "SUITE",
        Price = 30m,
        Currency = "USD",
        CycleType = BillingCycleType.Month,
        CycleValue = 1,
        IsActive = true
    };

    /// <summary>★ 计划代码重复答订阅计划自己的错误码：此前答 <c>PROMOTION_CODE_ALREADY_EXISTS</c>，客户端会提示「促销代码已被占用」。</summary>
    [Fact]
    public async Task CreatePlan_WithADuplicateCode_AnswersThePlanCodeError()
    {
        (await InScopeAsync<ISubscriptionService, Result<SubscriptionPlanDto>>(svc => svc.CreatePlanAsync(Plan("PRO-DUP")))).Succeeded.ShouldBeTrue();

        var duplicate = await InScopeAsync<ISubscriptionService, Result<SubscriptionPlanDto>>(svc => svc.CreatePlanAsync(Plan("PRO-DUP")));

        duplicate.Succeeded.ShouldBeFalse();
        duplicate.Code.ShouldBe(409);
        duplicate.Message.ShouldBe(ErrorCodes.SubscriptionPlanCodeAlreadyExists);
    }

    /// <summary>★ 「我的订阅」能翻页；不带参数时仍是最新的那一页（每页 100）。</summary>
    [Fact]
    public async Task GetUserSubscriptions_Pages()
    {
        var plan = new SubscriptionPlan
        {
            PlanCode = "PRO-MY",
            PlanName = "Pro",
            ProductCode = "SUITE",
            Price = 30m,
            Currency = "USD",
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            IsActive = true
        };
        await SeedAsync(plan);

        var owner = Guid.NewGuid();
        for (var i = 0; i < 3; i++)
        {
            await SeedAsync(new Subscription
            {
                SubscriptionNo = $"SUB{Guid.NewGuid():N}",
                UserId = owner,
                PlanId = plan.Id,
                Status = SubscriptionStatus.Cancelled,
                CycleType = BillingCycleType.Month,
                CycleValue = 1,
                StartTime = DateTime.UtcNow.AddMonths(-1),
                OriginalPrice = 30m,
                Currency = "USD",
                ChannelCode = "Null"
            });
        }

        var secondPage = await InScopeAsync<ISubscriptionService, Result<IPagedList<SubscriptionDto>>>(
            svc => svc.GetUserSubscriptionsAsync(owner, new OwnerListQueryDto { PageIndex = 2, PageSize = 2 }));
        var defaults = await InScopeAsync<ISubscriptionService, Result<IPagedList<SubscriptionDto>>>(
            svc => svc.GetUserSubscriptionsAsync(owner));

        secondPage.Data!.Items.Count.ShouldBe(1);
        secondPage.Data.TotalCount.ShouldBe(3);
        defaults.Data!.Items.Count.ShouldBe(3);
    }
}
