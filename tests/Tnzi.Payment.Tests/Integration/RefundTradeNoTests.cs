using Mapster;
using MapsterMapper;
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
/// 单条退款的答复带着它所属支付的流水号（<c>RefundDto.TradeNo</c>）。
/// </summary>
/// <remarks>
/// 这个字段经 <c>Refund.Payment</c> 导航映射；列表走 SQL 投影所以一直有值，而建退款与按 Id 读退款映射的是没有加载导航的实体，
/// 四个端点（用户端与管理端的建与读）此前一律答 <c>"tradeNo": null</c>，详情页链接不回它的支付。
/// </remarks>
public class RefundTradeNoTests : PaymentIntegrationTestBase
{
    /// <summary>
    /// 跑<b>真实的</b> <c>PaymentMappingConfig</c>（基类给的是空映射配置，那样列表投影也拿不到 TradeNo）。
    /// 上下文实现是 Tnzi.Mapster 的 internal 类型，故反射构造；只在当前异步流里生效，不影响并行的别的测试类。
    /// </summary>
    private static IDisposable UseModuleMappings()
    {
        var config = new TypeAdapterConfig();
        var contextType = typeof(MapsterModule).Assembly.GetType("Tnzi.Mapster.Adapters.MapsterMappingConfigContext")!;
        new PaymentMappingConfig().Configure((IMappingConfigContext)Activator.CreateInstance(contextType, config)!);
        return MapperExtensions.PushMapper(new Mapper(config), config);
    }

    private async Task SeedSucceededPaymentAsync(string tradeNo)
    {
        await SeedAsync(new PaymentEntity
        {
            TradeNo = tradeNo,
            BusinessOrderNo = "ORDER-" + tradeNo,
            BusinessType = BusinessType.Order,
            OriginalAmount = 100m,
            PayableAmount = 100m,
            PaidAmount = 100m,
            Currency = "USD",
            Status = PaymentStatus.Succeeded,
            ChannelCode = "Null",
            PaymentMethod = PaymentMethod.CreditCard,
            PaidTime = DateTime.UtcNow
        });
    }

    [Fact]
    public async Task CreatingARefund_AnswersWithThePaymentsTradeNo()
    {
        using var _ = UseModuleMappings();
        await SeedSucceededPaymentAsync("PAY-REFUND-TN1");

        var created = await InScopeAsync<IRefundService, Result<RefundDto>>(svc => svc.CreateRefundAsync(
            new CreateRefundDto { TradeNo = "PAY-REFUND-TN1", RefundAmount = 40m, Reason = "partial" }));

        created.Succeeded.ShouldBeTrue(created.Message);
        created.Data!.TradeNo.ShouldBe("PAY-REFUND-TN1");
    }

    [Fact]
    public async Task ReadingARefund_AnswersWithThePaymentsTradeNo()
    {
        using var _ = UseModuleMappings();
        await SeedSucceededPaymentAsync("PAY-REFUND-TN2");
        var created = await InScopeAsync<IRefundService, Result<RefundDto>>(svc => svc.CreateRefundAsync(
            new CreateRefundDto { TradeNo = "PAY-REFUND-TN2", RefundAmount = 40m, Reason = "partial" }));
        created.Succeeded.ShouldBeTrue(created.Message);

        var read = await InScopeAsync<IRefundService, Result<RefundDto>>(svc => svc.GetRefundAsync(created.Data!.Id));

        read.Succeeded.ShouldBeTrue(read.Message);
        read.Data!.TradeNo.ShouldBe("PAY-REFUND-TN2");
        read.Data.RefundNo.ShouldBe(created.Data!.RefundNo);
    }

    [Fact]
    public async Task ReadingSomeoneElsesRefund_IsNotFound()
    {
        await SeedSucceededPaymentAsync("PAY-REFUND-TN3");
        var created = await InScopeAsync<IRefundService, Result<RefundDto>>(svc => svc.CreateRefundAsync(
            new CreateRefundDto { TradeNo = "PAY-REFUND-TN3", RefundAmount = 40m, Reason = "partial" }));
        created.Succeeded.ShouldBeTrue(created.Message);

        var read = await InScopeAsync<IRefundService, Result<RefundDto>>(svc => svc.GetRefundAsync(created.Data!.Id, Guid.NewGuid()));

        read.Succeeded.ShouldBeFalse();
        read.Code.ShouldBe(404);
    }
}
