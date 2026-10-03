using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ConflictException = Tnzi.Exceptions.ConflictException;

namespace Tnzi.Payment.Promotions.Tests.Integration;

/// <summary>
/// 「不可叠加」在同一业务单号上的两个方向，以及它在并发下的兜底。
/// </summary>
/// <remarks>
/// 规则是双向的：单上只要有一张已生效的券不可叠加，就不能再有第二张；
/// 要用的券不可叠加，而单上已经有任何一张券，也不行。可叠加的券之间照常叠加。
/// 只判「要用的这张」时，先用一张不可叠加的、再用一张可叠加的，两张就同单共存了。
/// </remarks>
public class CouponStackingIntegrationTests : PromotionsIntegrationTestBase
{
    private const string OrderNo = "ORDER-STACK";

    private readonly CompetingInsertInterceptor _competingInsert = new();

    protected override void ConfigureDbContextOptions(DbContextOptionsBuilder options)
    {
        base.ConfigureDbContextOptions(options);
        options.AddInterceptors(_competingInsert);
    }

    private async Task<Promotion> SeedPromotionAsync(string code, bool stackable)
    {
        var promotion = new Promotion
        {
            PromotionCode = code,
            Name = code,
            IsActive = true,
            IsPublic = true,
            StartTime = DateTime.UtcNow.AddDays(-1),
            DiscountType = DiscountType.Percentage,
            DiscountValue = 5m, // 5%
            Currency = "USD",
            Stackable = stackable,
            ProductType = ProductType.All,
            ApplyScope = ApplyScope.Global,
            UsedCount = 0
        };
        await SeedAsync(promotion);
        return promotion;
    }

    private Task<Result<CouponUsageDto>> ApplyAsync(Guid userId, string code, string orderNo = OrderNo) =>
        InScopeAsync<ICouponService, Result<CouponUsageDto>>(svc => svc.ApplyCouponAsync(new CouponApplyContext
        {
            CouponCode = code,
            UserId = userId,
            BusinessOrderNo = orderNo,
            OrderAmount = 100m,
            Currency = "USD",
            ProductType = ProductType.All
        }));

    [Theory]
    [InlineData(false, false, false)] // 不可叠加 + 不可叠加
    [InlineData(false, true, false)]  // 先不可叠加、再可叠加：单上那张不可叠加的券不允许再有第二张
    [InlineData(true, false, false)]  // 先可叠加、再不可叠加：要用的这张不允许与任何券同单
    [InlineData(true, true, true)]    // 可叠加 + 可叠加：照常叠加
    public async Task ASecondCouponOnTheSameOrder_IsAllowedOnlyWhenBothAreStackable(bool firstStackable, bool secondStackable, bool allowed)
    {
        var user = Guid.NewGuid();
        await SeedPromotionAsync("FIRST", firstStackable);
        var second = await SeedPromotionAsync("SECOND", secondStackable);

        (await ApplyAsync(user, "FIRST")).Succeeded.ShouldBeTrue();

        var applied = await ApplyAsync(user, "SECOND");

        applied.Succeeded.ShouldBe(allowed);
        if (allowed)
            return;

        applied.Code.ShouldBe(400);
        applied.ErrorCode.ShouldBe(ErrorCodes.CouponNotStackable);
        applied.Message.ShouldNotBeNullOrWhiteSpace();
        // 被拒的那一次不能烧掉总用量名额，也不能留下核销记录
        (await ReloadAsync<Promotion>(second.Id))!.UsedCount.ShouldBe(0);
        (await CountUsagesAsync(second.Id)).ShouldBe(0);
    }

    /// <summary>不可叠加管的是「同一张单」，另一张单照常可用。</summary>
    [Fact]
    public async Task ANonStackableCoupon_DoesNotBlockADifferentOrder()
    {
        var user = Guid.NewGuid();
        await SeedPromotionAsync("FIRST", stackable: false);
        await SeedPromotionAsync("SECOND", stackable: true);

        (await ApplyAsync(user, "FIRST")).Succeeded.ShouldBeTrue();

        (await ApplyAsync(user, "SECOND", "ORDER-OTHER")).Succeeded.ShouldBeTrue();
    }

    /// <summary>「已生效」指未释放：那张不可叠加的券还回去之后，单上就可以用别的券了。</summary>
    [Fact]
    public async Task OnceTheNonStackableCouponIsReleased_TheOrderAcceptsAnotherCoupon()
    {
        var user = Guid.NewGuid();
        await SeedPromotionAsync("FIRST", stackable: false);
        await SeedPromotionAsync("SECOND", stackable: true);

        var first = await ApplyAsync(user, "FIRST");
        (await InScopeAsync<ICouponService, Result>(svc => svc.ReleaseCouponAsync(first.Data!.Id))).Succeeded.ShouldBeTrue();

        (await ApplyAsync(user, "SECOND")).Succeeded.ShouldBeTrue();
    }

