using Mapster;
using MapsterMapper;
using Tnzi.Data;
using Tnzi.Mapping;
using Tnzi.Mapster;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Mappings;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Services;
using Tnzi.Results;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// 平台管理端按计费账户查付款与退款：列表行带着账户（种类 + Id），并能按它筛选。
/// </summary>
/// <remarks>
/// 平台运营者要回答的是「这家机构付过什么、退过什么」，而一家机构的付款可能是任何一位管理者点的：
/// 按经办人 <c>UserId</c> 筛只能拼出一部分。
/// </remarks>
public class AdminBillingAccountFilterTests : PaymentIntegrationTestBase
{
    private static readonly BillingAccountRef Firm = new(BillingAccountTypes.Installation, "firm-a");
    private static readonly BillingAccountRef OtherFirm = new(BillingAccountTypes.Installation, "firm-b");

    private static IDisposable UseModuleMappings()
    {
        var config = new TypeAdapterConfig();
        var contextType = typeof(MapsterModule).Assembly.GetType("Tnzi.Mapster.Adapters.MapsterMappingConfigContext")!;
        new PaymentMappingConfig().Configure((IMappingConfigContext)Activator.CreateInstance(contextType, config)!);
        return MapperExtensions.PushMapper(new Mapper(config), config);
    }

    private Task SeedPaymentAsync(string tradeNo, BillingAccountRef account) => SeedAsync(new PaymentEntity
    {
        TradeNo = tradeNo,
        BusinessOrderNo = "ORDER-" + tradeNo,
        BusinessType = BusinessType.Order,
        OriginalAmount = 100m,
        PayableAmount = 100m,
        PaidAmount = 100m,
        Currency = "CAD",
        Status = PaymentStatus.Succeeded,
        ChannelCode = "Null",
        PaymentMethod = PaymentMethod.CreditCard,
        PaidTime = DateTime.UtcNow,
        UserId = Guid.NewGuid(),
        BillingAccountType = account.Type,
        BillingAccountId = account.Id
    });

    [Fact]
    public async Task PaymentList_CarriesTheAccount_AndFiltersByIt()
    {
        using var _ = UseModuleMappings();
        await SeedPaymentAsync("PAY-ACCT-1", Firm);
        await SeedPaymentAsync("PAY-ACCT-2", Firm);
        await SeedPaymentAsync("PAY-ACCT-3", OtherFirm);

        var list = await InScopeAsync<IPaymentService, Result<IPagedList<PaymentDto>>>(svc => svc.GetPaymentListAsync(new PaymentQueryDto
        {
            BillingAccountType = Firm.Type,
            BillingAccountId = Firm.Id
        }));

        list.Succeeded.ShouldBeTrue(list.Message);
        list.Data!.Items.Select(p => p.TradeNo).OrderBy(t => t).ShouldBe(["PAY-ACCT-1", "PAY-ACCT-2"]);
        list.Data.Items.ShouldAllBe(p => p.BillingAccountType == Firm.Type && p.BillingAccountId == Firm.Id);
    }

    [Fact]
    public async Task RefundList_CarriesThePaymentsAccount_AndFiltersByIt()
    {
        using var _ = UseModuleMappings();
        await SeedPaymentAsync("PAY-ACCT-R1", Firm);
        await SeedPaymentAsync("PAY-ACCT-R2", OtherFirm);
        foreach (var tradeNo in new[] { "PAY-ACCT-R1", "PAY-ACCT-R2" })
        {
            var created = await InScopeAsync<IRefundService, Result<RefundDto>>(svc => svc.CreateRefundAsync(
                new CreateRefundDto { TradeNo = tradeNo, RefundAmount = 10m, Reason = "service credit" }));
            created.Succeeded.ShouldBeTrue(created.Message);
        }

        var list = await InScopeAsync<IRefundService, Result<IPagedList<RefundDto>>>(svc => svc.GetRefundListAsync(new RefundQueryDto
        {
            BillingAccountId = Firm.Id
        }));

        list.Succeeded.ShouldBeTrue(list.Message);
        var refund = list.Data!.Items.ShouldHaveSingleItem();
        refund.TradeNo.ShouldBe("PAY-ACCT-R1");
        refund.BillingAccountType.ShouldBe(Firm.Type);
        refund.BillingAccountId.ShouldBe(Firm.Id);
    }
}
