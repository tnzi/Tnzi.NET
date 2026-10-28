using Tnzi.Payment.Dtos;
using Tnzi.Payment.Services;

namespace Tnzi.Payment.Billing.Tests.Integration;

/// <summary>
/// 票据收件人的兜底：付款上没有邮箱时，开票与发送都向计费账户服务再问一次
/// （账户的账单联系人，没有就是付款人账号上的邮箱）。
/// </summary>
/// <remarks>
/// 消费方实测：首期收据开出时日志「has no customer email」，发送答 <c>INVOICE_RECIPIENT_EMAIL_REQUIRED</c>，
/// 一家刚开通的机构拿不到收据。已经这样开出去的收据，修复之后再发一次就该寄得出去。
/// </remarks>
public class InvoiceRecipientFallbackTests : BillingIntegrationTestBase
{
    private static readonly Guid Manager = Guid.Parse("eeeeeeee-0000-0000-0000-000000000005");

    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<IBillingAccountService> _accounts = new();
    private readonly List<CreateNotificationRequest> _sent = [];

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        _notifications
            .Setup(n => n.CreateAndSendAsync(It.IsAny<CreateNotificationRequest>(), It.IsAny<CancellationToken>()))
            .Callback((CreateNotificationRequest r, CancellationToken _) => _sent.Add(r))
            .ReturnsAsync(Result.Success(new NotificationInfo()));
        services.AddScoped(_ => _notifications.Object);

        _accounts
            .Setup(a => a.ResolveContactAsync(It.IsAny<BillingAccountRef?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BillingAccountRef? _, string? _, string? _, Guid? userId, CancellationToken _) =>
                new BillingContact(null, userId == Manager ? ["manager@firm.example"] : []));
        services.AddScoped(_ => _accounts.Object);
    }

    private async Task<PaymentEntity> SeedPaymentAsync()
    {
        var payment = new PaymentEntity
        {
            TradeNo = $"PAY{Guid.NewGuid():N}"[..20],
            BusinessOrderNo = "SUB-1",
            BusinessType = BusinessType.Subscription,
            ChannelCode = "Null",
            OriginalAmount = 100m,
            PayableAmount = 100m,
            PaidAmount = 100m,
            Currency = "CAD",
            Status = PaymentStatus.Succeeded,
            PaidTime = DateTime.UtcNow,
            UserId = Manager,
            CustomerName = "Northwind",
            CustomerEmail = null
        };
        await SeedAsync(payment);
        return payment;
    }

    [Fact]
    public async Task AReceiptForAPaymentWithoutAnEmail_IsAddressedToThePayersAccountEmail()
    {
        var payment = await SeedPaymentAsync();

        var issued = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(s => s.CreateFromPaymentAsync(payment.Id));

        issued.Succeeded.ShouldBeTrue(issued.Message);
        (await ReloadAsync<Invoice>(issued.Data!.Id))!.CustomerEmail.ShouldBe("manager@firm.example");
    }

    [Fact]
    public async Task AReceiptAlreadyIssuedWithoutARecipient_IsSentOnceOneCanBeFound_AndRemembersIt()
    {
        var payment = await SeedPaymentAsync();
        var invoice = new Invoice
        {
            InvoiceNo = Invoice.GenerateInvoiceNo(),
            PaymentId = payment.Id,
            Type = InvoiceType.Receipt,
            Status = InvoiceStatus.Paid,
            Amount = 100m,
            DueAmount = 100m,
            PaidAmount = 100m,
            Currency = "CAD",
            UserId = Manager,
            CustomerName = "Northwind",
            CustomerEmail = string.Empty,
            InvoiceDate = DateTime.UtcNow
        };
        await SeedAsync(invoice);

        var sent = await InScopeAsync<IPaymentInvoiceService, Result>(s => s.SendAsync(invoice.Id, null));

        sent.Succeeded.ShouldBeTrue(sent.Message);
        _sent.ShouldHaveSingleItem().Recipients.ShouldHaveSingleItem().Address.ShouldBe("manager@firm.example");
        (await ReloadAsync<Invoice>(invoice.Id))!.CustomerEmail.ShouldBe("manager@firm.example");
    }
}
