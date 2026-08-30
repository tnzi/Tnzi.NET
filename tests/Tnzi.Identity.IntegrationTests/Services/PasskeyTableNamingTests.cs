using Microsoft.AspNetCore.Identity;

namespace Tnzi.Identity.IntegrationTests.Services;

/// <summary>
/// Passkey 凭据表的表名约定。
/// </summary>
/// <remarks>
/// ★ 值得单独钉一条的原因：<see cref="IdentityUserPasskey{TKey}"/> 是<strong>运行时的类型</strong>，
/// 程序集是 <c>Microsoft.Extensions.Identity.Stores</c> 而不是 <c>Tnzi.Identity</c>。
/// 而框架的表名前缀（<c>TableNamePrefixConfiguration</c>）是<strong>按实体所在程序集</strong>反查模块的 ——
/// 换句话说，Identity 模块自己那些实体能自动拿到 <c>Identity_</c>，这一个拿不到。
/// 所以它的表名必须在 <c>ConfigurePasskeyModel</c> 里写全；照抄旁边那些 <c>ToTable("User")</c>
/// 的裸名写法，会得到一张没有模块前缀的 <c>UserPasskey</c> 表，混在消费应用自己的表中间。
/// </remarks>
public class PasskeyTableNamingTests : IntegrationTestBase
{
    [Fact]
    public void PasskeyTable_CarriesTheIdentityModulePrefix()
    {
        var entityType = DbContext.Model.FindEntityType(typeof(IdentityUserPasskey<Guid>));

        Assert.NotNull(entityType);
        Assert.Equal("Identity_UserPasskey", entityType.GetTableName());
    }

    /// <summary>
    /// 对照组：模块自有实体的前缀是自动加上的，不是每处手写的。
    /// </summary>
    /// <remarks>
    /// 两条并排，才能区分「前缀机制在工作」与「前缀恰好被手写对了」——
    /// 这正是 passkey 那张表需要例外处理的原因。
    /// </remarks>
    [Fact]
    public void ModuleOwnedTable_GetsThePrefixAutomatically()
    {
        var entityType = DbContext.Model.FindEntityType(typeof(User));

        Assert.NotNull(entityType);
        Assert.Equal("Identity_User", entityType.GetTableName());
    }
}

/// <summary>
/// Passkey 凭据能真的存进关系型库并读回来。
/// </summary>
/// <remarks>
/// ★★ 这是 <c>HasNoKey</c> 那个止血带做不到的事：无主键实体在 EF 里是只读查询类型，
/// <c>AddOrUpdatePasskeyAsync</c> 的写入路径根本走不通，而模型校验照样通过、启动照样不报错。
/// 走 <see cref="UserManager{TUser}"/> 而不是直接 <c>DbSet.Add</c>，是因为要验的正是
/// <c>IUserPasskeyStore</c> 那条真实路径 —— 服务层用的就是它。
/// ★ 必须用关系型 provider：InMemory 对 <c>ComplexProperty</c> 的查询投影支持不完整，
/// 读回时会抛 <c>KeyNotFoundException</c>，那是 provider 的限制而不是映射的问题。
/// </remarks>
public class PasskeyPersistenceTests : RelationalIdentityIntegrationTestBase
{
    [Fact]
    public async Task Passkey_SurvivesAWriteAndReadThroughUserManager()
    {
        var user = new User { UserName = "passkey-owner", Email = "passkey-owner@example.com" };
        Assert.True((await UserManager.CreateAsync(user, "Password1")).Succeeded);

        var passkey = new UserPasskeyInfo(
            credentialId: [1, 2, 3, 4],
            publicKey: [9, 9, 9],
            createdAt: DateTimeOffset.UtcNow,
            signCount: 0,
            transports: ["internal"],
            isUserVerified: true,
            isBackupEligible: true,
            isBackedUp: false,
            attestationObject: [7],
            clientDataJson: [8]);

        Assert.True((await UserManager.AddOrUpdatePasskeyAsync(user, passkey)).Succeeded);

        var stored = await UserManager.GetPasskeyAsync(user, [1, 2, 3, 4]);

        Assert.NotNull(stored);
        Assert.Equal([9, 9, 9], stored.PublicKey);
        Assert.True(stored.IsUserVerified);
    }

    /// <summary>
    /// 签名计数器写得回去 —— 克隆凭据检测的前提。
    /// </summary>
    [Fact]
    public async Task Passkey_SignCountUpdateIsPersisted()
    {
        var user = new User { UserName = "counter-owner", Email = "counter-owner@example.com" };
        Assert.True((await UserManager.CreateAsync(user, "Password1")).Succeeded);

        await UserManager.AddOrUpdatePasskeyAsync(user, CreatePasskey(signCount: 1));
        await UserManager.AddOrUpdatePasskeyAsync(user, CreatePasskey(signCount: 42));

        var stored = await UserManager.GetPasskeyAsync(user, [5, 6]);

        Assert.NotNull(stored);
        Assert.Equal((uint)42, stored.SignCount);
        // 更新而非新增：同一个凭据标识只该有一行。
        Assert.Single(await UserManager.GetPasskeysAsync(user));
    }

    private static UserPasskeyInfo CreatePasskey(uint signCount) => new(
        credentialId: [5, 6],
        publicKey: [1],
        createdAt: DateTimeOffset.UtcNow,
        signCount: signCount,
        transports: ["internal"],
        isUserVerified: true,
        isBackupEligible: true,
        isBackedUp: false,
        attestationObject: [2],
        clientDataJson: [3]);
}
