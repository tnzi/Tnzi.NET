using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tnzi.Payment.Promotions.Tests.Integration;

/// <summary>
/// 管理端改促销（编辑 / 停用 / 同步到渠道）不能把并发核销对 <see cref="Promotion.UsedCount"/> 的原子增减写回旧值。
/// </summary>
/// <remarks>
/// <para>
/// 核销与归还经裸 SQL 原子增减 <c>UsedCount</c>（总量上限的 CAS 也靠它）。管理端此前读出一份游离快照、改几列，
/// 再用仓储 <c>UpdateAsync</c> 整行写回，包括读时的 <c>UsedCount</c>。限量 100 的券正被高频核销时，
/// 运营延长一下结束时间：读到 60，期间又核销了 15 张，写回 60，总量 CAS 以为还剩 40 个名额，最终超发 15 份折扣；
/// 同步到渠道中间隔着一次网络往返，窗口更宽。
/// </para>
/// <para>
/// 复现方式：命令拦截器在促销那一行的 UPDATE 真正执行之前，用同一个连接先把 <c>UsedCount</c> 加上若干：
/// 正是「读与写之间落地的那几笔核销」。
/// </para>
/// </remarks>
public partial class PromotionAdminWriteTests : PromotionsIntegrationTestBase
{
    private readonly RedemptionsLandBeforePromotionUpdate _redemptions = new();
    private PaymentChannelCouponDto? _synced;

