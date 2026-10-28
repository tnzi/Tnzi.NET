using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.MultiTenancy;
using Tnzi.Payment.Extensions;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Options;
using Tnzi.Payment.Permissions;
using Tnzi.Payment.Services;
using Tnzi.Security.Authorization;

namespace Tnzi.Payment.Tests;

/// <summary>
/// 内置计费账户解析器的三种范围，以及服务层账户门的读 / 写判定。
/// </summary>
public class BillingAccountResolverTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static DefaultBillingAccountResolver Resolver(BillingAccountOptions options, IPermissionChecker? checker = null, ICurrentTenant? tenant = null)
        => new(new StaticMonitor(options), NullLogger<DefaultBillingAccountResolver>.Instance, checker, tenant);

    private static IPermissionChecker Granting(params string[] codes)
    {
        var checker = new Mock<IPermissionChecker>();
        checker.Setup(c => c.IsGrantedAsync(UserId, It.IsAny<string>())).ReturnsAsync((Guid _, string code) => codes.Contains(code));
        return checker.Object;
    }

    [Fact]
    public async Task UserScope_IsThePersonalAccount_WithFullAccess_AndNeedsNoPermissionChecker()
    {
        var access = await Resolver(new BillingAccountOptions()).ResolveAsync(UserId);

        access.ShouldNotBeNull();
        access.Account.ShouldBe(BillingAccountRef.ForUser(UserId));
        access.Account.Id.ShouldBe(UserId.ToString(), "lower-case dashed, the form backfilled rows must match");
        access.CanView.ShouldBeTrue();
        access.CanManage.ShouldBeTrue();
    }

    [Fact]
    public async Task InstallationScope_IsOneSharedAccount_GatedByPermissions()
    {
        var options = new BillingAccountOptions { Scope = BillingAccountScope.Installation, InstallationAccountId = "firm" };

        var manager = await Resolver(options, Granting(PaymentPermissions.AccountManage)).ResolveAsync(UserId);
        manager!.Account.ShouldBe(new BillingAccountRef(BillingAccountTypes.Installation, "firm"));
        manager.CanManage.ShouldBeTrue();
        manager.CanView.ShouldBeTrue("manage implies view");

        var viewer = await Resolver(options, Granting(PaymentPermissions.AccountView)).ResolveAsync(UserId);
        viewer!.CanView.ShouldBeTrue();
        viewer.CanManage.ShouldBeFalse();

        var staff = await Resolver(options, Granting()).ResolveAsync(UserId);
        staff!.CanView.ShouldBeFalse();
        staff.CanManage.ShouldBeFalse();
    }

    [Fact]
    public async Task InstallationScope_WithoutAPermissionChecker_FailsClosed()
    {
        var access = await Resolver(new BillingAccountOptions { Scope = BillingAccountScope.Installation }).ResolveAsync(UserId);

        access!.CanView.ShouldBeFalse();
        access.CanManage.ShouldBeFalse();
    }

    [Fact]
    public async Task TenantScope_IsTheCurrentTenant_AndAHostUserActsForNoAccount()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new Mock<ICurrentTenant>();
        tenant.Setup(t => t.Id).Returns(tenantId);

        var options = new BillingAccountOptions { Scope = BillingAccountScope.Tenant };
        var inTenant = await Resolver(options, Granting(PaymentPermissions.AccountManage), tenant.Object).ResolveAsync(UserId);
        inTenant!.Account.ShouldBe(new BillingAccountRef(BillingAccountTypes.Tenant, tenantId.ToString()));

        var host = new Mock<ICurrentTenant>();
        host.Setup(t => t.Id).Returns((Guid?)null);
        (await Resolver(options, Granting(PaymentPermissions.AccountManage), host.Object).ResolveAsync(UserId)).ShouldBeNull();
    }

    [Fact]
    public async Task ResolveScope_WithoutAnActingUser_IsUnrestricted()
    {
        var scope = await Resolver(new BillingAccountOptions()).ResolveScopeAsync(actingUserId: null, manage: true);

        scope.Succeeded.ShouldBeTrue();
        scope.Data.ShouldBeNull("administrative and system callers are not narrowed to an account");
    }

    [Fact]
    public async Task ResolveScope_DistinguishesViewFromManage()
    {
        var resolver = Resolver(new BillingAccountOptions { Scope = BillingAccountScope.Installation }, Granting(PaymentPermissions.AccountView));

        (await resolver.ResolveScopeAsync(UserId, manage: false)).Succeeded.ShouldBeTrue();

        var write = await resolver.ResolveScopeAsync(UserId, manage: true);
        write.Succeeded.ShouldBeFalse();
        write.Code.ShouldBe(403);
        write.Message.ShouldBe(ErrorCodes.BillingAccountAccessDenied);
    }

    [Fact]
    public void Validator_RequiresAnInstallationAccountId()
    {
        var result = new BillingAccountOptionsValidator().Validate(null, new BillingAccountOptions
        {
            Scope = BillingAccountScope.Installation,
            InstallationAccountId = " "
        });

        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void BillingAccountRef_ComparesByValue_AndReadsLegacyUserAccounts()
    {
        BillingAccountRef.From("User", UserId.ToString()).ShouldBe(BillingAccountRef.ForUser(UserId));
        BillingAccountRef.From(null, "x").ShouldBeNull();
        BillingAccountRef.ForUser(UserId).TryGetUserId(out var parsed).ShouldBeTrue();
        parsed.ShouldBe(UserId);
        new BillingAccountRef(BillingAccountTypes.Installation, "default").TryGetUserId(out _).ShouldBeFalse();
    }

    private sealed class StaticMonitor(BillingAccountOptions value) : IOptionsMonitor<BillingAccountOptions>
    {
        public BillingAccountOptions CurrentValue => value;
        public BillingAccountOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<BillingAccountOptions, string?> listener) => null;
    }
}
