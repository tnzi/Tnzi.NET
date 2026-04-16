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

    [Fact]
    public async Task CreateAsync_InProduction_WithoutOptIn_Throws()
    {
        // 2026-04-15 audit fix: LocalSandbox offers no process isolation; must be
        // opt-in for Production to prevent operators from accidentally deploying it.
        var provider = CreateProvider(environment: "Production");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.CreateAsync(new SandboxCreateOptions
            {
                ThreadId = Guid.NewGuid(),
                WorkspacePath = Path.GetTempPath()
            }));

        Assert.Contains("AllowInProduction", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_InProduction_WithOptIn_Succeeds()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tnzi-provider-prodoptin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var provider = CreateProvider(
                environment: "Production",
                allowInProduction: true);

            await using var sandbox = await provider.CreateAsync(new SandboxCreateOptions
            {
                ThreadId = Guid.NewGuid(),
                WorkspacePath = tempDir
            });

            Assert.NotNull(sandbox);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task CreateAsync_InDevelopment_Succeeds()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tnzi-provider-dev-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var provider = CreateProvider(environment: "Development");

            await using var sandbox = await provider.CreateAsync(new SandboxCreateOptions
            {
                ThreadId = Guid.NewGuid(),
                WorkspacePath = tempDir
            });

            Assert.NotNull(sandbox);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private static LocalSandboxProvider CreateProvider(
        string environment = "Development",
        bool allowInProduction = false)
    {
        var options = new SandboxModuleOptions();
        options.Local.AllowInProduction = allowInProduction;

        var hostEnvironment = new StubHostEnvironment(environment);

        return new LocalSandboxProvider(
            Microsoft.Extensions.Options.Options.Create(options),
            hostEnvironment,
            NullLogger<LocalSandboxProvider>.Instance);
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public StubHostEnvironment(string environmentName) => EnvironmentName = environmentName;

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "Tnzi.AI.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
