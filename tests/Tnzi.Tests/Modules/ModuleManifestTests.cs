namespace Tnzi.Tests.Modules;

public class ModuleManifestTests
{
    [Fact]
    public void ModuleDescriptor_ShouldHaveEmptyManifestByDefault()
    {
        var module = new TestModuleA();
        var descriptor = new ModuleDescriptor(typeof(TestModuleA), module);

        Assert.Same(ModuleManifest.Empty, descriptor.Manifest);
    }

    [Fact]
    public void Empty_ShouldHaveEmptyCollections()
    {
        var manifest = ModuleManifest.Empty;

        Assert.Empty(manifest.Services);
        Assert.Empty(manifest.Controllers);
        Assert.Empty(manifest.Events);
        Assert.Empty(manifest.BackgroundTasks);
        Assert.Empty(manifest.Options);
    }

    [Fact]
    public void ServiceExport_ShouldStoreTypeInfo()
    {
        var export = new ServiceExport(
            typeof(IDisposable),
            typeof(MemoryStream),
            ServiceLifetime.Scoped);

        Assert.Equal(typeof(IDisposable), export.InterfaceType);
        Assert.Equal(typeof(MemoryStream), export.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, export.Lifetime);
    }
}
