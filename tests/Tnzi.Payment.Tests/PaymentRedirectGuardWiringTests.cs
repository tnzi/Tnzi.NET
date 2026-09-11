using Mapster;
using MapsterMapper;
using Microsoft.Extensions.Logging;
using Tnzi.Domain.Repositories;
using Tnzi.Mapster;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Entities;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Options;
using Tnzi.Payment.Providers;
using Tnzi.Payment.Services;
using Tnzi.Results;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Tests;

/// <summary>
/// 回跳地址守卫真的接在两条入口上。
/// </summary>
/// <remarks>
/// <see cref="PaymentRedirectPolicyTests"/> 测的是判定本身 —— 一个纯函数全绿，
/// 完全不能说明它被谁调用过。这里走的是服务方法，断言坏地址在**建单**与**绑卡**
/// 两条路径上都被拦下、且没有任何东西被交给渠道。
/// </remarks>
public class PaymentRedirectGuardWiringTests
{
    private readonly Mock<IRepository<PaymentEntity, Guid>> _paymentRepositoryMock = new();
    private readonly Mock<IRepository<StoredPaymentMethod, Guid>> _methodRepositoryMock = new();
    private readonly Mock<IPaymentProviderFactory> _providerFactoryMock = new();
    private readonly Mock<IPaymentProvider> _providerMock = new();
    private readonly Mock<IOptionsMonitor<PaymentOptions>> _optionsMock = new();
    private readonly PaymentService _paymentService;
    private readonly PaymentMethodService _methodService;

    public PaymentRedirectGuardWiringTests()
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));

        _optionsMock.Setup(x => x.CurrentValue).Returns(new PaymentOptions
        {
            DefaultCurrency = "USD",
            DefaultChannelCode = "Null",
            AllowedRedirectHosts = ["shop.example.com"]
        });

        var serviceProviderMock = new Mock<IServiceProvider>();
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactoryMock.Object);

        var taxOptionsMock = new Mock<IOptionsMonitor<TaxOptions>>();
        taxOptionsMock.Setup(x => x.CurrentValue).Returns(new TaxOptions());

        _providerFactoryMock.Setup(f => f.GetProvider(It.IsAny<string>())).Returns(_providerMock.Object);
        _providerMock.Setup(p => p.IsSupported(It.IsAny<PaymentMethod>())).Returns(true);
        _providerMock.Setup(p => p.SupportsPaymentMethodStorage).Returns(true);
        _providerMock.Setup(p => p.CreatePaymentAsync(It.IsAny<PaymentProviderCreateDto>()))
            .ReturnsAsync(Result.Success(new PaymentProviderOrderResult { TradeNo = "PAY1" }));
        _providerMock.Setup(p => p.CreateSetupSessionAsync(It.IsAny<PaymentProviderSetupDto>()))
            .ReturnsAsync(Result.Success(new PaymentProviderSetupResult { SetupId = "seti_1" }));

        _paymentRepositoryMock
            .Setup(r => r.InsertAsync(It.IsAny<PaymentEntity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _paymentRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<PaymentEntity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _paymentService = new PaymentService(
            _paymentRepositoryMock.Object,
            _providerFactoryMock.Object,
            new DefaultPaymentTaxCalculator(taxOptionsMock.Object),
            new Mock<IPaymentMethodService>().Object,
            _optionsMock.Object,
            serviceProviderMock.Object);

        _methodService = new PaymentMethodService(
            _methodRepositoryMock.Object,
            _providerFactoryMock.Object,
            _optionsMock.Object,
            serviceProviderMock.Object);
    }

    private static CreatePaymentDto Order(string? returnUrl) => new()
    {
        BusinessOrderNo = "ORDER001",
        BusinessType = BusinessType.Order,
        Amount = 100m,
        Currency = "USD",
        ReturnUrl = returnUrl
    };

    [Fact]
    public async Task CreatePayment_WithAForeignReturnUrl_IsRefusedAndNothingReachesTheChannel()
    {
        var result = await _paymentService.CreatePaymentAsync(Order("https://evil.example.net/done"));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message.ShouldBe(ErrorCodes.PaymentReturnUrlNotAllowed);
        _providerMock.Verify(p => p.CreatePaymentAsync(It.IsAny<PaymentProviderCreateDto>()), Times.Never);
    }

    [Fact]
    public async Task CreatePayment_WithAnAllowedReturnUrl_GoesThrough()
    {
        (await _paymentService.CreatePaymentAsync(Order("https://shop.example.com/done"))).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateSetupSession_WithAForeignReturnUrl_IsRefused()
    {
        var result = await _methodService.CreateSetupSessionAsync(
            Guid.NewGuid(), new CreateSetupSessionDto { ReturnUrl = "https://evil.example.net/bound" });

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe(ErrorCodes.PaymentReturnUrlNotAllowed);
        _providerMock.Verify(p => p.CreateSetupSessionAsync(It.IsAny<PaymentProviderSetupDto>()), Times.Never);
    }

    [Fact]
    public async Task CreateSetupSession_WithAForeignCancelUrl_IsRefused()
    {
        // 取消地址同样把付款人送出去 —— 只挡 ReturnUrl 等于把整扇门留了一条缝
        var result = await _methodService.CreateSetupSessionAsync(
            Guid.NewGuid(),
            new CreateSetupSessionDto
            {
                ReturnUrl = "https://shop.example.com/bound",
                CancelUrl = "https://evil.example.net/cancelled"
            });

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe(ErrorCodes.PaymentReturnUrlNotAllowed);
        _providerMock.Verify(p => p.CreateSetupSessionAsync(It.IsAny<PaymentProviderSetupDto>()), Times.Never);
    }

    // 绑卡的「放行」路径不在这里断言：它随后要读支付方式仓储，用 mock 走不到底。
    // 放行由 CreatePayment_WithAnAllowedReturnUrl_GoesThrough 与
    // PaymentMethodIntegrationTests 覆盖；这里只钉住「守卫在不在」。
}
