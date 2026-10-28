using Tnzi.Identity.Services;

namespace Tnzi.Identity.IntegrationTests.Services;

/// <summary>
/// 按用户 Id 查账号邮箱（不引用 Identity 的模块经 <c>IUserEmailProvider</c> 用它给发起人寄账单邮件）。
/// </summary>
public class UserEmailProviderIntegrationTests : RelationalIdentityIntegrationTestBase
{
    [Fact]
    public async Task ReturnsTheAccountEmail_AndLeavesOutUsersWithoutOne()
    {
        var manager = await CreateUserAsync(email: "manager@firm.example");
        var noEmail = await CreateUserAsync(email: null);
        var provider = new UserEmailProvider(CreateRepository<User>());

        var emails = await provider.GetEmailsAsync([manager.Id, noEmail.Id, Guid.NewGuid(), Guid.Empty]);

        Assert.Single(emails);
        Assert.Equal("manager@firm.example", emails[manager.Id]);
    }
}
