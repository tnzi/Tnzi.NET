using Microsoft.Extensions.Configuration;
using Tnzi.Authorization.DataAuth.Events.Handlers;
using Tnzi.EventBus;
using Tnzi.Identity.Events;
using Tnzi.Modules;

namespace Tnzi.Authorization.DataAuth.Tests;

/// <summary>
/// 模块真的把处理器挂进了容器：单测里手工 new 一个处理器调用证明不了这一点，
/// 少了这行注册，角色成员变更后行级过滤的缓存表达式在缓存期内照样放出被摘掉角色的行。
/// </summary>
public class ModuleRegistrationTests
{
    [Fact]
    public async Task Role_membership_changes_are_wired_to_the_data_filter_cache()
    {
        var services = new ServiceCollection();
        var context = new ServiceConfigurationContext(services, new ConfigurationBuilder().Build());

        await new AuthorizationDataAuthModule().ConfigureServicesAsync(context);

        services.ShouldContain(d =>
            d.ServiceType == typeof(IEventHandler<UserRolesChangedEvent>)
            && d.ImplementationType == typeof(UserRolesChangedDataFilterCacheHandler));
    }
}
