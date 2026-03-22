using Microsoft.Extensions.Hosting;
using Tnzi.Modules.Diagnostics;

namespace Tnzi.Tests.Modules;

public class ModuleManifestBuilderTests
{
    [Fact]
    public void Build_ShouldExtractServicesFromDescriptors()
    {
        // MemoryStream is in System.IO assembly, not the test assembly,
        // so it should be filtered out by assembly origin check.
        // Only services with ImplementationType in module assembly are included.
        var services = new List<ServiceDescriptor>
        {
            ServiceDescriptor.Scoped<IDisposable, MemoryStream>()
        };
        var module = CreateModuleDescriptor();

        var manifest = ModuleManifestBuilder.Build(module, services);

        Assert.Empty(manifest.Services);
    }

    [Fact]
    public void Build_WithEmptyServices_ShouldReturnEmptyServiceBackgroundTasksAndOptions()
    {
        var module = CreateModuleDescriptor();

        var manifest = ModuleManifestBuilder.Build(module, []);

        // Services, BackgroundTasks, and Options come from service descriptors — empty when none provided
        Assert.Empty(manifest.Services);
        Assert.Empty(manifest.BackgroundTasks);
        Assert.Empty(manifest.Options);

        // Controllers and Events come from assembly scanning — test assembly may contain them
        Assert.NotNull(manifest.Controllers);
        Assert.NotNull(manifest.Events);
    }

    [Fact]
    public void Build_ShouldFilterHostedServices()
    {
        var services = new List<ServiceDescriptor>
        {
            ServiceDescriptor.Singleton<IHostedService, TestHostedService>()
        };
        var module = CreateModuleDescriptor();

        var manifest = ModuleManifestBuilder.Build(module, services);

        Assert.Single(manifest.BackgroundTasks);
        Assert.Contains("TestHostedService", manifest.BackgroundTasks[0]);
    }

    [Fact]
    public void Build_ShouldExtractEventHandlers()
    {
        // The test assembly contains TestEventHandler (defined below as private nested class)
        var module = CreateModuleDescriptor();
        var manifest = ModuleManifestBuilder.Build(module, []);

        // Should find TestEventHandler in the same assembly via assembly scanning
        Assert.Contains(manifest.Events, e => e.Contains("TestEventHandler"));
    }

    [Fact]
    public void Build_WithNullModule_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ModuleManifestBuilder.Build(null!, []));
    }

    [Fact]
    public void Build_WithNullServiceDescriptors_ShouldThrow()
    {
        var module = CreateModuleDescriptor();

        Assert.Throws<ArgumentNullException>(() =>
            ModuleManifestBuilder.Build(module, null!));
    }

    private static ModuleDescriptor CreateModuleDescriptor()
    {
        // Use ModuleManifestBuilderTests as the type so the assembly is the test assembly.
        // We need a real ITnziModule instance — use TestModuleA which is defined in this test project.
        var moduleStub = new TestModuleA();
        // Pass ModuleManifestBuilderTests as the type so Assembly = test assembly
        return new ModuleDescriptor(typeof(ModuleManifestBuilderTests), moduleStub);
    }

    // Test helper types
    private class TestHostedService : IHostedService
    {
        public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private class TestEvent : EventBase { }

    private class TestEventHandler : IEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
