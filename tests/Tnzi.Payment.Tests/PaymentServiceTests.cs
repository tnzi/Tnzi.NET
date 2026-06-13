using System.Linq.Expressions;
using Mapster;
using MapsterMapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Tnzi.Caching;
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
/// PaymentService 单元测试
/// </summary>
public class PaymentServiceTests
{
    private readonly Mock<IRepository<PaymentEntity, Guid>> _paymentRepositoryMock;
    private readonly Mock<IPaymentProviderFactory> _providerFactoryMock;
    private readonly Mock<IPaymentProvider> _providerMock;
    private readonly Mock<IOptionsMonitor<PaymentOptions>> _optionsMock;
    private readonly PaymentService _service;

    public PaymentServiceTests()
    {
        // 初始化 Mapster
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        _paymentRepositoryMock = new Mock<IRepository<PaymentEntity, Guid>>();
        _providerFactoryMock = new Mock<IPaymentProviderFactory>();
        _providerMock = new Mock<IPaymentProvider>();
        _optionsMock = new Mock<IOptionsMonitor<PaymentOptions>>();

        _optionsMock.Setup(x => x.CurrentValue).Returns(new PaymentOptions
        {
            DefaultCurrency = "USD",
            AutoCloseExpireMinutes = 30
        });

        // 设置 IServiceProvider mock 以提供 ILoggerFactory
        var serviceProviderMock = new Mock<IServiceProvider>();
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactoryMock.Object);

        _service = new PaymentService(
            _paymentRepositoryMock.Object,
            _providerFactoryMock.Object,
            _optionsMock.Object,
            serviceProviderMock.Object
        );
    }

    #region CreatePaymentAsync Tests

    [Fact]
    public async Task CreatePaymentAsync_WithValidInput_ReturnsSuccess()
    {
        // Arrange
        var request = new CreatePaymentDto
        {
            BusinessOrderNo = "ORDER001",
            BusinessType = BusinessType.Order,
            Amount = 100m,
            Currency = "USD",
            ChannelCode = "Stripe",
            Description = "Test payment"
        };

        _providerFactoryMock.Setup(f => f.GetProvider("Stripe")).Returns(_providerMock.Object);
        _providerMock.Setup(p => p.IsSupported(It.IsAny<PaymentMethod>())).Returns(true);
        _providerMock.Setup(p => p.CreatePaymentAsync(It.IsAny<PaymentProviderCreateDto>()))
            .ReturnsAsync(Result.Success(new PaymentProviderOrderResult
            {
                TradeNo = "PAY123",
                ExternalTradeNo = "ext_123",
                PayUrl = "https://pay.example.com"
            }));

        _paymentRepositoryMock.Setup(r => r.InsertAsync(It.IsAny<PaymentEntity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _paymentRepositoryMock.Setup(r => r.UpdateAsync(It.IsAny<PaymentEntity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.CreatePaymentAsync(request);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Amount.ShouldBe(100m);
        result.Data.Currency.ShouldBe("USD");
        _paymentRepositoryMock.Verify(r => r.InsertAsync(It.IsAny<PaymentEntity>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreatePaymentAsync_WithZeroAmount_ReturnsFailure()
    {
        // Arrange
        var request = new CreatePaymentDto
        {
            BusinessOrderNo = "ORDER001",
            BusinessType = BusinessType.Order,
            Amount = 0m
        };

        // Act
        var result = await _service.CreatePaymentAsync(request);

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task CreatePaymentAsync_WithNegativeAmount_ReturnsFailure()
    {
        // Arrange
        var request = new CreatePaymentDto
        {
            BusinessOrderNo = "ORDER001",
            BusinessType = BusinessType.Order,
            Amount = -10m
        };

        // Act
        var result = await _service.CreatePaymentAsync(request);

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task CreatePaymentAsync_WithUnsupportedChannel_ReturnsFailure()
    {
        // Arrange
        var request = new CreatePaymentDto
        {
            BusinessOrderNo = "ORDER001",
            BusinessType = BusinessType.Order,
            Amount = 100m,
            ChannelCode = "UnknownChannel"
        };

        _providerFactoryMock.Setup(f => f.GetProvider("UnknownChannel")).Returns((IPaymentProvider?)null);

        // Act
        var result = await _service.CreatePaymentAsync(request);

        // Assert
        result.Succeeded.ShouldBeFalse();
    }

    #endregion

    #region ClosePaymentAsync Tests

    [Fact]
    public async Task ClosePaymentAsync_WithPendingPayment_ReturnsSuccess()
    {
        // Arrange
        var tradeNo = "PAY123";
        var payment = new PaymentEntity
        {
            TradeNo = tradeNo,
            Status = PaymentStatus.Pending
        };

        _paymentRepositoryMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<PaymentEntity, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        _paymentRepositoryMock.Setup(r => r.UpdateAsync(It.IsAny<PaymentEntity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.ClosePaymentAsync(tradeNo, "Test close");

        // Assert
        result.Succeeded.ShouldBeTrue();
        payment.Status.ShouldBe(PaymentStatus.Closed);
    }

    [Fact]
    public async Task ClosePaymentAsync_WithSucceededPayment_ReturnsFailure()
    {
        // Arrange
        var tradeNo = "PAY123";
        var payment = new PaymentEntity
        {
            TradeNo = tradeNo,
            Status = PaymentStatus.Succeeded
        };

        _paymentRepositoryMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<PaymentEntity, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        // Act
        var result = await _service.ClosePaymentAsync(tradeNo, "Test close");

        // Assert
        result.Succeeded.ShouldBeFalse();
    }

    #endregion

    #region HandleCallbackAsync Tests

    [Fact]
    public async Task HandleCallbackAsync_WithInvalidSignature_ReturnsFailure()
    {
        // Arrange
        var request = new PaymentCallbackDto
        {
            ChannelCode = "Stripe",
            Parameters = new Dictionary<string, string> { { "key", "value" } }
        };

        _providerFactoryMock.Setup(f => f.GetProvider("Stripe")).Returns(_providerMock.Object);
        _providerMock.Setup(p => p.VerifySignatureAsync(It.IsAny<IDictionary<string, string>>())).ReturnsAsync(false);

        // Act
        var result = await _service.HandleCallbackAsync(request);

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe(ErrorCodes.PaymentInvalidSignature);
    }

    #endregion
}
