using System.Reflection;
using Mapster;
using MapsterMapper;
using Microsoft.Extensions.Logging;
using Tnzi.Domain.Repositories;
using Tnzi.Mapster;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Options;
using Tnzi.Payment.Providers;
using Tnzi.Payment.Services;
using Tnzi.Results;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Tests;

/// <summary>
/// 币种回退链：请求指定 &gt; 渠道配置 &gt; 全局默认。
/// </summary>
/// <remarks>
/// 这条链原本整条不可达：<c>CreatePaymentDto.Currency</c> 是非空 <c>string</c> 且带
/// <c>= "USD"</c> 初始值，于是 <c>ResolveCurrency</c> 的 <c>IsNullOrWhiteSpace</c> 判定恒为 false，
/// 后两级从不执行。症状是欧元商户把 <c>Payment:DefaultCurrency</c> 配成 EUR 之后，
/// 前端不带 currency 的每一笔订单仍以 USD 建单并送进渠道 —— 配置、日志与接口返回全都正常。
/// </remarks>
public class PaymentCurrencyResolutionTests
{
    private readonly Mock<IRepository<PaymentEntity, Guid>> _paymentRepositoryMock = new();
    private readonly Mock<IPaymentProviderFactory> _providerFactoryMock = new();
    private readonly Mock<IPaymentProvider> _providerMock = new();
    private readonly Mock<IOptionsMonitor<PaymentOptions>> _optionsMock = new();
    private readonly PaymentService _service;
    private PaymentEntity? _inserted;

    public PaymentCurrencyResolutionTests()
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));

        _optionsMock.Setup(x => x.CurrentValue).Returns(new PaymentOptions
        {
            DefaultCurrency = "EUR",
            DefaultChannelCode = "Stripe",
            Channels = new Dictionary<string, ChannelOptions>
            {
                ["Stripe"] = new() { Enabled = true, Currency = "GBP" },
                ["Offline"] = new() { Enabled = true }
            }
        });

        var serviceProviderMock = new Mock<IServiceProvider>();
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactoryMock.Object);

        var taxOptionsMock = new Mock<IOptionsMonitor<TaxOptions>>();
        taxOptionsMock.Setup(x => x.CurrentValue).Returns(new TaxOptions());

        _providerFactoryMock.Setup(f => f.GetProvider(It.IsAny<string>())).Returns(_providerMock.Object);
        _providerMock.Setup(p => p.IsSupported(It.IsAny<PaymentMethod>())).Returns(true);
        _providerMock.Setup(p => p.CreatePaymentAsync(It.IsAny<PaymentProviderCreateDto>()))
            .ReturnsAsync(Result.Success(new PaymentProviderOrderResult { TradeNo = "PAY123" }));

        _paymentRepositoryMock
            .Setup(r => r.InsertAsync(It.IsAny<PaymentEntity>(), It.IsAny<CancellationToken>()))
            .Callback<PaymentEntity, CancellationToken>((p, _) => _inserted = p)
            .Returns(Task.CompletedTask);
        _paymentRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<PaymentEntity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _service = new PaymentService(
            _paymentRepositoryMock.Object,
            _providerFactoryMock.Object,
            new DefaultPaymentTaxCalculator(taxOptionsMock.Object),
            new Mock<IPaymentMethodService>().Object,
            _optionsMock.Object,
            serviceProviderMock.Object);
    }

    /// <summary>
    /// 省略币种的请求**不赋值该属性** —— 反序列化器对 JSON 里没有的字段就是这样，
    /// 显式写 <c>Currency = null</c> 会绕过属性初始值，把这条测试变成假绿。
    /// </summary>
    private static CreatePaymentDto Request(string? currency, string? channelCode)
    {
        var dto = new CreatePaymentDto
        {
            BusinessOrderNo = "ORDER001",
            BusinessType = BusinessType.Order,
            Amount = 100m,
            ChannelCode = channelCode
        };

        if (currency != null)
            dto.Currency = currency;

        return dto;
    }

    [Fact]
    public async Task CreatePayment_WhenRequestSpecifiesCurrency_UsesIt()
    {
        var result = await _service.CreatePaymentAsync(Request("JPY", "Stripe"));

        result.Succeeded.ShouldBeTrue();
        result.Data!.Currency.ShouldBe("JPY");
        _inserted!.Currency.ShouldBe("JPY");
    }

    [Fact]
    public async Task CreatePayment_WhenCurrencyOmitted_FallsBackToChannelCurrency()
    {
        var result = await _service.CreatePaymentAsync(Request(null, "Stripe"));

        result.Succeeded.ShouldBeTrue();
        result.Data!.Currency.ShouldBe("GBP");
        _inserted!.Currency.ShouldBe("GBP");
    }

    [Fact]
    public async Task CreatePayment_WhenCurrencyOmittedAndChannelHasNone_FallsBackToDefaultCurrency()
    {
        var result = await _service.CreatePaymentAsync(Request(null, "Offline"));

        result.Succeeded.ShouldBeTrue();
        result.Data!.Currency.ShouldBe("EUR");
        _inserted!.Currency.ShouldBe("EUR");
    }

    [Fact]
    public async Task CreatePayment_WhenCurrencyIsWhitespace_FallsBackRatherThanStoringBlank()
    {
        var result = await _service.CreatePaymentAsync(Request("   ", "Offline"));

        result.Succeeded.ShouldBeTrue();
        _inserted!.Currency.ShouldBe("EUR");
    }

    [Fact]
    public void CreatePaymentDto_Currency_IsOptional()
    {
        // 非空 string + "USD" 初始值 = 回退链整条不可达。契约上它必须是"没填"能被看见的形状。
        var property = typeof(CreatePaymentDto).GetProperty(nameof(CreatePaymentDto.Currency))!;
        new CreatePaymentDto().Currency.ShouldBeNull();
        new NullabilityInfoContext().Create(property).WriteState.ShouldBe(NullabilityState.Nullable);
    }
}
