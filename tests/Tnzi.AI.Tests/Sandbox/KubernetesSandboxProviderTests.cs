namespace Tnzi.AI.Tests.Sandbox;

/// <summary>
/// 2026-04-15 audit fix: KubernetesSandboxProvider used to silently fall back to
/// host-local execution while logging a warning. That broke the operator's pod
/// isolation expectation. The provider now fails fast with NotSupportedException
/// until native K8s pod support ships.
/// </summary>
public class KubernetesSandboxProviderTests
{
    [Fact]
    public void Name_ReturnsKubernetes()
    {
        var provider = CreateProvider();
        provider.Name.ShouldBe("kubernetes");
    }

    [Fact]
    public async Task CreateAsync_Throws_NotSupportedException()
    {
        var provider = CreateProvider();

        var ex = await Should.ThrowAsync<NotSupportedException>(() =>
            provider.CreateAsync(new SandboxCreateOptions
            {
                ThreadId = Guid.NewGuid(),
                WorkspacePath = Path.GetTempPath()
            }));

        ex.Message.ShouldContain("Kubernetes sandbox provider is not implemented");
        ex.Message.ShouldContain("local");
    }

    [Fact]
    public void Validator_RejectsUnknownProviderName()
    {
        var validator = new SandboxModuleOptionsValidator();
        var options = new SandboxModuleOptions { Provider = "nonsense" };

        var result = validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("not supported");
    }

    [Fact]
    public void Validator_AcceptsKubernetesProviderName()
    {
        // K8s provider name is valid at the options level (the provider itself
        // throws at CreateAsync time); this ensures operators can still wire it
        // in config and get a meaningful runtime error rather than an options
        // validation failure at startup.
        var validator = new SandboxModuleOptionsValidator();
        var options = new SandboxModuleOptions { Provider = "kubernetes" };

        var result = validator.Validate(null, options);

        result.Succeeded.ShouldBeTrue();
    }

    private static KubernetesSandboxProvider CreateProvider()
    {
        return new KubernetesSandboxProvider(NullLogger<KubernetesSandboxProvider>.Instance);
    }
}
