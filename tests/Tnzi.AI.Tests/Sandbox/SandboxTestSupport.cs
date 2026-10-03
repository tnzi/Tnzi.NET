using Tnzi.AI.Sandbox.Middleware;
using Tnzi.AI.Sandbox.Services;

namespace Tnzi.AI.Tests.Sandbox;

/// <summary>
/// 沙箱中间件测试的公共夹具：可计数的 provider / 可计数的技能仓库 / 按目录建好的中间件。
/// </summary>
internal static class SandboxTestSupport
{
    public static string NewTempRoot(string prefix) =>
        Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");

    public static SandboxModuleOptions SandboxOptions(string dataRoot, bool lazyDirectoryCreation = true) =>
        new() { DataRoot = dataRoot, LazyDirectoryCreation = lazyDirectoryCreation };

    public static ThreadDataProvisioner CreateProvisioner(SandboxModuleOptions options, ISkillStore? store = null) =>
        new(Microsoft.Extensions.Options.Options.Create(options),
            new VirtualPathTranslator(options.DataRoot),
            NullLogger<ThreadDataProvisioner>.Instance,
            store);

    public static ThreadDataMiddleware CreateThreadDataMiddleware(SandboxModuleOptions options) =>
        new(Microsoft.Extensions.Options.Options.Create(options),
            new ServiceCollection().AddSingleton<IVirtualPathTranslator>(_ => new VirtualPathTranslator(options.DataRoot)).BuildServiceProvider(),
            NullLogger<ThreadDataMiddleware>.Instance);

    public static SandboxMiddleware CreateSandboxMiddleware(
        SandboxModuleOptions options, ISandboxProvider provider, AgentExecutionContextAccessor accessor, ISkillStore? store = null) =>
        new(provider,
            Microsoft.Extensions.Options.Options.Create(options),
            accessor,
            CreateProvisioner(options, store),
            NullLogger<SandboxMiddleware>.Instance);

    public static LocalSandboxProvider CreateLocalProvider(SandboxModuleOptions options, string environmentName = "Development") =>
        new(Microsoft.Extensions.Options.Options.Create(options),
            new TestHostEnvironment { EnvironmentName = environmentName },
            NullLogger<LocalSandboxProvider>.Instance);

    public sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tnzi.AI.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    /// <summary>记录每一次创建与释放的 provider；创建可以被脚本化成失败。</summary>
    public sealed class CountingSandboxProvider : ISandboxProvider
    {
        private int _createCalls;

        public string Name => "counting";
        public int CreateCalls => _createCalls;
        public List<CountingSandbox> Created { get; } = [];
        public Func<SandboxCreateOptions, Exception?>? FailWith { get; set; }

        public Task<ISandbox> CreateAsync(SandboxCreateOptions options, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _createCalls);
            if (FailWith?.Invoke(options) is { } failure) throw failure;

            var sandbox = new CountingSandbox($"counting-{_createCalls}", options.WorkspacePath);
            Created.Add(sandbox);
            return Task.FromResult<ISandbox>(sandbox);
        }
    }

    public sealed class CountingSandbox(string id, string workspacePath) : ISandbox
    {
        public string Id => id;
        public string WorkspacePath => workspacePath;
        public bool Disposed { get; private set; }
        public List<string> ExecutedCommands { get; } = [];

        public Task<CommandResult> ExecuteCommandAsync(string command, CancellationToken ct = default)
        {
            ExecutedCommands.Add(command);
            return Task.FromResult(new CommandResult(0, "counting-ok", string.Empty));
        }

        public Task<string> ReadFileAsync(string path, int? offset = null, int? limit = null, CancellationToken ct = default)
            => Task.FromResult(File.Exists(path) ? File.ReadAllText(path) : string.Empty);

        public Task WriteFileAsync(string path, string content, bool append = false, CancellationToken ct = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return Task.CompletedTask;
        }

        public Task UpdateFileAsync(string path, byte[] content, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<FileEntry>> ListDirectoryAsync(string path, int maxDepth = 2, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<FileEntry>>([]);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>只有一个带资源的技能，并记录被查询的次数。</summary>
    public sealed class CountingSkillStore : ISkillStore
    {
        public string Slug => "probe";
        public int GetAllCalls { get; private set; }

        public Task<List<SkillDefinition>> GetAllAsync(CancellationToken ct = default)
        {
            GetAllCalls++;
            return Task.FromResult(new List<SkillDefinition>
            {
                new()
                {
                    Slug = Slug,
                    Name = "Probe",
                    Resources = new Dictionary<string, string> { ["scripts/run.py"] = "print('hi')" }
                }
            });
        }

        public Task<SkillDefinition?> GetBySlugAsync(string slug, CancellationToken ct = default)
            => Task.FromResult<SkillDefinition?>(null);
    }
}
