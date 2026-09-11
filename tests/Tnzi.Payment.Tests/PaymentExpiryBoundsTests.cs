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
/// 调用方指定的订单有效期必须落在合理区间内。
/// </summary>
/// <remarks>
/// <c>ResolveExpireTime</c> 此前对 <c>ExpireMinutes</c> 一个字都不校验，直接
/// <c>DateTime.UtcNow.AddMinutes(值)</c>。两端各有一个真实后果：
/// <c>int.MinValue</c> 让 <c>AddMinutes</c> 抛 <c>ArgumentOutOfRangeException</c>，
/// 一个畸形请求换来 500；而一个足够大的值（例如一百年）让订单**永不过期** ——
/// 过期清扫扫不到它，为它核销掉的优惠券也就永远不会归还。
/// </remarks>
public class PaymentExpiryBoundsTests
{
    private readonly Mock<IRepository<PaymentEntity, Guid>> _paymentRepositoryMock = new();
    private readonly Mock<IPaymentProviderFactory> _providerFactoryMock = new();
    private readonly Mock<IPaymentProvider> _providerMock = new();
    private readonly Mock<IOptionsMonitor<PaymentOptions>> _optionsMock = new();
    private readonly PaymentService _service;
    private PaymentEntity? _inserted;

    public PaymentExpiryBoundsTests()
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));

        _optionsMock.Setup(x => x.CurrentValue).Returns(new PaymentOptions
        {
            DefaultCurrency = "USD",
            DefaultChannelCode = "Null",
            AutoCloseExpireMinutes = 30
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
            .ReturnsAsync(Result.Success(new PaymentProviderOrderResult { TradeNo = "PAY1" }));

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

    private static CreatePaymentDto Request(int? expireMinutes) => new()
    {
        BusinessOrderNo = "ORDER001",
        BusinessType = BusinessType.Order,
        Amount = 100m,
        Currency = "USD",
        ExpireMinutes = expireMinutes
    };

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    [InlineData(PaymentConstants.MaxPaymentExpireMinutes + 1)]
    public async Task CreatePayment_WithAnOutOfRangeExpiry_IsRejectedWithFourHundred(int expireMinutes)
    {
        var result = await _service.CreatePaymentAsync(Request(expireMinutes));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message.ShouldBe(ErrorCodes.PaymentInvalidExpireMinutes);
        _inserted.ShouldBeNull();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(PaymentConstants.MaxPaymentExpireMinutes)]
    public async Task CreatePayment_WithAnInRangeExpiry_IsAccepted(int expireMinutes)
    {
        var result = await _service.CreatePaymentAsync(Request(expireMinutes));

        result.Succeeded.ShouldBeTrue();
        _inserted!.ExpireTime!.Value.ShouldBeGreaterThan(DateTime.UtcNow);
    }

    [Fact]
    public async Task CreatePayment_WithNoExpirySpecified_FallsBackToTheConfiguredWindow()
    {
        var result = await _service.CreatePaymentAsync(Request(null));

        result.Succeeded.ShouldBeTrue();
        _inserted!.ExpireTime!.Value.ShouldBeLessThan(DateTime.UtcNow.AddMinutes(31));
    }
}
