using Microsoft.Extensions.Logging.Abstractions;

namespace Tnzi.AI.Tests.Agents;

/// <summary>
/// 数据库里的子 Agent 类型在迁移之后读进注册表；启动那一刻数据库不可达时任务<b>抛出</b>（运行器据此在后台重试），
/// 数据库恢复后再跑一次即读到。此前它在模块初始化里读一次、失败只记 Warning，各租户的自定义类型缺席到重启。
/// </summary>
public class SubAgentTypeLoadStartupTaskTests
{
    private readonly Mock<IRepository<SubAgentType, Guid>> _repository = new();
    private readonly SubAgentRegistry _registry = new();

    private ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISubAgentRegistry>(_registry);
        services.AddScoped(_ => _repository.Object);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task WhileTheDatabaseIsUnreachable_TheTaskFails_AndOnceItIsBack_TheTypesAreLoaded()
    {
        await using var services = Services();
        IPostMigrationStartupTask task = new SubAgentTypeLoadStartupTask();

        _repository.Setup(r => r.AsQueryable(It.IsAny<bool>())).Throws(new TimeoutException("Timeout during connecting"));
        await Should.ThrowAsync<TimeoutException>(() => task.ExecuteAsync(services));
        _registry.GetForTenant("auditor", SubAgentTenantKey.From(null)).ShouldBeNull();

        var row = new SubAgentType { Id = Guid.NewGuid(), Name = "auditor", Description = "auditor", Instructions = "audit", IsEnabled = true };
        _repository.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(new List<SubAgentType> { row }.BuildMock());
        await task.ExecuteAsync(services);

        _registry.GetForTenant("auditor", SubAgentTenantKey.From(null)).ShouldNotBeNull();
        task.RequiredForReadiness.ShouldBeFalse("the built-in types keep working meanwhile");
    }
}
