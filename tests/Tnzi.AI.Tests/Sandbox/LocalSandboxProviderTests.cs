namespace Tnzi.AI.Tests.Sandbox;

public class LocalSandboxProviderTests
{
    [Fact]
    public void Name_ReturnsLocal()
    {
        var provider = CreateProvider();
        Assert.Equal("local", provider.Name);
    }

    [Fact]
    public async Task CreateAsync_ReturnsLocalSandbox()
    {
        var provider = CreateProvider();
        var tempDir = Path.Combine(Path.GetTempPath(), $"tnzi-provider-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            await using var sandbox = await provider.CreateAsync(new SandboxCreateOptions
            {
                ThreadId = Guid.NewGuid(),
                WorkspacePath = tempDir
            });

            Assert.NotNull(sandbox);
            Assert.StartsWith("local-", sandbox.Id);
            Assert.IsType<LocalSandbox>(sandbox);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task CreateAsync_IncrementsId()
    {
        var provider = CreateProvider();
        var tempDir = Path.Combine(Path.GetTempPath(), $"tnzi-provider-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            await using var sb1 = await provider.CreateAsync(new SandboxCreateOptions
            {
                ThreadId = Guid.NewGuid(),
                WorkspacePath = tempDir
            });
            await using var sb2 = await provider.CreateAsync(new SandboxCreateOptions
            {
                ThreadId = Guid.NewGuid(),
                WorkspacePath = tempDir
            });

            Assert.NotEqual(sb1.Id, sb2.Id);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private static LocalSandboxProvider CreateProvider()
    {
        var options = new SandboxModuleOptions();
        return new LocalSandboxProvider(Microsoft.Extensions.Options.Options.Create(options));
    }
}
