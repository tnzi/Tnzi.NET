using Microsoft.Extensions.DependencyInjection;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Entities;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Options;
using Tnzi.Payment.Permissions;
using Tnzi.Payment.Services;
using Tnzi.Results;
using Tnzi.Security.Authorization;
using Tnzi.Security.Claims;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// 单客户部署（<c>Payment:BillingAccount:Scope = Installation</c>）：卡、付款、资料都记在整个安装的同一个账户上，
/// 持有 <c>payment.account.manage</c> 的几位管理者共同管理，只持 view 的人只能看，什么都不持的人什么都碰不到。
/// </summary>
public class BillingAccountIntegrationTests : PaymentIntegrationTestBase
{
    private static readonly Guid ManagerA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid ManagerB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid Viewer = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
    private static readonly Guid Staff = Guid.Parse("dddddddd-0000-0000-0000-000000000004");

    private static readonly BillingAccountRef Installation = new(BillingAccountTypes.Installation, "firm-1");

    // 字段初始化先于基类构造函数执行，所以 ConfigureServices 里拿得到它们
    private readonly SwitchableCurrentUser _currentUser = new(ManagerA);

    private readonly GrantingPermissionChecker _permissions = new(new Dictionary<Guid, string[]>
    {
        [ManagerA] = [PaymentPermissions.AccountManage],
        [ManagerB] = [PaymentPermissions.AccountManage],
        [Viewer] = [PaymentPermissions.AccountView]
    });

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.Configure<BillingAccountOptions>(o =>
        {
            o.Scope = BillingAccountScope.Installation;
            o.InstallationAccountId = Installation.Id;
        });
        services.AddScoped<ICurrentUser>(_ => _currentUser);
        services.AddSingleton<IPermissionChecker>(_permissions);
    }

    private Task<Result<StoredPaymentMethodDto>> BindAs(Guid user, string token)
    {
        _currentUser.Id = user;
        return InScopeAsync<IPaymentMethodService, Result<StoredPaymentMethodDto>>(svc => svc.BindAsync(user, new BindPaymentMethodDto
        {
            PaymentMethodToken = token,
            ChannelCode = "Null",
            SetAsDefault = true
        }));
    }

    private Task<Result<List<StoredPaymentMethodDto>>> ListAs(Guid user)
    {
        _currentUser.Id = user;
        return InScopeAsync<IPaymentMethodService, Result<List<StoredPaymentMethodDto>>>(svc => svc.GetUserMethodsAsync(user));
    }

    [Fact]
    public async Task ACardBoundByOneManager_BelongsToTheAccount_AndEveryManagerSeesIt()
    {
        var bound = await BindAs(ManagerA, "pm_firm_1");
        bound.Succeeded.ShouldBeTrue(bound.Message);

        var stored = await ReloadAsync<StoredPaymentMethod>(bound.Data!.Id);
        stored!.BillingAccountType.ShouldBe(BillingAccountTypes.Installation);
        stored.BillingAccountId.ShouldBe(Installation.Id);
        stored.UserId.ShouldBe(ManagerA, "UserId records who entered the card, not who owns it");

        (await ListAs(ManagerB)).Data!.Select(m => m.Id).ShouldBe([bound.Data.Id]);
        (await ListAs(Viewer)).Data!.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ManagerB_CanReplaceTheDefaultCardManagerABound()
    {
        var first = await BindAs(ManagerA, "pm_firm_old");
        var second = await BindAs(ManagerB, "pm_firm_new");

        first.Succeeded.ShouldBeTrue(first.Message);
        second.Succeeded.ShouldBeTrue(second.Message);

        var methods = (await ListAs(ManagerA)).Data!;
        methods.Count.ShouldBe(2);
        methods.Single(m => m.IsDefault).Id.ShouldBe(second.Data!.Id, "one default card per account, whoever bound it");

        _currentUser.Id = ManagerB;
        var removed = await InScopeAsync<IPaymentMethodService, Result>(svc => svc.RemoveAsync(ManagerB, first.Data!.Id));
        removed.Succeeded.ShouldBeTrue(removed.Message);
    }

    [Fact]
    public async Task StaffWithoutAccountPermissions_CanNeitherSeeNorChangeTheAccount()
    {
        await BindAs(ManagerA, "pm_firm_2");

        var list = await ListAs(Staff);
        list.Succeeded.ShouldBeFalse();
        list.Code.ShouldBe(403);
        list.Message.ShouldBe(ErrorCodes.BillingAccountAccessDenied);

        var bind = await BindAs(Staff, "pm_staff");
        bind.Code.ShouldBe(403);
    }

    [Fact]
    public async Task AViewer_CanSeeButNotChange()
    {
        await BindAs(ManagerA, "pm_firm_3");

        (await ListAs(Viewer)).Succeeded.ShouldBeTrue();
        (await BindAs(Viewer, "pm_viewer")).Code.ShouldBe(403);
    }

    [Fact]
    public async Task APaymentMadeByOneManager_IsVisibleToAnother_AndNotToStaff()
    {
        _currentUser.Id = ManagerB;
        var created = await InScopeAsync<IPaymentService, Result<PaymentOrderResultDto>>(svc => svc.CreatePaymentAsync(new CreatePaymentDto
        {
            BusinessOrderNo = "ORDER-1",
            BusinessType = BusinessType.Order,
            Amount = 10m,
            ChannelCode = "Null"
        }));
        created.Succeeded.ShouldBeTrue(created.Message);

        _currentUser.Id = ManagerA;
        var seen = await InScopeAsync<IPaymentService, Result<PaymentDto>>(svc => svc.GetPaymentAsync(created.Data!.TradeNo, ManagerA));
        seen.Succeeded.ShouldBeTrue(seen.Message);
        seen.Data!.UserId.ShouldBe(ManagerB);

        _currentUser.Id = Staff;
        var hidden = await InScopeAsync<IPaymentService, Result<PaymentDto>>(svc => svc.GetPaymentAsync(created.Data!.TradeNo, Staff));
        hidden.Code.ShouldBe(403);

        _currentUser.Id = Staff;
        var staffPays = await InScopeAsync<IPaymentService, Result<PaymentOrderResultDto>>(svc => svc.CreatePaymentAsync(new CreatePaymentDto
        {
            BusinessOrderNo = "ORDER-2",
            BusinessType = BusinessType.Order,
            Amount = 10m,
            ChannelCode = "Null"
        }));
        staffPays.Code.ShouldBe(403, "paying for the account is a manage action");
    }

    [Fact]
    public async Task BillingProfile_IsSharedByManagers_AndUsedAsTheBillingContact()
    {
        _currentUser.Id = ManagerA;
        var updated = await InScopeAsync<IBillingAccountService, Result<BillingAccountDto>>(svc => svc.UpdateCurrentAsync(ManagerA, new UpdateBillingAccountDto
        {
            DisplayName = "Smith & Partners LLP",
            BillingEmails = ["billing@smith.example", "partner@smith.example", "BILLING@smith.example"],
            TaxId = "123456789RT0001"
        }));
        updated.Succeeded.ShouldBeTrue(updated.Message);
        updated.Data!.BillingEmails.ShouldBe(["billing@smith.example", "partner@smith.example"]);

        _currentUser.Id = Viewer;
        var viewed = await InScopeAsync<IBillingAccountService, Result<BillingAccountDto>>(svc => svc.GetCurrentAsync(Viewer));
        viewed.Data!.DisplayName.ShouldBe("Smith & Partners LLP");
        viewed.Data.CanManage.ShouldBeFalse();

        var viewerEdit = await InScopeAsync<IBillingAccountService, Result<BillingAccountDto>>(svc => svc.UpdateCurrentAsync(Viewer, new UpdateBillingAccountDto { DisplayName = "x" }));
        viewerEdit.Code.ShouldBe(403);

        var contact = await InScopeAsync<IBillingAccountService, BillingContact>(svc => svc.ResolveContactAsync(Installation, "manager", "manager@smith.example"));
        contact.Name.ShouldBe("Smith & Partners LLP");
        contact.Emails.ShouldBe(["billing@smith.example", "partner@smith.example"], "billing mail goes to the account, not to whichever manager clicked");
    }

    [Fact]
    public async Task BillingProfile_RejectsAnInvalidEmail()
    {
        _currentUser.Id = ManagerA;
        var updated = await InScopeAsync<IBillingAccountService, Result<BillingAccountDto>>(svc => svc.UpdateCurrentAsync(ManagerA, new UpdateBillingAccountDto
        {
            BillingEmails = ["not-an-address"]
        }));

        updated.Code.ShouldBe(400);
    }

    [Fact]
    public async Task ACardSavedWhileTakingAPayment_IsRegisteredOnThePaymentsAccount()
    {
        _currentUser.Id = ManagerA;
        var created = await InScopeAsync<IPaymentService, Result<PaymentOrderResultDto>>(svc => svc.CreatePaymentAsync(new CreatePaymentDto
        {
            BusinessOrderNo = "SUB-1",
            BusinessType = BusinessType.Order,
            Amount = 25m,
            ChannelCode = "Null",
            IsSystemInitiated = true,
            BillingAccount = Installation,
            SavePaymentMethod = true
        }));
        created.Succeeded.ShouldBeTrue(created.Message);

        // 渠道回调是匿名的：存卡不能依赖当前用户
        _currentUser.Id = null;
        var callback = await InScopeAsync<IPaymentService, Result>(svc => svc.HandleCallbackAsync(new PaymentCallbackDto
        {
            ChannelCode = "Null",
            Parameters = new Dictionary<string, string>
            {
                ["trade_no"] = created.Data!.TradeNo,
                ["amount"] = "25",
                ["event_id"] = "evt_saved_card",
                ["saved_token"] = "pm_saved_on_payment"
            }
        }));
        callback.Succeeded.ShouldBeTrue(callback.Message);

        var methods = (await ListAs(ManagerB)).Data!;
        var saved = methods.ShouldHaveSingleItem();
        saved.IsDefault.ShouldBeTrue("the card paid with becomes the account's default for renewals");
    }

    [Fact]
    public async Task AChannelCardUpdate_RefreshesTheStoredCard_AndAnUnknownCardIsIgnoredQuietly()
    {
        var bound = await BindAs(ManagerA, "pm_firm_update");

        _currentUser.Id = null;
        var updated = await InScopeAsync<IPaymentService, Result>(svc => svc.HandleCallbackAsync(new PaymentCallbackDto
        {
            ChannelCode = "Null",
            Parameters = new Dictionary<string, string>
            {
                ["event_id"] = "evt_card_updated",
                ["updated_token"] = "pm_firm_update",
                ["last4"] = "1881",
                ["exp_year"] = "2031"
            }
        }));
        updated.Succeeded.ShouldBeTrue(updated.Message);

        var stored = await ReloadAsync<StoredPaymentMethod>(bound.Data!.Id);
        stored!.Last4.ShouldBe("1881");
        stored.ExpiryYear.ShouldBe(2031);
        stored.Brand.ShouldBe("null", "fields the channel did not report keep their value");

        // 同一个渠道账户上别的应用的卡：本地查不到，安静地成功（答 2xx，渠道不重投）
        var foreign = await InScopeAsync<IPaymentService, Result>(svc => svc.HandleCallbackAsync(new PaymentCallbackDto
        {
            ChannelCode = "Null",
            Parameters = new Dictionary<string, string>
            {
                ["event_id"] = "evt_foreign_card",
                ["updated_token"] = "pm_someone_else",
                ["last4"] = "0000"
            }
        }));
        foreign.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task ClientConfig_ReportsTheChannel()
    {
        var config = await InScopeAsync<IPaymentMethodService, Result<PaymentClientConfigDto>>(svc => Task.FromResult(svc.GetClientConfig("null")));

        config.Succeeded.ShouldBeTrue();
        config.Data!.ChannelCode.ShouldBe("Null");
        config.Data.PublishableKey.ShouldBeNull("the test channel has no browser key");
    }

    /// <summary>可在测试中途切换身份的当前用户。</summary>
    private sealed class SwitchableCurrentUser(Guid? id) : ICurrentUser
    {
        public Guid? Id { get; set; } = id;
        public bool IsAuthenticated => Id.HasValue;
        public string? UserName => Id?.ToString("N")[..8];
        public string? Email => Id.HasValue ? $"{UserName}@test.example" : null;
        public Guid? TenantId => null;
        public string[] Roles => [];
        public bool IsInRole(string roleName) => false;
    }

    /// <summary>按用户发放固定权限码的检查器。</summary>
    private sealed class GrantingPermissionChecker(IReadOnlyDictionary<Guid, string[]> grants) : IPermissionChecker
    {
        public Task<bool> IsGrantedAsync(string permissionName) => Task.FromResult(false);

        public Task<bool> IsGrantedAsync(Guid userId, string permissionName)
            => Task.FromResult(grants.TryGetValue(userId, out var codes) && codes.Contains(permissionName));

        public Task<bool> IsGrantedAnyAsync(params string[] permissionNames) => Task.FromResult(false);
        public Task<bool> IsGrantedAllAsync(params string[] permissionNames) => Task.FromResult(false);
        public Task CheckAsync(string permissionName) => Task.CompletedTask;
    }
}
