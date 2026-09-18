using Microsoft.Extensions.Logging.Abstractions;

namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 多租户开启时的两条无租户上下文路径：匿名渠道回调与后台扫描。
/// </summary>
/// <remarks>
/// <para>
/// 此前两条路径在多租户开启后必然出错：回调是匿名请求，中间件解析不出租户，过滤器成了
/// <c>TenantId IS NULL</c>，别的租户的支付一律 404（渠道重投到禁用端点，订阅永远 Pending）；
/// 后台循环只 <c>CreateScope</c> 不切租户，八条扫描每轮 <c>Processed 0</c>，零 Warning。
/// </para>
/// <para>
/// 与其它用例不同，这里的 DbContext 真的开着多租户：<c>TenantId</c> 是列、过滤器生效、
/// <c>ICurrentTenant</c> 是真的 <c>CurrentTenant</c>。SQLite 测得出这两条，因为它们靠的是查询过滤器，不是并发。
/// </para>
/// </remarks>
public class SubscriptionMultiTenancyIntegrationTests : SubscriptionsIntegrationTestBase
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    protected override bool MultiTenancyEnabled => true;

    /// <summary>在某个租户的上下文里做一件事（发布方那一侧的形态：中间件 / 后台切租户）。</summary>
    private async Task<T> InTenantAsync<T>(Guid? tenantId, Func<Task<T>> action)
    {
        using var scope = ServiceProvider.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        using (tenant.Change(tenantId))
        {
            return await action();
        }
    }

    private async Task<SubscriptionPlan> SeedPlanAsync(string code, decimal price = 30m)
    {
        var plan = new SubscriptionPlan
        {
            PlanCode = code,
            PlanName = code,
            ProductCode = code,
            Price = price,
            Currency = "USD",
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            IsActive = true
        };
        await SeedAsync(plan);
        return plan;
    }

    private Task<Subscription> SeedDueSubscriptionAsync(Guid tenantId, SubscriptionPlan plan, string subscriptionNo) =>
        InTenantAsync(tenantId, async () =>
        {
            var subscription = new Subscription
            {
                SubscriptionNo = subscriptionNo,
                UserId = Guid.NewGuid(),
                PlanId = plan.Id,
                ProductCode = plan.ProductCode,
                Status = SubscriptionStatus.Active,
                CycleType = plan.CycleType,
                CycleValue = plan.CycleValue,
                StartTime = DateTime.UtcNow.AddMonths(-1),
                NextBillingTime = DateTime.UtcNow.AddDays(-1),
                OriginalPrice = plan.Price,
                Currency = plan.Currency,
                AutoRenew = true,
                ChannelCode = "Null",
                PaymentMethodToken = "pm_test",
                ProviderCustomerId = "cus_test"
            };
            await SeedAsync(subscription);
            return subscription;
        });

    private async Task<PaymentEntity?> LoadPaymentAsync(string businessOrderNo)
    {
        using var scope = ServiceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<SubscriptionsTestDbContext>();
        return await ctx.Set<PaymentEntity>().IgnoreQueryFilters()
            .OrderByDescending(p => p.CreationTime)
            .FirstOrDefaultAsync(p => p.BusinessOrderNo == businessOrderNo);
    }

    private async Task<Subscription?> LoadSubscriptionAsync(Guid id)
    {
        using var scope = ServiceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<SubscriptionsTestDbContext>();
        return await ctx.Set<Subscription>().IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == id);
    }

    private PaymentBackgroundService NewBackgroundLoop() => new(
        ServiceProvider,
        NullLogger<PaymentBackgroundService>.Instance,
        ServiceProvider.GetRequiredService<IOptionsMonitor<PaymentOptions>>(),
        ServiceProvider.GetRequiredService<IOptions<MultiTenancyOptions>>());

    /// <summary>
    /// 租户 A 的用户开通订阅（首付单在 A 里建），渠道回调不带任何租户线索地打进来：
    /// 支付要能被找到并推进，订阅要在 A 里被激活。
    /// </summary>
    [Fact]
    public async Task Callback_ForAPaymentInAnotherTenant_StillAdvancesItAndItsSubscription()
    {
        var plan = await SeedPlanAsync("MT-CB");

        var created = await InTenantAsync(TenantA, () =>
            InScopeAsync<ISubscriptionService, Result<SubscriptionCreateResultDto>>(
                svc => svc.CreateSubscriptionAsync(new CreateSubscriptionDto { PlanId = plan.Id, ChannelCode = "Null" })));
        created.Succeeded.ShouldBeTrue();
        var tradeNo = created.Data!.Payment!.TradeNo;

        (await LoadPaymentAsync(created.Data.Subscription.SubscriptionNo))!.TenantId.ShouldBe(TenantA);

        // 匿名回调：没有租户上下文
        var handled = await InScopeAsync<IPaymentService, Result>(svc => svc.HandleCallbackAsync(new PaymentCallbackDto
        {
            ChannelCode = "Null",
            Parameters = new Dictionary<string, string>
            {
                ["event_id"] = "evt_mt_1",
                ["trade_no"] = tradeNo,
                ["amount"] = plan.Price.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }
        }));

        handled.Succeeded.ShouldBeTrue(handled.Message);

        var payment = await LoadPaymentAsync(created.Data.Subscription.SubscriptionNo);
        payment!.Status.ShouldBe(PaymentStatus.Succeeded);

        var subscription = await LoadSubscriptionAsync(created.Data.Subscription.Id);
        subscription!.Status.ShouldBe(SubscriptionStatus.Active);
        subscription.TenantId.ShouldBe(TenantA);
    }

    /// <summary>后台循环要把每个租户的过期支付都关掉，而不是只看「无租户」那一层。</summary>
    [Fact]
    public async Task CloseExpiredPayments_CoversEveryTenant()
    {
        foreach (var (tenant, orderNo) in new[] { (TenantA, "MT-EXP-A"), (TenantB, "MT-EXP-B") })
        {
            var created = await InTenantAsync(tenant, () =>
                InScopeAsync<IPaymentService, Result<PaymentOrderResultDto>>(svc => svc.CreatePaymentAsync(new CreatePaymentDto
                {
                    BusinessOrderNo = orderNo,
                    BusinessType = BusinessType.Order,
                    Amount = 10m,
                    Currency = "USD",
                    ChannelCode = "Null"
                })));
            created.Succeeded.ShouldBeTrue();

            using var scope = ServiceProvider.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<SubscriptionsTestDbContext>();
            var payment = await ctx.Set<PaymentEntity>().IgnoreQueryFilters().FirstAsync(p => p.BusinessOrderNo == orderNo);
            payment.ExpireTime = DateTime.UtcNow.AddMinutes(-1);
            await ctx.SaveChangesAsync();
        }

        await NewBackgroundLoop().RunOnceAsync();

        (await LoadPaymentAsync("MT-EXP-A"))!.Status.ShouldBe(PaymentStatus.Expired);
        (await LoadPaymentAsync("MT-EXP-B"))!.Status.ShouldBe(PaymentStatus.Expired);
    }

    /// <summary>续费扫描同理：两个租户各一条到期订阅，一轮之后两条都续上了，各自的支付落在各自的租户里。</summary>
    [Fact]
    public async Task RenewDueSubscriptions_CoversEveryTenant()
    {
        var plan = await SeedPlanAsync("MT-RENEW");
        var subA = await SeedDueSubscriptionAsync(TenantA, plan, "MT-SUB-A");
        var subB = await SeedDueSubscriptionAsync(TenantB, plan, "MT-SUB-B");

        await NewBackgroundLoop().RunOnceAsync();

        foreach (var (sub, tenant, orderNo) in new[] { (subA, TenantA, "MT-SUB-A"), (subB, TenantB, "MT-SUB-B") })
        {
            var reloaded = await LoadSubscriptionAsync(sub.Id);
            reloaded!.Status.ShouldBe(SubscriptionStatus.Active);
            reloaded.NextBillingTime!.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddDays(20));
            reloaded.BillingLockedUntil.ShouldBeNull();

            var payment = await LoadPaymentAsync(orderNo);
            payment.ShouldNotBeNull();
            payment!.Status.ShouldBe(PaymentStatus.Succeeded);
            payment.TenantId.ShouldBe(tenant);
        }
    }

    /// <summary>
    /// 一个租户来源挂了不掀掉整轮：本类把一个总是抛异常的来源也注册进去了（见 ConfigureServices），
    /// 上面两条后台用例照样通过就是证据；这里再单独钉一次。
    /// </summary>
    [Fact]
    public async Task ATenantSourceThatFails_DoesNotStopTheOtherSources()
    {
        var plan = await SeedPlanAsync("MT-SRC");
        var sub = await SeedDueSubscriptionAsync(TenantA, plan, "MT-SRC-A");

        await NewBackgroundLoop().RunOnceAsync();

        (await LoadSubscriptionAsync(sub.Id))!.Status.ShouldBe(SubscriptionStatus.Active);
        (await LoadPaymentAsync("MT-SRC-A"))!.Status.ShouldBe(PaymentStatus.Succeeded);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddScoped<IPaymentTenantSource, ThrowingTenantSource>();
    }

    private sealed class ThrowingTenantSource : IPaymentTenantSource
    {
        public Task<IReadOnlyList<Guid?>> GetTenantIdsAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("boom");
    }
}