    /// <summary>
    /// 两笔核销在同一张单上并发：各自读到「单上还没有券」，各自放行。
    /// 本用例在我方写入核销记录的前一刻，把另一笔并发核销的那一行插进同一个事务（它读到的券集合与我方相同，
    /// 于是算出同一个槽位）—— 那正是「对方读完之后、我方写入之前对方已落库」的状态。
    /// 槽位唯一索引必须把我方挡下，而且整笔回滚：总用量不能多占一个。
    /// </summary>
    [Fact]
    public async Task TwoConcurrentApplicationsOnTheSameOrder_OnlyOneLands()
    {
        var user = Guid.NewGuid();
        var competitor = await SeedPromotionAsync("COMPETITOR", stackable: true);
        var mine = await SeedPromotionAsync("MINE", stackable: false);

        _competingInsert.Arm(new CouponUsage
        {
            CouponId = competitor.Id,
            UserId = user,
            BusinessOrderNo = OrderNo,
            DiscountAmount = 5m,
            OrderSlot = 0
        });

        var ex = await Should.ThrowAsync<ConflictException>(() => ApplyAsync(user, "MINE"));

        ex.Message.ShouldNotBeNullOrWhiteSpace();
        _competingInsert.Fired.ShouldBeTrue();
        (await ReloadAsync<Promotion>(mine.Id))!.UsedCount.ShouldBe(0);
        (await CountUsagesAsync(mine.Id)).ShouldBe(0);
    }

    /// <summary>顺序叠加的可叠加券依次占 0、1、2 号槽位；释放掉最高那张再用，不撞剩下的。</summary>
    [Fact]
    public async Task SequentialStackableCoupons_TakeIncreasingSlots_AndAReleasedSlotIsNotReusedIntoAConflict()
    {
        var user = Guid.NewGuid();
        await SeedPromotionAsync("A", stackable: true);
        await SeedPromotionAsync("B", stackable: true);
        await SeedPromotionAsync("C", stackable: true);

        var a = await ApplyAsync(user, "A");
        var b = await ApplyAsync(user, "B");
        (await ReloadAsync<CouponUsage>(a.Data!.Id))!.OrderSlot.ShouldBe(0);
        (await ReloadAsync<CouponUsage>(b.Data!.Id))!.OrderSlot.ShouldBe(1);

        (await InScopeAsync<ICouponService, Result>(svc => svc.ReleaseCouponAsync(b.Data!.Id))).Succeeded.ShouldBeTrue();

        var c = await ApplyAsync(user, "C");
        c.Succeeded.ShouldBeTrue();
        (await ReloadAsync<CouponUsage>(c.Data!.Id))!.OrderSlot.ShouldBe(1);
    }

    private async Task<int> CountUsagesAsync(Guid promotionId)
    {
        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PromotionsTestDbContext>();
        return await db.Set<CouponUsage>().CountAsync(c => c.CouponId == promotionId);
    }

    /// <summary>
    /// 在核销记录的 INSERT 真正执行之前，用同一个事务先写入另一笔「并发核销」的那一行。
    /// </summary>
    /// <remarks>
    /// 测试库是单条共享的 SQLite 连接，真开两个线程只会撞连接；借同一个事务写入，
    /// 对我方的 INSERT 来说与「对方已经提交」没有区别：两行争的是同一个唯一键。
    /// </remarks>
    private sealed class CompetingInsertInterceptor : DbCommandInterceptor
    {
        private CouponUsage? _pending;

        public bool Fired { get; private set; }

        public void Arm(CouponUsage competitor) => _pending = competitor;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await InsertCompetitorAsync(command, eventData, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await InsertCompetitorAsync(command, eventData, cancellationToken);
            return result;
        }

        private async Task InsertCompetitorAsync(DbCommand command, CommandEventData eventData, CancellationToken cancellationToken)
        {
            var competitor = _pending;
            if (competitor == null || eventData.Context == null)
                return;

            var table = eventData.Context.Model.FindEntityType(typeof(CouponUsage))!.GetTableName()!;
            if (!command.CommandText.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase)
                || !command.CommandText.Contains($"\"{table}\"", StringComparison.Ordinal))
                return;

            // 先解除再写，否则对方那一行自己的 INSERT 会再次触发这里
            _pending = null;
            Fired = true;

            await using var insert = command.Connection!.CreateCommand();
            insert.Transaction = command.Transaction;
            insert.CommandText =
                $"INSERT INTO \"{table}\" (\"Id\", \"CouponId\", \"UserId\", \"BusinessOrderNo\", \"DiscountAmount\", \"OrderSlot\", \"CreationTime\") " +
                "VALUES ($id, $couponId, $userId, $orderNo, $discount, $slot, $created)";
            AddParameter(insert, "$id", Guid.NewGuid().ToString().ToUpperInvariant());
            AddParameter(insert, "$couponId", competitor.CouponId.ToString().ToUpperInvariant());
            AddParameter(insert, "$userId", competitor.UserId.ToString().ToUpperInvariant());
            AddParameter(insert, "$orderNo", competitor.BusinessOrderNo!);
            AddParameter(insert, "$discount", competitor.DiscountAmount.ToString(CultureInfo.InvariantCulture));
            AddParameter(insert, "$slot", competitor.OrderSlot!.Value);
            AddParameter(insert, "$created", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        private static void AddParameter(DbCommand command, string name, object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
    }
}
