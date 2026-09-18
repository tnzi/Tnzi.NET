using Tnzi.AI.Sandbox.Services;
using Tnzi.AI.Sandbox.Tools;

namespace Tnzi.AI.Tests.Sandbox;

/// <summary>
/// Skills → Sandbox 端到端集成测试。
/// 验证完整链路：加载内置技能 → 沙箱首次使用时提取 Resources → Sandbox 执行脚本。
/// </summary>
public class SkillSandboxPipelineTests : IAsyncLifetime
{
    private string _tempRoot = null!;
    private readonly Guid _threadId = Guid.NewGuid();

    public Task InitializeAsync()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"tnzi-pipeline-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
        return Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // 1. FileSystemSkillStore 加载内置技能并包含 Resources
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LoadBuiltInSkills_AllHaveNameAndDescription()
    {
        var store = CreateStorePointingToBuiltIn();
        var skills = await store.GetAllAsync();

        skills.ShouldNotBeEmpty();
        foreach (var skill in skills)
        {
            skill.Name.ShouldNotBeNullOrWhiteSpace($"Skill '{skill.Slug}' missing name");
            skill.Description.ShouldNotBeNullOrWhiteSpace($"Skill '{skill.Name}' missing description");
            skill.Slug.ShouldNotBeNullOrWhiteSpace($"Skill '{skill.Name}' missing slug");
        }
    }

    [Fact]
    public async Task LoadBuiltInSkills_SkillsWithScripts_HaveResources()
    {
        var store = CreateStorePointingToBuiltIn();
        var skills = await store.GetAllAsync();

        // 至少 data-analysis, chart-visualization 等有 scripts/
        var skillsWithResources = skills.Where(s => s.Resources.Count > 0).ToList();
        skillsWithResources.ShouldNotBeEmpty("Expected at least one skill with script resources");

        foreach (var skill in skillsWithResources)
        {
            foreach (var (relPath, content) in skill.Resources)
            {
                relPath.ShouldNotBeNullOrWhiteSpace();
                // __init__.py 是合法的空文件（Python 包标记）
                if (!relPath.EndsWith("__init__.py"))
                    content.ShouldNotBeNullOrWhiteSpace($"Resource '{relPath}' in skill '{skill.Slug}' is empty");
                relPath.ShouldContain("/");
            }
        }
    }

    // -------------------------------------------------------------------------
    // 2. ThreadDataProvisioner 提取技能资源到线程目录（沙箱首次被用到时）
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExtractSkillResources_WritesFilesToThreadDirectory()
    {
        var store = CreateStorePointingToBuiltIn();
        var translator = new VirtualPathTranslator(_tempRoot);
        var state = await ProvisionAsync(translator, store);
        state.SkillsPath.ShouldNotBeNullOrWhiteSpace();

        // 验证 skills/ 目录有提取的文件
        var skillsDir = state.SkillsPath;
        Directory.Exists(skillsDir).ShouldBeTrue("Skills directory should exist after extraction");

        // 完成标记位于父线程目录 (.skills_wired)，避免在 symlink 模式下把
        // 标记写入共享只读目录污染其他 thread。
        File.Exists(Path.Combine(state.ThreadDirectory, ".skills_wired")).ShouldBeTrue("Wiring marker should exist");

        // 至少有一个技能的 scripts 被提取
        var extractedDirs = Directory.GetDirectories(skillsDir);
        extractedDirs.ShouldNotBeEmpty("At least one skill directory should be extracted");
    }

    [Fact]
    public async Task ExtractSkillResources_DataAnalysisScript_Exists()
    {
        var store = CreateStorePointingToBuiltIn();
        var translator = new VirtualPathTranslator(_tempRoot);
        await ProvisionAsync(translator, store);

        var scriptPath = Path.Combine(translator.GetThreadDirectory(_threadId), "skills", "data-analysis", "scripts", "analyze.py");
        File.Exists(scriptPath).ShouldBeTrue($"data-analysis/scripts/analyze.py should be extracted to {scriptPath}");

        var content = await File.ReadAllTextAsync(scriptPath);
        content.ToLower().ShouldContain("duckdb");
    }

    // -------------------------------------------------------------------------
    // 3. Sandbox 执行提取的脚本
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SandboxBash_CanReadExtractedSkillScript()
    {
        // 1. 提取技能资源
        var store = CreateStorePointingToBuiltIn();
        var translator = new VirtualPathTranslator(_tempRoot);
        await ProvisionAsync(translator, store);

        // 2. 创建 sandbox + tools（workspace 设为线程根目录，以便访问 skills/ 子目录）
        var threadDir = translator.GetThreadDirectory(_threadId);
        await using var sandbox = new LocalSandbox("test", threadDir,
            TimeSpan.FromSeconds(10), 1024);
        var tools = CreateTools(translator, sandbox);

        // 3. 用 read_file 读取提取的脚本
        var result = await tools.ReadFileAsync("/mnt/skills/data-analysis/scripts/analyze.py");
        var json = JsonSerializer.Serialize(result);
        json.ToLower().ShouldContain("duckdb");
    }

