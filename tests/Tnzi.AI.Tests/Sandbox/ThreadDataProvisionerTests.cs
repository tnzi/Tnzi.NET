using static Tnzi.AI.Tests.Sandbox.SandboxTestSupport;

namespace Tnzi.AI.Tests.Sandbox;

/// <summary>
/// 线程目录布置：建线程根目录、按配置预建子目录、逐线程复制技能资源（哨兵幂等 + 遗留符号链接迁移）。
/// </summary>
public class ThreadDataProvisionerTests : IDisposable
{
    private readonly string _root = NewTempRoot("tnzi-tdp");
    private readonly Guid _threadId = Guid.NewGuid();

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private ThreadDataState State(SandboxModuleOptions options) =>
        ThreadDataState.FromThreadDirectory(new VirtualPathTranslator(options.DataRoot).GetThreadDirectory(_threadId));

    /// <summary>
    /// 线程根目录一律建出来：Local provider 把它当 bash 的工作目录，不存在则进程起不来；
    /// 此前它只是技能复制的副作用，没加载 Skills 模块时根本不存在。
    /// </summary>
    [Fact]
    public async Task Provision_LazyDirectories_CreatesOnlyTheThreadRoot()
    {
        var options = SandboxOptions(_root, lazyDirectoryCreation: true);
        var provisioner = CreateProvisioner(options);
        var state = State(options);

        await provisioner.ProvisionAsync(_threadId, state);

        Assert.True(Directory.Exists(state.ThreadDirectory));
        Assert.False(Directory.Exists(state.WorkspacePath));
        Assert.False(Directory.Exists(state.UploadsPath));
        Assert.False(Directory.Exists(state.OutputsPath));
        Assert.False(Directory.Exists(state.SkillsPath));
    }

    [Fact]
    public async Task Provision_EagerDirectories_CreatesAllFourSubdirectories()
    {
        var options = SandboxOptions(_root, lazyDirectoryCreation: false);
        var provisioner = CreateProvisioner(options);
        var state = State(options);

        await provisioner.ProvisionAsync(_threadId, state);

        Assert.True(Directory.Exists(state.WorkspacePath));
        Assert.True(Directory.Exists(state.UploadsPath));
        Assert.True(Directory.Exists(state.OutputsPath));
        Assert.True(Directory.Exists(state.SkillsPath));
    }

    [Fact]
    public async Task Provision_WithSkillStore_CopiesResourcesAndWritesTheMarker()
    {
        var options = SandboxOptions(_root);
        var store = new CountingSkillStore();
        var provisioner = CreateProvisioner(options, store);
        var state = State(options);

        await provisioner.ProvisionAsync(_threadId, state);

        Assert.True(File.Exists(Path.Combine(state.SkillsPath, store.Slug, "scripts", "run.py")));
        Assert.StartsWith("copied at", File.ReadAllText(Path.Combine(state.ThreadDirectory, ".skills_wired")));
        Assert.Null(new DirectoryInfo(state.SkillsPath).LinkTarget);
    }

