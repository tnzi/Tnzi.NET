using Microsoft.Extensions.Options;
using Tnzi.AI.Sandbox.Middleware;
using Tnzi.AI.Sandbox.Tools;

namespace Tnzi.AI.Tests.Sandbox;

/// <summary>
/// Skills → Sandbox 端到端集成测试。
/// 验证完整链路：加载内置技能 → 提取 Resources → Sandbox 执行脚本。
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
    // 2. ThreadDataMiddleware 提取技能资源到线程目录
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExtractSkillResources_WritesFilesToThreadDirectory()
    {
        var store = CreateStorePointingToBuiltIn();
        var translator = new VirtualPathTranslator(_tempRoot);
        var middleware = new ThreadDataMiddleware(
            Microsoft.Extensions.Options.Options.Create(new SandboxModuleOptions { LazyDirectoryCreation = false }),
            translator,
            NullLogger<ThreadDataMiddleware>.Instance,
            store);

        // 模拟 middleware 的 SetupThreadDataAsync
        var threadDir = translator.GetThreadDirectory(_threadId);
        translator.EnsureThreadDirectories(_threadId);

        // 手动调用提取逻辑（通过 middleware pipeline 模拟）
        var context = CreateFakeMiddlewareContext(_threadId);
        var callCount = 0;
        await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            callCount++;
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });

        callCount.ShouldBe(1, "next() should be called once");

        // 验证 ThreadData 被设置
        context.Properties.ShouldContainKey(SandboxPropertyKeys.ThreadData);
        var state = (ThreadDataState)context.Properties[SandboxPropertyKeys.ThreadData];
        state.SkillsPath.ShouldNotBeNullOrWhiteSpace();

        // 验证 skills/ 目录有提取的文件
        var skillsDir = state.SkillsPath;
        Directory.Exists(skillsDir).ShouldBeTrue("Skills directory should exist after extraction");

        // .extracted 标记文件应该存在
        File.Exists(Path.Combine(skillsDir, ".extracted")).ShouldBeTrue("Extraction marker should exist");

        // 至少有一个技能的 scripts 被提取
        var extractedDirs = Directory.GetDirectories(skillsDir);
        extractedDirs.ShouldNotBeEmpty("At least one skill directory should be extracted");
    }

    [Fact]
    public async Task ExtractSkillResources_DataAnalysisScript_Exists()
    {
        var store = CreateStorePointingToBuiltIn();
        var translator = new VirtualPathTranslator(_tempRoot);
        translator.EnsureThreadDirectories(_threadId);

        var middleware = new ThreadDataMiddleware(
            Microsoft.Extensions.Options.Options.Create(new SandboxModuleOptions { LazyDirectoryCreation = false }),
            translator,
            NullLogger<ThreadDataMiddleware>.Instance,
            store);

        var context = CreateFakeMiddlewareContext(_threadId);
        await middleware.InvokeAsync(context, (ctx, ct) =>
            Task.FromResult(new AgentRunResult { Response = "ok" }));

        var state = (ThreadDataState)context.Properties[SandboxPropertyKeys.ThreadData];
        var scriptPath = Path.Combine(state.SkillsPath, "data-analysis", "scripts", "analyze.py");
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
        translator.EnsureThreadDirectories(_threadId);

        var middleware = new ThreadDataMiddleware(
            Microsoft.Extensions.Options.Options.Create(new SandboxModuleOptions { LazyDirectoryCreation = false }),
            translator,
            NullLogger<ThreadDataMiddleware>.Instance,
            store);

        var context = CreateFakeMiddlewareContext(_threadId);
        await middleware.InvokeAsync(context, (ctx, ct) =>
            Task.FromResult(new AgentRunResult { Response = "ok" }));

        // 2. 创建 sandbox + tools（workspace 设为线程根目录，以便访问 skills/ 子目录）
        var threadDir = translator.GetThreadDirectory(_threadId);
        await using var sandbox = new LocalSandbox("test", threadDir,
            TimeSpan.FromSeconds(10), 1024);
        var tools = new SandboxTools(translator, NullLogger<SandboxTools>.Instance);

        // 3. 用 read_file 读取提取的脚本
        var result = await tools.ReadFileAsync(sandbox, _threadId, "/mnt/skills/data-analysis/scripts/analyze.py");
        var json = JsonSerializer.Serialize(result);
        json.ToLower().ShouldContain("duckdb");
    }

    [Fact]
    public async Task SandboxBash_CanListSkillScripts()
    {
        // 提取
        var store = CreateStorePointingToBuiltIn();
        var translator = new VirtualPathTranslator(_tempRoot);
        translator.EnsureThreadDirectories(_threadId);

        var middleware = new ThreadDataMiddleware(
            Microsoft.Extensions.Options.Options.Create(new SandboxModuleOptions { LazyDirectoryCreation = false }),
            translator,
            NullLogger<ThreadDataMiddleware>.Instance,
            store);

        var context = CreateFakeMiddlewareContext(_threadId);
        await middleware.InvokeAsync(context, (ctx, ct) =>
            Task.FromResult(new AgentRunResult { Response = "ok" }));

        // ls skills/（workspace 设为线程根目录）
        var threadDir = translator.GetThreadDirectory(_threadId);
        await using var sandbox = new LocalSandbox("test", threadDir,
            TimeSpan.FromSeconds(10), 1024);
        var tools = new SandboxTools(translator, NullLogger<SandboxTools>.Instance);

        var result = await tools.ListDirectoryAsync(sandbox, _threadId, "/mnt/skills");
        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("data-analysis");
    }

    [Fact]
    public async Task SandboxBash_TranslatesSkillPaths()
    {
        // 提取
        var store = CreateStorePointingToBuiltIn();
        var translator = new VirtualPathTranslator(_tempRoot);
        translator.EnsureThreadDirectories(_threadId);

        var middleware = new ThreadDataMiddleware(
            Microsoft.Extensions.Options.Options.Create(new SandboxModuleOptions { LazyDirectoryCreation = false }),
            translator,
            NullLogger<ThreadDataMiddleware>.Instance,
            store);

        var context = CreateFakeMiddlewareContext(_threadId);
        await middleware.InvokeAsync(context, (ctx, ct) =>
            Task.FromResult(new AgentRunResult { Response = "ok" }));

        // 用 bash cat 读取技能脚本（验证 /mnt/skills 路径被正确翻译）
        var threadDir = translator.GetThreadDirectory(_threadId);
        await using var sandbox = new LocalSandbox("test", Path.Combine(threadDir, "workspace"),
            TimeSpan.FromSeconds(10), 4096);
        var tools = new SandboxTools(translator, NullLogger<SandboxTools>.Instance);

        var result = await tools.BashAsync(sandbox, _threadId,
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
        translator.EnsureThreadDirectories(_threadId);

        var middleware = new ThreadDataMiddleware(
            Microsoft.Extensions.Options.Options.Create(new SandboxModuleOptions { LazyDirectoryCreation = false }),
            translator,
            NullLogger<ThreadDataMiddleware>.Instance,
            store);

        var context = CreateFakeMiddlewareContext(_threadId);
        await middleware.InvokeAsync(context, (ctx, ct) =>
            Task.FromResult(new AgentRunResult { Response = "ok" }));

        // 写一个简单的测试脚本到 workspace 并执行
        var threadDir = translator.GetThreadDirectory(_threadId);
        await using var sandbox = new LocalSandbox("test", Path.Combine(threadDir, "workspace"),
            TimeSpan.FromSeconds(10), 4096);
        var tools = new SandboxTools(translator, NullLogger<SandboxTools>.Instance);

        await tools.WriteFileAsync(sandbox, _threadId, "/mnt/workspace/test.sh", "#!/bin/bash\necho 'skill-pipeline-ok'");
        var result = await tools.BashAsync(sandbox, _threadId, "bash /mnt/workspace/test.sh");
        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("skill-pipeline-ok");
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
        var options = Microsoft.Extensions.Options.Options.Create(new AIOptions
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

    private static AiMiddlewareContext CreateFakeMiddlewareContext(Guid threadId)
    {
        return new AiMiddlewareContext
        {
            Request = new AgentRunRequest
            {
                UserMessage = "test",
                ThreadId = threadId
            },
            Agent = AgentResolution.Success(
                agent: null!,
                provider: "TestProvider",
                model: "test-model",
                agentId: null),
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
            Messages = []
        };
    }
}