    [Fact]
    public async Task SandboxBash_CanListSkillScripts()
    {
        // 提取
        var store = CreateStorePointingToBuiltIn();
        var translator = new VirtualPathTranslator(_tempRoot);
        await ProvisionAsync(translator, store);

        // ls skills/（workspace 设为线程根目录）
        var threadDir = translator.GetThreadDirectory(_threadId);
        await using var sandbox = new LocalSandbox("test", threadDir,
            TimeSpan.FromSeconds(10), 1024);
        var tools = CreateTools(translator, sandbox);

        var result = await tools.ListDirectoryAsync("/mnt/skills");
        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("data-analysis");
    }

    [Fact]
    public async Task SandboxBash_TranslatesSkillPaths()
    {
        // 提取
        var store = CreateStorePointingToBuiltIn();
        var translator = new VirtualPathTranslator(_tempRoot);
        await ProvisionAsync(translator, store);

        // 用 bash cat 读取技能脚本（验证 /mnt/skills 路径被正确翻译）
        var threadDir = translator.GetThreadDirectory(_threadId);
        await using var sandbox = new LocalSandbox("test", Path.Combine(threadDir, "workspace"),
            TimeSpan.FromSeconds(10), 4096);
        var tools = CreateTools(translator, sandbox);

        var result = await tools.BashAsync(
            "cat /mnt/skills/data-analysis/scripts/analyze.py | head -5");
        var json = JsonSerializer.Serialize(result);
        json.ShouldNotContain("No such file");
    }

    [Fact]
    public async Task SandboxBash_CanExecuteSimpleScript()
    {
        // 提取
        var store = CreateStorePointingToBuiltIn();
        var translator = new VirtualPathTranslator(_tempRoot);
        await ProvisionAsync(translator, store);

        // 写一个简单的测试脚本到 workspace 并执行
        var threadDir = translator.GetThreadDirectory(_threadId);
        await using var sandbox = new LocalSandbox("test", Path.Combine(threadDir, "workspace"),
            TimeSpan.FromSeconds(10), 4096);
        var tools = CreateTools(translator, sandbox);

        if (OperatingSystem.IsWindows())
        {
            await tools.WriteFileAsync("/mnt/workspace/test.cmd", "@echo off\r\necho skill-pipeline-ok\r\n");
        }
        else
        {
            await tools.WriteFileAsync("/mnt/workspace/test.sh", "#!/bin/bash\necho 'skill-pipeline-ok'");
        }

        var command = OperatingSystem.IsWindows()
            ? "call /mnt/workspace/test.cmd"
            : "bash /mnt/workspace/test.sh";

        var result = await tools.BashAsync(command);
        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("skill-pipeline-ok");
    }

    // -------------------------------------------------------------------------
    // 3b. skills/ 必须是线程目录里的真目录，不能是指向线程目录之外的链接
    // -------------------------------------------------------------------------

    /// <summary>
    /// 2026-09-04 起围栏会解析符号链接再判定，于是任何把 <c>skills/</c> 链到线程目录之外
    /// （例如启动时提取的共享根 <c>{DataRoot}/_skills</c>）的接线，都会让 <c>read_file</c> / <c>ls</c>
    /// 在 <c>/mnt/skills</c> 上报 "Path traversal detected"。这里先摆好一个"启动时已提取"的共享根，
    /// 逼出旧的链接分支：修复后接线一律逐线程复制，共享根只是一个无关的旁路目录。
    /// </summary>
    [Fact]
    public async Task ReadFile_AndLs_OnSkills_SucceedEvenWhenASharedRootExists()
    {
        var store = CreateStorePointingToBuiltIn();
        var translator = new VirtualPathTranslator(_tempRoot);
        translator.EnsureThreadDirectories(_threadId);

        // 模拟启动时的共享提取：共享根 + 标记文件 + 一份技能脚本。
        var sharedRoot = Path.Combine(_tempRoot, "_skills");
        Directory.CreateDirectory(Path.Combine(sharedRoot, "data-analysis", "scripts"));
        await File.WriteAllTextAsync(Path.Combine(sharedRoot, "data-analysis", "scripts", "analyze.py"), "import duckdb");
        await File.WriteAllTextAsync(Path.Combine(sharedRoot, ".extracted"), "1 skills");

        var state = await ProvisionAsync(translator, store);
        new DirectoryInfo(state.SkillsPath).LinkTarget.ShouldBeNull(
            "skills/ must be a real directory inside the thread directory, never a link that leaves it");

        var threadDir = translator.GetThreadDirectory(_threadId);
        await using var sandbox = new LocalSandbox("test", threadDir, TimeSpan.FromSeconds(10), 4096);
        var tools = CreateTools(translator, sandbox);

        var read = JsonSerializer.Serialize(await tools.ReadFileAsync("/mnt/skills/data-analysis/scripts/analyze.py"));
        read.ShouldNotContain("Path traversal");
        read.ToLower().ShouldContain("duckdb");

        var listed = JsonSerializer.Serialize(await tools.ListDirectoryAsync("/mnt/skills"));
        listed.ShouldNotContain("Path traversal");
        listed.ShouldContain("data-analysis");
    }

