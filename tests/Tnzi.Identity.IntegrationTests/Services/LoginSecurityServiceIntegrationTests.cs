using Microsoft.Extensions.Options;
using Tnzi.Identity.Options;
using Tnzi.Identity.Services;

namespace Tnzi.Identity.IntegrationTests.Services;

public class LoginSecurityServiceIntegrationTests : RelationalIdentityIntegrationTestBase
{
    private readonly LoginSecurityService _service;

    public LoginSecurityServiceIntegrationTests()
    {
        _service = new LoginSecurityService(
            ServiceProvider,
            ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>(),
            CreateRepository<LoginLog>(),
            UserManager);
    }

    [Fact]
    public async Task DetectAbnormalLoginAsync_WithKnownIp_ReturnsNormal()
    {
        var user = await CreateUserAsync();
        DbContext.LoginLogs.Add(new LoginLog
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            IpAddress = "127.0.0.1",
            UserAgent = "Mozilla/5.0 Chrome",
            Status = LoginStatus.Success,
            CreationTime = DateTime.UtcNow.AddHours(-2)
        });
        await SaveChangesAsync();

        var result = await _service.DetectAbnormalLoginAsync(user.Id, "127.0.0.1", "Mozilla/5.0 Chrome");

        Assert.False(result.IsAbnormal);
    }

    [Fact]
    public async Task DetectAbnormalLoginAsync_WithNewIp_ReturnsAbnormal()
    {
        var user = await CreateUserAsync();
        DbContext.LoginLogs.Add(new LoginLog
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            IpAddress = "127.0.0.1",
            UserAgent = "Mozilla/5.0 Chrome",
            Status = LoginStatus.Success,
            CreationTime = DateTime.UtcNow.AddHours(-2)
        });
        await SaveChangesAsync();

        var result = await _service.DetectAbnormalLoginAsync(user.Id, "8.8.8.8", "Mozilla/5.0 Chrome");

        Assert.True(result.IsAbnormal);
        Assert.Contains(AbnormalLoginType.NewIpAddress, result.AbnormalTypes);
    }
}