    protected override void ConfigureDbContextOptions(DbContextOptionsBuilder options)
        => options.AddInterceptors(_redemptions);

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        var sync = new Mock<IPaymentChannelCouponSync>();
        sync.SetupGet(x => x.ChannelCode).Returns(PaymentConstants.StripeChannelCode);
        sync.Setup(x => x.SyncCouponAsync(It.IsAny<PaymentChannelCouponDto>(), It.IsAny<CancellationToken>()))
            .Callback<PaymentChannelCouponDto, CancellationToken>((dto, _) => _synced = dto)
            .ReturnsAsync(Result<string>.Success("coupon_LIMITED"));
        services.AddSingleton(sync.Object);
        services.Configure<PromotionOptions>(o => o.EnableStripeCouponSync = true);
    }

    private async Task<Promotion> SeedLimitedPromotionAsync()
    {
        var promotion = new Promotion
        {
            PromotionCode = "LIMITED",
            Name = "Limited",
            IsActive = true,
            IsPublic = true,
            StartTime = DateTime.UtcNow.AddDays(-1),
            EndTime = DateTime.UtcNow.AddDays(1),
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10m,
            Currency = "USD",
            ProductType = ProductType.All,
            ApplyScope = ApplyScope.Global,
            TotalUsageLimit = 100,
            UsedCount = 60
        };
        await SeedAsync(promotion);
        return promotion;
    }

    [Fact]
    public async Task EditingAPromotion_KeepsRedemptionsThatLandedMeanwhile()
    {
        var promotion = await SeedLimitedPromotionAsync();
        var newEnd = DateTime.UtcNow.AddDays(30);
        _redemptions.Arm(15);

        var updated = await InScopeAsync<IPromotionService, Result>(svc =>
            svc.UpdateAsync(promotion.Id, new UpdatePromotionDto { EndTime = newEnd, Name = "Limited (extended)" }));

        updated.Succeeded.ShouldBeTrue(updated.Message);
        _redemptions.Fired.ShouldBeTrue();
        var reloaded = await ReloadAsync<Promotion>(promotion.Id);
        reloaded!.UsedCount.ShouldBe(75);
        reloaded.Name.ShouldBe("Limited (extended)");
        reloaded.EndTime!.Value.ShouldBe(newEnd, TimeSpan.FromSeconds(1));
        // 没给的字段不动
        reloaded.DiscountValue.ShouldBe(10m);
        reloaded.TotalUsageLimit.ShouldBe(100);
        reloaded.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task DeactivatingAPromotion_KeepsRedemptionsThatLandedMeanwhile()
    {
        var promotion = await SeedLimitedPromotionAsync();
        _redemptions.Arm(15);

        (await InScopeAsync<IPromotionService, Result>(svc => svc.DeactivateAsync(promotion.Id))).Succeeded.ShouldBeTrue();

        _redemptions.Fired.ShouldBeTrue();
        var reloaded = await ReloadAsync<Promotion>(promotion.Id);
        reloaded!.IsActive.ShouldBeFalse();
        reloaded.UsedCount.ShouldBe(75);
    }

    /// <summary>同步到渠道中间隔着一次网络往返，窗口最宽：写回渠道券标识时只写那一列。</summary>
    [Fact]
    public async Task SyncingAPromotionToTheChannel_KeepsRedemptionsThatLandedMeanwhile()
    {
        var promotion = await SeedLimitedPromotionAsync();
        _redemptions.Arm(15);

        (await InScopeAsync<IPromotionService, Result>(svc => svc.SyncToStripeAsync(promotion.Id))).Succeeded.ShouldBeTrue();

        _redemptions.Fired.ShouldBeTrue();
        var reloaded = await ReloadAsync<Promotion>(promotion.Id);
        reloaded!.StripeCouponId.ShouldBe("coupon_LIMITED");
        reloaded.UsedCount.ShouldBe(75);
    }

    /// <summary>
    /// 渠道侧按促销自身的币种建券（固定金额折扣与币种强相关，传错渠道会按另一种货币建券），返回的券标识落库；
    /// 已经同步过的不再打渠道。
    /// </summary>
    [Fact]
    public async Task SyncingAPromotion_SendsItsOwnCurrencyAndStoresTheCouponIdOnce()
    {
        var promotion = new Promotion
        {
            PromotionCode = "SAVE10",
            Name = "Save 10",
            IsActive = true,
            StartTime = DateTime.UtcNow.AddDays(-1),
            DiscountType = DiscountType.Fixed,
            DiscountValue = 10m,
            Currency = "EUR",
            ProductType = ProductType.All,
            ApplyScope = ApplyScope.Global
        };
        await SeedAsync(promotion);

        (await InScopeAsync<IPromotionService, Result>(svc => svc.SyncToStripeAsync(promotion.Id))).Succeeded.ShouldBeTrue();

        _synced.ShouldNotBeNull();
        _synced!.Currency.ShouldBe("EUR");
        _synced.PromotionCode.ShouldBe("SAVE10");
        (await ReloadAsync<Promotion>(promotion.Id))!.StripeCouponId.ShouldBe("coupon_LIMITED");

        _synced = null;
        (await InScopeAsync<IPromotionService, Result>(svc => svc.SyncToStripeAsync(promotion.Id))).Succeeded.ShouldBeTrue();
        _synced.ShouldBeNull();
    }

    [Fact]
    public async Task EditingOrDeactivatingAMissingPromotion_Is404()
    {
        (await InScopeAsync<IPromotionService, Result>(svc => svc.UpdateAsync(Guid.NewGuid(), new UpdatePromotionDto { Name = "x" }))).Code.ShouldBe(404);
        (await InScopeAsync<IPromotionService, Result>(svc => svc.DeactivateAsync(Guid.NewGuid()))).Code.ShouldBe(404);
    }

    /// <summary>
    /// 促销那一行的 UPDATE 执行之前，用同一个连接先把核销数加上一截：模拟读与写之间落地的那几笔核销。只触发一次。
    /// </summary>
    private sealed partial class RedemptionsLandBeforePromotionUpdate : DbCommandInterceptor
    {
        private int _pending;

        public bool Fired { get; private set; }

        public void Arm(int redemptions) => _pending = redemptions;

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Land(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Land(command);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Land(command);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Land(command);
            return ValueTask.FromResult(result);
        }

        private void Land(DbCommand command)
        {
            if (_pending == 0)
                return;

            var target = PromotionUpdate().Match(command.CommandText);
            if (!target.Success)
                return;

            var redemptions = _pending;
            _pending = 0;
            Fired = true;

            using var landed = command.Connection!.CreateCommand();
            landed.Transaction = command.Transaction;
            landed.CommandText = $"UPDATE \"{target.Groups["table"].Value}\" SET \"UsedCount\" = \"UsedCount\" + {redemptions}";
            landed.ExecuteNonQuery();
        }

        [GeneratedRegex("""^\s*UPDATE\s+"(?<table>[^"]*Promotion)"\s""", RegexOptions.IgnoreCase)]
        private static partial Regex PromotionUpdate();
    }
}
