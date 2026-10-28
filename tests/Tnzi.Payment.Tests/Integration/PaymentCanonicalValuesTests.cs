using Microsoft.Extensions.DependencyInjection;
using Tnzi.Domain.Repositories;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Entities;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Services;
using Tnzi.Results;
using Tnzi.TestBase;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// 渠道代码与币种按规范写法落库，不按调用方的写法。
/// </summary>
/// <remarks>
/// 渠道查找不区分大小写，所以 <c>channelCode: "stripe"</c> 能建单、能绑卡；但存下来的值随后被按等值比较：
/// 撤卡回调按 <c>"Stripe"</c> 找支付方式、订阅按自己的渠道找默认卡、统计按渠道分组。在区分大小写的数据库上（SQLite 默认即是），
/// 以小写绑的卡在渠道侧被删之后本地仍然有效，报表里同一个渠道分成两行。币种同理：PayPal 拒收小写的 <c>usd</c>，
/// 每日退款限额按币种等值统计。
/// </remarks>
public class PaymentCanonicalValuesTests : PaymentIntegrationTestBase
{
    private static readonly Guid UserId = TestHelper.DefaultTestUserId;

    [Fact]
    public async Task CreatingAPayment_StoresTheChannelsOwnSpellingAndAnUpperCaseCurrency()
    {
        var created = await InScopeAsync<IPaymentService, Result<PaymentOrderResultDto>>(svc => svc.CreatePaymentAsync(new CreatePaymentDto
        {
            BusinessOrderNo = "ORDER-CANONICAL",
            BusinessType = BusinessType.Order,
            Amount = 10m,
            Currency = " usd ",
            ChannelCode = "null"
        }));

        created.Succeeded.ShouldBeTrue(created.Message);
        created.Data!.Currency.ShouldBe("USD");

        using var scope = ServiceProvider.CreateScope();
        var payment = await scope.ServiceProvider.GetRequiredService<IRepository<PaymentEntity, Guid>>()
            .FirstOrDefaultAsync(p => p.TradeNo == created.Data.TradeNo);
        payment!.ChannelCode.ShouldBe(PaymentConstants.NullChannelCode);
        payment.Currency.ShouldBe("USD");
    }

    /// <summary>
    /// ★ 以小写渠道代码绑的卡：渠道撤卡回调（按渠道自己的写法查）找得到它并把它置为失效。
    /// </summary>
    [Fact]
    public async Task ACardBoundWithALowerCaseChannel_IsFoundByTheRevocationCallback()
    {
        var bound = await InScopeAsync<IPaymentMethodService, Result<StoredPaymentMethodDto>>(svc => svc.BindAsync(UserId, new BindPaymentMethodDto
        {
            PaymentMethodToken = "pm_lower_case",
            ChannelCode = "null",
            SetAsDefault = true
        }));
        bound.Succeeded.ShouldBeTrue(bound.Message);
        bound.Data!.ChannelCode.ShouldBe(PaymentConstants.NullChannelCode);

        var revoked = await InScopeAsync<IPaymentMethodService, Result>(
            svc => svc.DeactivateByTokenAsync(PaymentConstants.NullChannelCode, "pm_lower_case"));

        revoked.Succeeded.ShouldBeTrue(revoked.Message);
        (await ReloadAsync<StoredPaymentMethod>(bound.Data.Id))!.IsActive.ShouldBeFalse();
    }

    /// <summary>绑卡会话回给客户端的同样是渠道自己的写法（客户端会把它原样传回绑卡）。</summary>
    [Fact]
    public async Task ASetupSession_ReportsTheChannelsOwnSpelling()
    {
        var session = await InScopeAsync<IPaymentMethodService, Result<SetupSessionDto>>(
            svc => svc.CreateSetupSessionAsync(UserId, new CreateSetupSessionDto { ChannelCode = "NULL" }));

        session.Succeeded.ShouldBeTrue(session.Message);
        session.Data!.ChannelCode.ShouldBe(PaymentConstants.NullChannelCode);
    }
}
