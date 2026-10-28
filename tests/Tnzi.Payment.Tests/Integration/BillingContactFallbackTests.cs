using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.Domain.Repositories;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Services;
using Tnzi.Results;
using Tnzi.Security.Claims;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// 账单联系人的最后一级兜底：账户没填资料、令牌又不带邮箱声明时，按发起人的用户 Id 查他账号上的邮箱。
/// </summary>
/// <remarks>
/// 消费方实测：单客户部署（Installation 范围）里没人去 PUT 账户资料，管理者的令牌不带 email 声明，
/// 首期收据开出来没有收件人（「Invoice ... has no customer email」），催款信同样无人可寄。
/// </remarks>
public class BillingContactFallbackTests : PaymentIntegrationTestBase
{
    private static readonly Guid Manager = Guid.Parse("eeeeeeee-0000-0000-0000-000000000005");

    private readonly Dictionary<Guid, string> _accountEmails = new() { [Manager] = "manager@firm.example" };

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddScoped<ICurrentUser>(_ => new TokenWithoutEmail(Manager));
        services.AddSingleton<IUserEmailProvider>(new FixedEmails(_accountEmails));
    }

    [Fact]
    public async Task APaymentByAManagerWhoseTokenHasNoEmail_RecordsTheEmailOnTheirAccount()
    {
        var created = await InScopeAsync<IPaymentService, Result<PaymentOrderResultDto>>(svc => svc.CreatePaymentAsync(new CreatePaymentDto
        {
            BusinessOrderNo = "ORDER-1",
            BusinessType = BusinessType.Order,
            Amount = 10m,
            ChannelCode = "Null"
        }));
        created.Succeeded.ShouldBeTrue(created.Message);

        var payment = await InScopeAsync<IRepository<PaymentEntity, Guid>, PaymentEntity?>(repo =>
            repo.AsQueryable().AsNoTracking().FirstOrDefaultAsync(p => p.TradeNo == created.Data!.TradeNo));
        payment!.CustomerEmail.ShouldBe("manager@firm.example");
    }

    [Fact]
    public async Task TheAccountsOwnBillingContacts_StillComeFirst()
    {
        var account = BillingAccountRef.ForUser(Manager);
        (await InScopeAsync<IBillingAccountService, Result<BillingAccountDto>>(svc => svc.UpdateCurrentAsync(Manager, new UpdateBillingAccountDto
        {
            BillingEmails = ["accounts@firm.example"]
        }))).Succeeded.ShouldBeTrue();

        var contact = await InScopeAsync<IBillingAccountService, BillingContact>(svc => svc.ResolveContactAsync(account, null, null, Manager));

        contact.Emails.ShouldBe(["accounts@firm.example"]);
    }

    [Fact]
    public async Task WithNoAccountContactAndNoSnapshot_TheUsersAccountEmailIsUsed_AndUnknownUsersGetNone()
    {
        var contact = await InScopeAsync<IBillingAccountService, BillingContact>(svc => svc.ResolveContactAsync(null, null, null, Manager));
        contact.PrimaryEmail.ShouldBe("manager@firm.example");

        var stranger = await InScopeAsync<IBillingAccountService, BillingContact>(svc => svc.ResolveContactAsync(null, null, null, Guid.NewGuid()));
        stranger.Emails.ShouldBeEmpty();

        // 旧重载不带用户 Id：行为与此前逐字相同
        var legacy = await InScopeAsync<IBillingAccountService, BillingContact>(svc => svc.ResolveContactAsync(null, null, null));
        legacy.Emails.ShouldBeEmpty();
    }

    private sealed class TokenWithoutEmail(Guid id) : ICurrentUser
    {
        public Guid? Id => id;
        public bool IsAuthenticated => true;
        public string? UserName => "manager";
        public string? Email => null;
        public Guid? TenantId => null;
        public string[] Roles => [];
        public bool IsInRole(string roleName) => false;
    }

    private sealed class FixedEmails(IReadOnlyDictionary<Guid, string> emails) : IUserEmailProvider
    {
        public Task<IReadOnlyDictionary<Guid, string>> GetEmailsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(userIds.Where(emails.ContainsKey).ToDictionary(id => id, id => emails[id]));
    }
}
