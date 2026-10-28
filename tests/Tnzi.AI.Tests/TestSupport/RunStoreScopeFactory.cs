namespace Tnzi.AI.Tests;

/// <summary>
/// 给后台作用域补上测试里 mock 的 <see cref="IRunStore"/>（容器里已注册的优先）。
/// </summary>
/// <remarks>
/// spawn 在独立作用域里建运行行并立刻提交（不落在调用方的环境事务里），建行用的是那个作用域里解析出的 <see cref="IRunStore"/>，
/// 而不是注入给服务的那一个。单元测试的容器只注册了运行时，这里把同一个 mock 交给新作用域。
/// </remarks>
internal sealed class RunStoreScopeFactory(IServiceScopeFactory inner, IRunStore runStore) : IServiceScopeFactory
{
    public IServiceScope CreateScope() => new Scope(inner.CreateScope(), runStore);

    private sealed class Scope(IServiceScope inner, IRunStore runStore) : IServiceScope, IServiceProvider
    {
        public IServiceProvider ServiceProvider => this;

        public object? GetService(Type serviceType)
        {
            var service = inner.ServiceProvider.GetService(serviceType);
            return service ?? (serviceType == typeof(IRunStore) ? runStore : null);
        }

        public void Dispose() => inner.Dispose();
    }
}
