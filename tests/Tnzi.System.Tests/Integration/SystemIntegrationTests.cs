
namespace Tnzi.System.Tests.Integration;

/// <summary>
/// System 模块集成测试
/// </summary>
public class SystemIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task Should_Create_AccessLog()
    {
        // Arrange
        var accessLog = new AccessLog
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            UserName = "testuser",
            Path = "/api/test",
            Method = "GET",
            IpAddress = "192.168.1.1",
            UserAgent = "Mozilla/5.0",
            StatusCode = 200,
            ResponseTime = 150,
            CreationTime = DateTime.UtcNow
        };

        // Act
        await DbContext.AccessLogs.AddAsync(accessLog);
        await DbContext.SaveChangesAsync();

        // Assert
        var savedLog = await DbContext.AccessLogs.FirstOrDefaultAsync(l => l.UserName == "testuser");
        savedLog.ShouldNotBeNull();
        savedLog!.IpAddress.ShouldBe("192.168.1.1");
        savedLog.StatusCode.ShouldBe(200);
    }

    [Fact]
    public async Task Should_Query_AccessLogs_By_User()
    {
        // Arrange
        var userId = Guid.NewGuid();
        for (int i = 1; i <= 5; i++)
        {
            await DbContext.AccessLogs.AddAsync(new AccessLog
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                UserName = "testuser",
                Path = $"/api/test{i}",
                Method = "GET",
                IpAddress = "192.168.1.1",
                StatusCode = 200,
                ResponseTime = 100 + i,
                CreationTime = DateTime.UtcNow.AddMinutes(-i)
            });
        }
        await DbContext.SaveChangesAsync();

        // Act
        var userLogs = await DbContext.AccessLogs
            .Where(l => l.UserId == userId)
            .OrderByDescending(l => l.CreationTime)
            .ToListAsync();

        // Assert
        userLogs.Count.ShouldBe(5);
        userLogs.First().Path.ShouldBe("/api/test1");
    }
}
