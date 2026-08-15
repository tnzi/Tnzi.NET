namespace Tnzi.Audit.Tests.Retention;

/// <summary>
/// 默认密钥存活判定，以及它能否真的被容器激活。
/// </summary>
/// <remarks>
/// 销毁流程的其余测试都是手工 <c>new</c> 出来的，那条路径<b>绕过了容器</b>，
/// 因而证明不了「构造函数上的可选依赖能被内置 DI 解析」这件事。
/// 若解析不出来，单元测试照样全绿，而应用一跑销毁作业就崩在 DI 上。
/// </remarks>
public class EncryptionKeyStateProviderTests
{
    private static ServiceProvider BuildContainer(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddScoped<IEncryptionKeyStateProvider, FieldEncryptionKeyStateProvider>();
        configure?.Invoke(services);
        return services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public void TheDefaultProvider_IsResolvableFromTheContainer()
    {
        using var container = BuildContainer();
        using var scope = container.CreateScope();

        var provider = scope.ServiceProvider.GetRequiredService<IEncryptionKeyStateProvider>();

        Assert.IsType<FieldEncryptionKeyStateProvider>(provider);
    }

    [Fact]
    public void WithoutFieldEncryptionConfigured_NothingIsClaimedDestroyed()
    {
        // 密钥环此时本来就是空的。据此判定「已销毁」等于盖一个没资格盖的章。
        using var container = BuildContainer();
        using var scope = container.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IEncryptionKeyStateProvider>();

        Assert.False(provider.IsDestroyed("tip-2025"));
    }

    [Fact]
    public void WithEncryptionEnabled_AKeyStillInTheRingIsNotDestroyed()
    {
        using var container = BuildContainer(s => s.Configure<FieldEncryptionOptions>(o =>
        {
            o.Enabled = true;
            o.ActiveKeyId = "tip-2026";
            o.Keys["tip-2026"] = Convert.ToBase64String(new byte[32]);
        }));
        using var scope = container.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IEncryptionKeyStateProvider>();

        Assert.False(provider.IsDestroyed("tip-2026"));
    }

    [Fact]
    public void WithEncryptionEnabled_AKeyRemovedFromTheRingIsDestroyed()
    {
        using var container = BuildContainer(s => s.Configure<FieldEncryptionOptions>(o =>
        {
            o.Enabled = true;
            o.ActiveKeyId = "tip-2026";
            o.Keys["tip-2026"] = Convert.ToBase64String(new byte[32]);
        }));
        using var scope = container.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IEncryptionKeyStateProvider>();

        Assert.True(provider.IsDestroyed("tip-2025"));
    }

    [Fact]
    public void AnEmptyKeyId_IsNeverClaimedDestroyed()
    {
        using var container = BuildContainer();
        using var scope = container.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IEncryptionKeyStateProvider>();

        Assert.False(provider.IsDestroyed(""));
        Assert.False(provider.IsDestroyed("   "));
    }
}
