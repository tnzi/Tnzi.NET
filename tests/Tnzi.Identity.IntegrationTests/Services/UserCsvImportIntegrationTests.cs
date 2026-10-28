using Microsoft.AspNetCore.Identity;
using Tnzi.Identity.Services;

namespace Tnzi.Identity.IntegrationTests.Services;

/// <summary>
/// CSV 导入逐行建号：一行在数据库那一层被拒，不能连累后面的行。
/// </summary>
/// <remarks>
/// <c>UserManager.CreateAsync</c> 先把账号加进变更跟踪器再保存；数据库拒绝（列超长、并发重名）时抛出的
/// <c>DbUpdateException</c> 被逐行的 catch 接住，而那个账号仍以 Added 状态留在本请求的 DbContext 里，
/// 下一行的保存会把它再提交一遍、再失败一遍。结果是坏行之后的每一个有效行都报同一条数据库错误，
/// 而坏行之前的行已经提交，重跑时又被计成「已存在、跳过」。
/// </remarks>
public class UserCsvImportIntegrationTests : RelationalIdentityIntegrationTestBase
{
    private const string RefusedEmail = "refused@example.com";

    private readonly UserService _users;

    public UserCsvImportIntegrationTests()
    {
        _users = new UserService(
            UserManager,
            ServiceProvider.GetRequiredService<RoleManager<Role>>(),
            CreateRepository<User>(),
            ServiceProvider,
            eventBus: EventBusMock.Object,
            currentUser: ServiceProvider.GetRequiredService<ICurrentUser>());
    }

    /// <summary>
    /// 让数据库拒绝某一个地址的插入，代表校验器看不到、只有数据库才拦得住的那类失败。
    /// </summary>
    private async Task RefuseInsertOfAsync(string email)
    {
        // DDL 不能参数化；表名取自模型，地址是本类的常量。
        var table = DbContext.Model.FindEntityType(typeof(User))!.GetTableName();
        var ddl = $"CREATE TRIGGER refuse_one BEFORE INSERT ON \"{table}\" WHEN NEW.\"Email\" = '{email}' "
                  + "BEGIN SELECT RAISE(ABORT, 'refused by the database'); END;";
        await DbContext.Database.ExecuteSqlRawAsync(ddl);
    }

    [Fact]
    public async Task A_RowRefusedByTheDatabase_DoesNotFailTheRowsAfterIt()
    {
        await RefuseInsertOfAsync(RefusedEmail);
        var csv = "Email,Password\n"
                  + "first@example.com,Password123!\n"
                  + $"{RefusedEmail},Password123!\n"
                  + "after@example.com,Password123!\n";

        var result = await _users.ImportUsersCsvAsync(csv);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(2, result.Data!.SuccessCount);
        Assert.Equal(1, result.Data.FailedCount);
        Assert.Equal(new[] { 3 }, result.Data.Errors.Keys);
        DbContext.ChangeTracker.Clear();
        Assert.True(await DbContext.Users.AnyAsync(u => u.Email == "after@example.com"));
        Assert.False(await DbContext.Users.AnyAsync(u => u.Email == RefusedEmail));
    }

    [Fact]
    public async Task A_RowRefusedByTheDatabase_ReportsAGenericReason_NotTheDatabaseText()
    {
        await RefuseInsertOfAsync(RefusedEmail);
        var csv = $"Email,Password\n{RefusedEmail},Password123!\n";

        var result = await _users.ImportUsersCsvAsync(csv);

        Assert.True(result.Succeeded, result.Message);
        var reason = Assert.Single(result.Data!.Errors).Value;
        // EF 的原文让管理员去看一个他看不到的内部异常；数据库原文同理不该出现
        Assert.DoesNotContain("inner exception", reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refused by the database", reason);
        Assert.Contains("server log", reason);
    }
}