    /// <summary>
    /// 同一线程两次运行并发首次用到沙箱：接线必须串行，后到的一方看到哨兵就不再复制。此前两边同时写 skills/，
    /// 一方撞上文件占用失败后递归删掉整个 skills/，另一方随后写下哨兵 —— 技能资源永久缺失。
    /// </summary>
    [Fact]
    public async Task Provision_ConcurrentFirstUseOfTheSameThread_WiresExactlyOnce()
    {
        var options = SandboxOptions(_root);
        var store = new GatedSkillStore();
        var state = State(options);

        var first = CreateProvisioner(options, store).ProvisionAsync(_threadId, state);
        await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = CreateProvisioner(options, store).ProvisionAsync(_threadId, state);

        // 给第二次足够的时间闯进技能仓库（若没有串行，它此刻就在里面）。
        await Task.Delay(300);
        Assert.Equal(1, store.Concurrent);

        store.Release();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, store.MaxConcurrent);
        // 每次布置都读一次仓库来比对指纹（串行地）；第二次比对一致，不再复制。
        Assert.Equal(2, store.Calls);
        Assert.True(File.Exists(Path.Combine(state.SkillsPath, "gated", "scripts", "run.py")));
        Assert.True(File.Exists(Path.Combine(state.ThreadDirectory, ".skills_wired")));
    }

    private sealed class GatedSkillStore : ISkillStore
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _concurrent;
        private int _maxConcurrent;
        private int _calls;

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Concurrent => Volatile.Read(ref _concurrent);
        public int MaxConcurrent => Volatile.Read(ref _maxConcurrent);
        public int Calls => Volatile.Read(ref _calls);

        public void Release() => _gate.TrySetResult();

        public async Task<List<SkillDefinition>> GetAllAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _calls);
            var now = Interlocked.Increment(ref _concurrent);
            InterlockedMax(ref _maxConcurrent, now);
            Entered.TrySetResult();
            try
            {
                await _gate.Task;
                return
                [
                    new SkillDefinition
                    {
                        Slug = "gated",
                        Name = "Gated",
                        Resources = new Dictionary<string, string> { ["scripts/run.py"] = "print('hi')" }
                    }
                ];
            }
            finally
            {
                Interlocked.Decrement(ref _concurrent);
            }
        }

        public Task<SkillDefinition?> GetBySlugAsync(string slug, CancellationToken ct = default)
            => Task.FromResult<SkillDefinition?>(null);

        private static void InterlockedMax(ref int target, int value)
        {
            int current;
            while ((current = Volatile.Read(ref target)) < value
                   && Interlocked.CompareExchange(ref target, value, current) != current)
            {
            }
        }
    }

    [Fact]
    public async Task Provision_MarkerWithRealDirectory_AndUnchangedResources_DoesNotCopyAgain()
    {
        var options = SandboxOptions(_root);
        var store = new CountingSkillStore();
        var provisioner = CreateProvisioner(options, store);
        var state = State(options);

        await provisioner.ProvisionAsync(_threadId, state);
        var copied = Path.Combine(state.SkillsPath, store.Slug, "scripts", "run.py");
        var marker = Path.Combine(state.ThreadDirectory, ".skills_wired");
        var past = DateTime.UtcNow.AddHours(-1);
        File.SetLastWriteTimeUtc(copied, past);
        var markerText = File.ReadAllText(marker);

        await provisioner.ProvisionAsync(_threadId, state);

        Assert.Equal(past, File.GetLastWriteTimeUtc(copied));
        Assert.Equal(markerText, File.ReadAllText(marker));
    }

    /// <summary>
    /// 技能仓库里的资源变了（管理员修了脚本），已经接过线的线程下一次用到沙箱就换上新的副本。
    /// 此前哨兵只记时间，线程一旦接过线就永远用那一刻的副本。
    /// </summary>
    [Fact]
    public async Task Provision_AfterAResourceChanges_RefreshesTheCopy()
    {
        var options = SandboxOptions(_root);
        var store = new MutableSkillStore();
        store.Set("report", ("scripts/build.py", "print('v1')"));
        var provisioner = CreateProvisioner(options, store);
        var state = State(options);

        await provisioner.ProvisionAsync(_threadId, state);
        store.Set("report", ("scripts/build.py", "print('v2')"), ("scripts/extra.py", "pass"));
        await provisioner.ProvisionAsync(_threadId, state);

        Assert.Equal("print('v2')", File.ReadAllText(Path.Combine(state.SkillsPath, "report", "scripts", "build.py")));
        Assert.True(File.Exists(Path.Combine(state.SkillsPath, "report", "scripts", "extra.py")));
    }

    [Fact]
    public async Task Provision_AfterASkillIsRemoved_ItsCopyIsGone()
    {
        var options = SandboxOptions(_root);
        var store = new MutableSkillStore();
        store.Set("keep", ("run.py", "1"));
        store.Set("drop", ("run.py", "2"));
        var provisioner = CreateProvisioner(options, store);
        var state = State(options);

        await provisioner.ProvisionAsync(_threadId, state);
        store.Remove("drop");
        await provisioner.ProvisionAsync(_threadId, state);

        Assert.True(File.Exists(Path.Combine(state.SkillsPath, "keep", "run.py")));
        Assert.False(Directory.Exists(Path.Combine(state.SkillsPath, "drop")));
    }

    /// <summary>这一版之前写的哨兵只有时间，没有指纹：按「已变化」重新复制一次，之后就按指纹比对。</summary>
    [Fact]
    public async Task Provision_MarkerWithoutAFingerprint_IsRecopiedOnce()
    {
        var options = SandboxOptions(_root);
        var store = new CountingSkillStore();
        var provisioner = CreateProvisioner(options, store);
        var state = State(options);
        Directory.CreateDirectory(Path.Combine(state.SkillsPath, "removed-long-ago"));
        File.WriteAllText(Path.Combine(state.SkillsPath, "removed-long-ago", "old.py"), "stale");
        var marker = Path.Combine(state.ThreadDirectory, ".skills_wired");
        File.WriteAllText(marker, "copied at 2026-01-01T00:00:00.0000000Z");

        await provisioner.ProvisionAsync(_threadId, state);

        Assert.False(Directory.Exists(Path.Combine(state.SkillsPath, "removed-long-ago")));
        Assert.True(File.Exists(Path.Combine(state.SkillsPath, store.Slug, "scripts", "run.py")));
        Assert.Contains("fingerprint: ", File.ReadAllText(marker));
    }

    private sealed class MutableSkillStore : ISkillStore
    {
        private readonly Dictionary<string, Dictionary<string, string>> _skills = new(StringComparer.Ordinal);

        public void Set(string slug, params (string Path, string Content)[] resources)
            => _skills[slug] = resources.ToDictionary(r => r.Path, r => r.Content);

        public void Remove(string slug) => _skills.Remove(slug);

        public Task<List<SkillDefinition>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult(_skills.Select(kv => new SkillDefinition
            {
                Slug = kv.Key,
                Name = kv.Key,
                Resources = new Dictionary<string, string>(kv.Value)
            }).ToList());

        public Task<SkillDefinition?> GetBySlugAsync(string slug, CancellationToken ct = default)
            => Task.FromResult<SkillDefinition?>(null);
    }

    [Fact]
    public async Task Provision_WithoutSkillStore_DoesNotWriteAMarker()
    {
        var options = SandboxOptions(_root);
        var provisioner = CreateProvisioner(options);
        var state = State(options);

        await provisioner.ProvisionAsync(_threadId, state);

        Assert.False(File.Exists(Path.Combine(state.ThreadDirectory, ".skills_wired")));
    }

    // -------------------------------------------------------------------------
    // 升级迁移：2026-05-22 → 2026-09-12 之间接线的线程，skills/ 是指向
    // {DataRoot}/_skills 的符号链接，且 .skills_wired 已存在。围栏解析链接之后
    // 把它判成越界，所以哨兵不能再被当成"已接好"：链接必须换成真目录。
    // -------------------------------------------------------------------------

    [SymlinkFact]
    public async Task Provision_LegacySymlinkedSkillsDir_IsReplacedByRealDirectory()
    {
        var options = SandboxOptions(_root);
        var store = new CountingSkillStore();
        var provisioner = CreateProvisioner(options, store);
        var state = State(options);
        new VirtualPathTranslator(_root).EnsureThreadDirectories(_threadId);

        var sharedRoot = Path.Combine(_root, "_skills");
        Directory.CreateDirectory(Path.Combine(sharedRoot, "legacy"));
        File.WriteAllText(Path.Combine(sharedRoot, "legacy", "old.txt"), "shared");
        if (Directory.Exists(state.SkillsPath)) Directory.Delete(state.SkillsPath);
        Directory.CreateSymbolicLink(state.SkillsPath, sharedRoot);
        var marker = Path.Combine(state.ThreadDirectory, ".skills_wired");
        File.WriteAllText(marker, $"linked->{sharedRoot}");

        await provisioner.ProvisionAsync(_threadId, state);

        Assert.Null(new DirectoryInfo(state.SkillsPath).LinkTarget);
        Assert.True(Directory.Exists(state.SkillsPath));
        Assert.True(File.Exists(Path.Combine(state.SkillsPath, store.Slug, "scripts", "run.py")));
        Assert.StartsWith("copied at", File.ReadAllText(marker));
        // 换掉的是链接本身，链接目标里的文件不能被删
        Assert.True(File.Exists(Path.Combine(sharedRoot, "legacy", "old.txt")));
    }

    [SymlinkFact]
    public async Task Provision_LegacyDanglingSymlinkedSkillsDir_IsReplacedByRealDirectory()
    {
        var options = SandboxOptions(_root);
        var store = new CountingSkillStore();
        var provisioner = CreateProvisioner(options, store);
        var state = State(options);
        new VirtualPathTranslator(_root).EnsureThreadDirectories(_threadId);

        // 文档曾建议"残留的 _skills/ 可直接删掉"：链接目标已不存在
        if (Directory.Exists(state.SkillsPath)) Directory.Delete(state.SkillsPath);
        Directory.CreateSymbolicLink(state.SkillsPath, Path.Combine(_root, "_skills"));
        File.WriteAllText(Path.Combine(state.ThreadDirectory, ".skills_wired"), "linked->gone");

        await provisioner.ProvisionAsync(_threadId, state);

        Assert.Null(new DirectoryInfo(state.SkillsPath).LinkTarget);
        Assert.True(File.Exists(Path.Combine(state.SkillsPath, store.Slug, "scripts", "run.py")));
    }

    /// <summary>
    /// 哨兵若是链接（线程根对容器用户可写的部署里，容器能把它换成指向宿主文件的链接），本进程读写它就是读写链接目标：
    /// 技能一变化，刷新时的写入会以本进程身份覆盖那个宿主文件。只删链接本身，按未接线重来。
    /// </summary>
    [SymlinkFact]
    public async Task Provision_MarkerThatIsALink_IsReplaced_AndItsTargetIsNotWritten()
    {
        var options = SandboxOptions(_root);
        var store = new CountingSkillStore();
        var provisioner = CreateProvisioner(options, store);
        var state = State(options);
        new VirtualPathTranslator(_root).EnsureThreadDirectories(_threadId);

        var outside = Path.Combine(_root, "_host");
        Directory.CreateDirectory(outside);
        var hostFile = Path.Combine(outside, "config.json");
        File.WriteAllText(hostFile, "host file");
        var marker = Path.Combine(state.ThreadDirectory, ".skills_wired");
        File.CreateSymbolicLink(marker, hostFile);

        await provisioner.ProvisionAsync(_threadId, state);

        Assert.Equal("host file", File.ReadAllText(hostFile));
        Assert.Null(new FileInfo(marker).LinkTarget);
        Assert.StartsWith("copied at", File.ReadAllText(marker));
    }
}
