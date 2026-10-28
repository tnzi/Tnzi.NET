using Tnzi.Payment.Options;

namespace Tnzi.Payment.Promotions.Tests.Integration;

/// <summary>
/// 不带币种创建的促销落在本部署配置的默认币种（<c>Payment:DefaultCurrency</c>）上。
/// </summary>
/// <remarks>
/// ★ 此前 <see cref="CreatePromotionDto.Currency"/> 的初始值是常量 <c>"USD"</c>：配了 CAD 的商户在管理端建的
/// 固定金额券一律绑在 USD 上，而结账按 CAD 校验币种，每一次输码都被 <c>COUPON_SCOPE_MISMATCH</c> 拒绝，
/// 列表上看起来却一切正常。与 <c>CreatePaymentDto.Currency</c> 同一条理由：带初始值时
/// 「调用方没指定」与「调用方指定了 USD」无法区分，回退到配置的那一支永远走不到。
/// </remarks>
public class PromotionCurrencyIntegrationTests : PromotionsIntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.Configure<PaymentOptions>(o => o.DefaultCurrency = "CAD");
    }

    private static CreatePromotionDto FixedFive(string code, string? currency)
    {
        var dto = new CreatePromotionDto
        {
            PromotionCode = code,
            Name = code,
            Type = PromotionType.FixedAmountDiscount,
            DiscountType = DiscountType.Fixed,
            DiscountValue = 5m,
            StartTime = DateTime.UtcNow.AddDays(-1)
        };
        if (currency != null)
            dto.Currency = currency;
        return dto;
    }

    private Task<Result<PromotionDto>> CreateAsync(CreatePromotionDto dto) =>
        InScopeAsync<IPromotionService, Result<PromotionDto>>(svc => svc.CreateAsync(dto));

    private Task<Result<CouponValidationResultDto>> ValidateAsync(string code, string currency) =>
        InScopeAsync<IPromotionService, Result<CouponValidationResultDto>>(svc => svc.ValidateCouponAsync(new CouponApplyContext
        {
            CouponCode = code,
            UserId = Guid.NewGuid(),
            OrderAmount = 50m,
            Currency = currency
        }));

    [Fact]
    public async Task Create_WithoutCurrency_BindsTheDeploymentDefault_AndAnOrderInThatCurrencyCanUseIt()
    {
        var created = await CreateAsync(FixedFive("SAVE5", currency: null));

        created.Succeeded.ShouldBeTrue(created.Message);
        created.Data!.Currency.ShouldBe("CAD");

        var validation = await ValidateAsync("SAVE5", "CAD");
        validation.Data!.IsValid.ShouldBeTrue(validation.Data.ErrorMessage);
        validation.Data.DiscountAmount.ShouldBe(5m);
    }

    [Fact]
    public async Task Create_WithBlankCurrency_BindsTheDeploymentDefault()
    {
        var created = await CreateAsync(FixedFive("BLANK5", currency: "  "));

        created.Succeeded.ShouldBeTrue(created.Message);
        created.Data!.Currency.ShouldBe("CAD");
    }

    /// <summary>
    /// 指定的币种按 ISO 4217 的规范写法落库：结账侧按不区分大小写比较，但列表、渠道同步与报表读的是原样。
    /// </summary>
    [Fact]
    public async Task Create_WithCurrency_StoresItInCanonicalForm()
    {
        var created = await CreateAsync(FixedFive("EURO5", currency: " eur "));

        created.Succeeded.ShouldBeTrue(created.Message);
        created.Data!.Currency.ShouldBe("EUR");
    }
}
