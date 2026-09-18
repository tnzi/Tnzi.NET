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

    [Fact]
    public async Task Provision_MarkerWithRealDirectory_DoesNotCopyAgain()
    {
        var options = SandboxOptions(_root);
        var store = new CountingSkillStore();
        var provisioner = CreateProvisioner(options, store);
        var state = State(options);

        await provisioner.ProvisionAsync(_threadId, state);
        await provisioner.ProvisionAsync(_threadId, state);

        Assert.Equal(1, store.GetAllCalls);
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
}
