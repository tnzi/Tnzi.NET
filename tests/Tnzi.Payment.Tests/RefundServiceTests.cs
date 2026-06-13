using System.Linq.Expressions;
using Mapster;
using MapsterMapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
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
/// RefundService 单元测试
/// </summary>
public class RefundServiceTests
{
    private readonly Mock<IRepository<Refund, Guid>> _refundRepositoryMock;
    private readonly Mock<IRepository<PaymentEntity, Guid>> _paymentRepositoryMock;
    private readonly Mock<IPaymentProviderFactory> _providerFactoryMock;
    private readonly Mock<IOptionsMonitor<PaymentOptions>> _optionsMock;
    private readonly RefundService _service;

    public RefundServiceTests()
    {
        // Initialize Mapster
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        _refundRepositoryMock = new Mock<IRepository<Refund, Guid>>();
        _paymentRepositoryMock = new Mock<IRepository<PaymentEntity, Guid>>();
        _providerFactoryMock = new Mock<IPaymentProviderFactory>();
        _optionsMock = new Mock<IOptionsMonitor<PaymentOptions>>();

        _optionsMock.Setup(x => x.CurrentValue).Returns(new PaymentOptions
        {
            EnableRefundApproval = true,
            RefundApprovalThreshold = 1000m,
            MaxRefundAmountPerDay = 10000m
        });

        // 设置 IServiceProvider mock
        var serviceProviderMock = new Mock<IServiceProvider>();
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactoryMock.Object);

        _service = new RefundService(
            _refundRepositoryMock.Object,
            _paymentRepositoryMock.Object,
            _providerFactoryMock.Object,
            _optionsMock.Object,
            serviceProviderMock.Object
        );
    }

    #region GetRefundAsync Tests

    [Fact]
    public async Task GetRefundAsync_WithExistingRefund_ReturnsSuccess()
    {
        // Arrange
        var refundId = Guid.NewGuid();
        var refund = new Refund
        {
            Id = refundId,
            RefundNo = "REF001",
            RefundAmount = 50m,
            Status = RefundStatus.Pending,
            Reason = "Test refund",
            BusinessOrderNo = "ORDER001"
        };

        _refundRepositoryMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<Refund, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(refund);

        // Act
        var result = await _service.GetRefundAsync(refundId);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.RefundNo.ShouldBe("REF001");
    }

    [Fact]
    public async Task GetRefundAsync_WithNonExistingRefund_Returns404()
    {
        // Arrange
        _refundRepositoryMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<Refund, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Refund?)null);

        // Act
        var result = await _service.GetRefundAsync(Guid.NewGuid());

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task GetRefundAsync_WithOwnerConstraint_Returns404ForNonOwner()
    {
        // Arrange
        var refundId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();

        // 当使用 ownerUserId 过滤时，非所有者查询返回 null
        _refundRepositoryMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<Refund, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Refund?)null);

        // Act
        var result = await _service.GetRefundAsync(refundId, ownerId);

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    #endregion

    #region CreateRefundAsync Tests

    [Fact]
    public async Task CreateRefundAsync_WithNonExistingPayment_Returns404()
    {
        // Arrange
        var request = new CreateRefundDto
        {
            TradeNo = "NONEXIST",
            RefundAmount = 50m,
            Reason = "Test"
        };

        _paymentRepositoryMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<PaymentEntity, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentEntity?)null);

        // Act
        var result = await _service.CreateRefundAsync(request);

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task CreateRefundAsync_WithPendingPayment_ReturnsFailure()
    {
        // Arrange
        var request = new CreateRefundDto
        {
            TradeNo = "PAY001",
            RefundAmount = 50m,
            Reason = "Test"
        };

        var payment = new PaymentEntity
        {
            TradeNo = "PAY001",
            Status = PaymentStatus.Pending,
            PaidAmount = 100m
        };

        _paymentRepositoryMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<PaymentEntity, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        // Act
        var result = await _service.CreateRefundAsync(request);

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe(ErrorCodes.PaymentCannotRefund);
    }

    #endregion
}
