using Tnzi.Identity.Services;

namespace Tnzi.Identity.IntegrationTests.Services;

public class LoginLogServiceIntegrationTests : RelationalIdentityIntegrationTestBase
{
    private readonly LoginLogService _service;

    public LoginLogServiceIntegrationTests()
    {
        _service = new LoginLogService(CreateRepository<LoginLog>(), LoginLogSenderMock.Object, ServiceProvider);
    }

    [Fact]
    public async Task GetUserLoginLogsAsync_WithValidUserId_ReturnsLogs()
    {
        var user = await CreateUserAsync(email: "logs@example.com");
        DbContext.LoginLogs.AddRange(
            CreateLog(user.Id, "10.0.0.1", LoginStatus.Success, DateTime.UtcNow.AddMinutes(-2)),
            CreateLog(user.Id, "10.0.0.2", LoginStatus.Failed, DateTime.UtcNow.AddMinutes(-1)),
            CreateLog(Guid.NewGuid(), "10.0.0.3", LoginStatus.Success, DateTime.UtcNow));
        await SaveChangesAsync();

        var result = await _service.GetUserLoginLogsAsync(user.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.Count());
        Assert.All(result.Data!, log => Assert.Equal(user.Id, log.UserId));
    }

    [Fact]
    public async Task GetRecentLogsAsync_WithUserId_ReturnsRecentLogs()
    {
        var user = await CreateUserAsync(email: "recent@example.com");
        DbContext.LoginLogs.AddRange(
            CreateLog(user.Id, "10.0.0.1", LoginStatus.Success, DateTime.UtcNow.AddMinutes(-10)),
            CreateLog(user.Id, "10.0.0.2", LoginStatus.Success, DateTime.UtcNow.AddMinutes(-1)),
            CreateLog(Guid.NewGuid(), "10.0.0.3", LoginStatus.Success, DateTime.UtcNow));
        await SaveChangesAsync();

        var logs = (await _service.GetRecentLogsAsync(user.Id, 5)).ToList();

        Assert.Equal(2, logs.Count);
        Assert.Equal("10.0.0.2", logs[0].IpAddress);
    }

    [Fact]
    public async Task GetRecentLogsAsync_WithoutUserId_ReturnsGlobalRecentLogs()
    {
        DbContext.LoginLogs.AddRange(
            CreateLog(Guid.NewGuid(), "10.0.0.1", LoginStatus.Success, DateTime.UtcNow.AddMinutes(-10)),
            CreateLog(Guid.NewGuid(), "10.0.0.2", LoginStatus.Success, DateTime.UtcNow.AddMinutes(-1)));
        await SaveChangesAsync();

        var logs = (await _service.GetRecentLogsAsync(null, 1)).ToList();

        Assert.Single(logs);
        Assert.Equal("10.0.0.2", logs[0].IpAddress);
    }

    [Fact]
    public async Task DeleteExpiredLogsAsync_WithValidDays_DeletesExpiredLogs()
    {
        DbContext.LoginLogs.AddRange(
            CreateLog(Guid.NewGuid(), "10.0.0.1", LoginStatus.Success, DateTime.UtcNow.AddDays(-120)),
            CreateLog(Guid.NewGuid(), "10.0.0.2", LoginStatus.Success, DateTime.UtcNow.AddDays(-2)));
        await SaveChangesAsync();

        var result = await _service.DeleteExpiredLogsAsync(90);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Data);
        Assert.Single(DbContext.LoginLogs);
    }

    [Fact]
    public async Task GetLogsByIpAsync_WithValidIp_ReturnsPagedList()
    {
        DbContext.LoginLogs.AddRange(
            CreateLog(Guid.NewGuid(), "192.168.1.10", LoginStatus.Success, DateTime.UtcNow.AddMinutes(-5)),
            CreateLog(Guid.NewGuid(), "192.168.1.10", LoginStatus.Failed, DateTime.UtcNow.AddMinutes(-1)),
            CreateLog(Guid.NewGuid(), "192.168.1.11", LoginStatus.Success, DateTime.UtcNow));
        await SaveChangesAsync();

        var result = await _service.GetLogsByIpAsync("192.168.1.10");

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item => Assert.Equal("192.168.1.10", item.IpAddress));
    }

    [Fact]
    public async Task GetLogsByDateRangeAsync_WithValidRange_ReturnsPagedList()
    {
        var now = DateTime.UtcNow;
        DbContext.LoginLogs.AddRange(
            CreateLog(Guid.NewGuid(), "10.0.0.1", LoginStatus.Success, now.AddDays(-4)),
            CreateLog(Guid.NewGuid(), "10.0.0.2", LoginStatus.Success, now.AddDays(-2)),
            CreateLog(Guid.NewGuid(), "10.0.0.3", LoginStatus.Success, now));
        await SaveChangesAsync();

        var result = await _service.GetLogsByDateRangeAsync(now.AddDays(-3), now.AddDays(-1));

        Assert.Single(result.Items);
        Assert.Equal("10.0.0.2", result.Items[0].IpAddress);
    }

    private static LoginLog CreateLog(Guid? userId, string ipAddress, LoginStatus status, DateTime creationTime)
    {
        return new LoginLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            UserName = userId?.ToString(),
            IpAddress = ipAddress,
            UserAgent = "Mozilla/5.0",
            Status = status,
            CreationTime = creationTime
        };
    }
}
