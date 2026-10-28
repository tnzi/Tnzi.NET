using Tnzi.Payment.Dtos;
using Tnzi.Payment.Services;
using Tnzi.Security.Claims;

namespace Tnzi.Payment.Billing.Tests.Integration;

/// <summary>
/// 票据上的客户（「Received From」/「Bill To」）是计费账户的账单联系人，不是建单那一刻按下付款按钮的人。
/// </summary>
/// <remarks>
/// 消费方实测：Installation 范围下收据印「Received From: admin」。付款与订阅上的名字快照是经办人的登录名，
/// 续费单上它一直是开通订阅那个人。
/// </remarks>
public class InvoiceBillingPartyTests : BillingIntegrationTestBase
{
    private static readonly Guid Manager = Guid.Parse("eeeeeeee-0000-0000-0000-000000000009");

    private BillingAccountProfile? _firmProfile = new()
    {
        DisplayName = "Whitfield & Partners",
        Company = "Whitfield & Partners LLP",
        Address = "100 King St W\nToronto ON M5X 1A9",
        TaxId = "123456789 RT0001",
        Emails = ["billing@whitfield.example"]
    };

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        AddRepo<Tnzi.Payment.Entities.BillingAccount>(services);
        services.AddScoped<IBillingAccountService, BillingAccountService>();
        services.AddScoped<IBillingAccountProfileProvider>(_ => new FirmProfile(() => _firmProfile));
        services.AddSingleton<IUserDisplayNameProvider>(new FixedName(Manager, "Dana Whitfield"));
    }

    private async Task<Invoice> IssueForFirmAsync()
    {
        var payment = SucceededPayment(113m);
        payment.BusinessType = BusinessType.Subscription;
        payment.UserId = Manager;
        payment.CustomerName = "admin";
        payment.BillingAccountType = BillingAccountTypes.Installation;
        payment.BillingAccountId = "default";
        await SeedAsync(payment);

        var issued = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(s => s.CreateFromPaymentAsync(payment.Id));
        issued.Succeeded.ShouldBeTrue(issued.Message);
        return (await ReloadAsync<Invoice>(issued.Data!.Id))!;
    }

    [Fact]
    public async Task AFirmsReceipt_IsMadeOutToTheFirmTheConsumerDescribes()
    {
        var invoice = await IssueForFirmAsync();

        invoice.CustomerName.ShouldBe("Whitfield & Partners");
        invoice.CustomerCompany.ShouldBe("Whitfield & Partners LLP");
        invoice.CustomerAddress.ShouldBe("100 King St W\nToronto ON M5X 1A9");
        invoice.CustomerTaxId.ShouldBe("123456789 RT0001");
        // 付款上已有邮箱快照时收件人不变
        invoice.CustomerEmail.ShouldBe("ada@example.com");
    }

    [Fact]
    public async Task WhenTheConsumerDoesNotKnowTheAccount_ThePayersDisplayNameIsUsed_NotTheLoginName()
    {
        _firmProfile = null;

        var invoice = await IssueForFirmAsync();

        invoice.CustomerName.ShouldBe("Dana Whitfield");
        invoice.CustomerCompany.ShouldBeNull();
        invoice.CustomerAddress.ShouldBeNull();
    }

    [Fact]
    public async Task WhatTheCallerPutsOnTheRequest_StillWins()
    {
        var payment = SucceededPayment(113m);
        payment.UserId = Manager;
        payment.BillingAccountType = BillingAccountTypes.Installation;
        payment.BillingAccountId = "default";
        await SeedAsync(payment);

        var issued = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(s => s.CreateFromPaymentAsync(payment.Id, new CreatePaymentInvoiceDto
        {
            CustomerName = "Named by the caller",
            CustomerCompany = "Caller Co"
        }));

        issued.Data!.CustomerName.ShouldBe("Named by the caller");
        issued.Data.CustomerCompany.ShouldBe("Caller Co");
        issued.Data.CustomerAddress.ShouldBe("100 King St W\nToronto ON M5X 1A9");
    }

    [Fact]
    public async Task AnOverlongProfileValue_IsCutToTheColumn_SoTheReceiptIsStillIssued()
    {
        _firmProfile = new BillingAccountProfile { DisplayName = new string('W', 300), Address = new string('A', 900) };

        var invoice = await IssueForFirmAsync();

        invoice.CustomerName.Length.ShouldBe(Invoice.CustomerNameMaxLength);
        invoice.CustomerAddress!.Length.ShouldBe(Invoice.CustomerAddressMaxLength);
    }

    private sealed class FirmProfile(Func<BillingAccountProfile?> profile) : IBillingAccountProfileProvider
    {
        public Task<BillingAccountProfile?> GetProfileAsync(BillingAccountRef account, CancellationToken cancellationToken = default)
            => Task.FromResult(account.Type == BillingAccountTypes.Installation ? profile() : null);
    }

    private sealed class FixedName(Guid userId, string name) : IUserDisplayNameProvider
    {
        public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(userIds.Contains(userId) ? new Dictionary<Guid, string> { [userId] = name } : []);
    }
}
