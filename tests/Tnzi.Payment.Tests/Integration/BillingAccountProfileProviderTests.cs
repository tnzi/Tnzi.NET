using Microsoft.Extensions.DependencyInjection;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Services;
using Tnzi.Results;
using Tnzi.Security.Claims;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// 账单联系人的取值顺序：账户自己的资料 → 消费应用的 <see cref="IBillingAccountProfileProvider"/> → 经办用户的显示名与邮箱。
/// </summary>
/// <remarks>
/// 消费方实测：Installation 范围下收据的「Received From」印的是付款人的登录名 <c>admin</c>。
/// 付钱的是机构，机构的名称与账单邮箱就在消费应用自己的设置里。
/// </remarks>
public class BillingAccountProfileProviderTests : PaymentIntegrationTestBase
{
    private static readonly Guid Manager = Guid.Parse("eeeeeeee-0000-0000-0000-000000000007");
    private static readonly BillingAccountRef Firm = new(BillingAccountTypes.Installation, "default");

    private readonly FirmProfile _profiles = new();
    private readonly Dictionary<Guid, string> _displayNames = new() { [Manager] = "Dana Whitfield" };

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddSingleton<IBillingAccountProfileProvider>(_profiles);
        services.AddSingleton<IUserDisplayNameProvider>(new FixedNames(_displayNames));
    }

    private Task<BillingContact> ResolveAsync(BillingAccountRef? account, string? fallbackName = "admin", string? fallbackEmail = "admin@login.example")
        => InScopeAsync<IBillingAccountService, BillingContact>(svc => svc.ResolveContactAsync(account, fallbackName, fallbackEmail, Manager));

    [Fact]
    public async Task WhenTheAccountHasNoProfile_TheConsumersProfileComesBeforeTheUser()
    {
        _profiles.Profile = new BillingAccountProfile
        {
            DisplayName = "Whitfield & Partners",
            Company = "Whitfield & Partners LLP",
            Address = "100 King St W\nToronto ON",
            TaxId = "123456789 RT0001",
            Emails = [" billing@whitfield.example ", "", "BILLING@whitfield.example", "ap@whitfield.example"]
        };

        var contact = await ResolveAsync(Firm);

        contact.Name.ShouldBe("Whitfield & Partners");
        contact.Company.ShouldBe("Whitfield & Partners LLP");
        contact.Address.ShouldBe("100 King St W\nToronto ON");
        contact.TaxId.ShouldBe("123456789 RT0001");
        contact.Emails.ShouldBe(["billing@whitfield.example", "ap@whitfield.example"]);
        _profiles.AskedFor.ShouldBe([Firm]);
    }

    [Fact]
    public async Task WhatTheAccountsOwnProfileSays_StillWins_FieldByField()
    {
        _profiles.Profile = new BillingAccountProfile
        {
            DisplayName = "From the provider",
            Company = "Provider Co",
            Address = "Provider address",
            Emails = ["provider@whitfield.example"]
        };
        await WithAccountProfileAsync(Firm, new BillingAccountDto { DisplayName = "Typed on the billing page", BillingEmails = ["typed@whitfield.example"] });

        var contact = await ResolveAsync(Firm);

        contact.Name.ShouldBe("Typed on the billing page");
        contact.Emails.ShouldBe(["typed@whitfield.example"]);
        // 账户资料里没有的项仍由消费应用补上
        contact.Address.ShouldBe("Provider address");
        contact.Company.ShouldBe("Provider Co");
    }

    [Fact]
    public async Task WhenNobodyKnowsTheAccount_TheUsersDisplayNameIsUsed_NotTheLoginName()
    {
        _profiles.Profile = null;

        var contact = await ResolveAsync(Firm);

        contact.Name.ShouldBe("Dana Whitfield");
        contact.Emails.ShouldBe(["admin@login.example"]);
        contact.Company.ShouldBeNull();
        contact.Address.ShouldBeNull();
    }

    [Fact]
    public async Task AUserWithNoDisplayName_FallsBackToWhatTheCallerGave()
    {
        _profiles.Profile = null;
        _displayNames.Clear();

        (await ResolveAsync(Firm)).Name.ShouldBe("admin");
    }

    [Fact]
    public async Task AProfileWithBlankFields_LeavesThoseFieldsToTheNextSource()
    {
        _profiles.Profile = new BillingAccountProfile { DisplayName = "  ", Emails = ["   "] };

        var contact = await ResolveAsync(Firm);

        contact.Name.ShouldBe("Dana Whitfield");
        contact.Emails.ShouldBe(["admin@login.example"]);
    }

    [Fact]
    public async Task WithoutAnAccount_TheProviderIsNotAsked()
    {
        _profiles.Profile = new BillingAccountProfile { DisplayName = "Should not be used" };

        var contact = await ResolveAsync(null);

        contact.Name.ShouldBe("Dana Whitfield");
        _profiles.AskedFor.ShouldBeEmpty();
    }

    private Task WithAccountProfileAsync(BillingAccountRef account, BillingAccountDto profile)
        => SeedAsync(new Entities.BillingAccount
        {
            AccountType = account.Type,
            AccountId = account.Id,
            DisplayName = profile.DisplayName,
            BillingEmails = string.Join(";", profile.BillingEmails)
        });

    private sealed class FirmProfile : IBillingAccountProfileProvider
    {
        public BillingAccountProfile? Profile { get; set; }

        public List<BillingAccountRef> AskedFor { get; } = [];

        public Task<BillingAccountProfile?> GetProfileAsync(BillingAccountRef account, CancellationToken cancellationToken = default)
        {
            AskedFor.Add(account);
            return Task.FromResult(account == Firm ? Profile : null);
        }
    }

    private sealed class FixedNames(IReadOnlyDictionary<Guid, string> names) : IUserDisplayNameProvider
    {
        public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(userIds.Where(names.ContainsKey).ToDictionary(id => id, id => names[id]));
    }
}
