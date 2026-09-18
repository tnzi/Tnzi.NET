using Tnzi.Data;
using Tnzi.Modules;

namespace Tnzi.Identity.Tests;

/// <summary>
/// DI 那一行漏了看不见：修复逻辑的测试再全，少了模块里的注册，启动期什么都不会跑，
/// 而那些被旧版「启用」关掉锁定的账号照旧没有暴力破解保护。
/// </summary>
public class LockoutProtectionRepairRegistrationTests
{
    [Fact]
    public async Task IdentityModule_RegistersTheLockoutRepairStartupTask()
    {
        var services = new ServiceCollection();
        var context = new ServiceConfigurationContext(services, new ConfigurationBuilder().Build());

        await new IdentityModule().ConfigureServicesAsync(context);

        services.ShouldContain(d =>
            d.ServiceType == typeof(IPostMigrationStartupTask)
            && d.ImplementationType == typeof(LockoutProtectionRepairStartupTask),
            "IdentityModule no longer registers LockoutProtectionRepairStartupTask; accounts left with "
            + "LockoutEnabled = false by the old Enable stay without brute-force protection.");
    }
}