    // -------------------------------------------------------------------------
    // 4. 虚拟路径翻译完整性
    // -------------------------------------------------------------------------

    [Fact]
    public void VirtualPathTranslator_SkillsPath_TranslatesCorrectly()
    {
        var translator = new VirtualPathTranslator(_tempRoot);
        var physical = translator.ToPhysical("/mnt/skills/data-analysis/scripts/analyze.py", _threadId);

        physical.ShouldContain("skills");
        physical.ShouldContain("data-analysis");
        physical.ShouldContain("analyze.py");
        physical.ShouldNotContain("/mnt/");
    }

    [Fact]
    public void VirtualPathTranslator_IsValidVirtualPath_AcceptsSkillsPaths()
    {
        var translator = new VirtualPathTranslator(_tempRoot);
        translator.IsValidVirtualPath("/mnt/skills/my-skill/scripts/run.py").ShouldBeTrue();
        translator.IsValidVirtualPath("/mnt/workspace/file.txt").ShouldBeTrue();
        translator.IsValidVirtualPath("/mnt/uploads/data.csv").ShouldBeTrue();
        translator.IsValidVirtualPath("/mnt/outputs/result.json").ShouldBeTrue();
        translator.IsValidVirtualPath("/mnt/invalid/path").ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private FileSystemSkillStore CreateStorePointingToBuiltIn()
    {
        var builtInPath = FindBuiltInSkillsPath();
        var options = new StaticOptionsMonitor<AIOptions>(new AIOptions
        {
            ContextProviders = new ContextProvidersOptions
            {
                Skills = new SkillsOptions
                {
                    Paths = [builtInPath],
                    LoadBuiltIn = false
                }
            }
        });

        return new FileSystemSkillStore(
            NullLogger<FileSystemSkillStore>.Instance,
            options);
    }

    private static string FindBuiltInSkillsPath()
    {
        // 从测试运行目录向上查找 src/Tnzi.AI.Skills/Skills/BuiltIn
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "src", "Tnzi.AI.Skills", "Skills", "BuiltIn");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        // Fallback: 硬编码路径（CI 环境）
        var fallback = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "Tnzi.AI.Skills", "Skills", "BuiltIn"));
        if (Directory.Exists(fallback)) return fallback;

        throw new DirectoryNotFoundException("Cannot find BuiltIn skills directory");
    }

    /// <summary>
    /// 创建 SandboxTools 并在当前测试的异步流上发布沙箱环境
    /// （模拟 SandboxMiddleware 的发布动作）。
    /// </summary>
    private SandboxTools CreateTools(VirtualPathTranslator translator, ISandbox sandbox)
    {
        var accessor = new AgentExecutionContextAccessor();
        accessor.Properties[SandboxPropertyKeys.ToolEnvironment] =
            new SandboxToolEnvironment(sandbox, _threadId);
        return new SandboxTools(translator, NullLogger<SandboxTools>.Instance, accessor);
    }

    /// <summary>
    /// 布置线程目录（含技能资源复制）；生产里由 SandboxMiddleware 发布的环境在沙箱首次被用到时做同一件事。
    /// </summary>
    private async Task<ThreadDataState> ProvisionAsync(VirtualPathTranslator translator, ISkillStore store)
    {
        var options = new SandboxModuleOptions { LazyDirectoryCreation = false, DataRoot = _tempRoot };
        var provisioner = new ThreadDataProvisioner(
            Microsoft.Extensions.Options.Options.Create(options),
            translator,
            NullLogger<ThreadDataProvisioner>.Instance,
            store);
        var state = ThreadDataState.FromThreadDirectory(translator.GetThreadDirectory(_threadId));
        await provisioner.ProvisionAsync(_threadId, state);
        return state;
    }
}
