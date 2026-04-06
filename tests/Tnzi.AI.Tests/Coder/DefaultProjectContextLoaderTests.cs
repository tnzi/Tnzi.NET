using Tnzi.AI.Coder.Options;
using Tnzi.AI.Coder.ProjectContext;
using Tnzi.AI.ProjectContext;

namespace Tnzi.AI.Tests.Coder;

/// <summary>
/// DefaultProjectContextLoader 单元测试 — 指令文件加载、YAML frontmatter 解析、多文件合并
/// </summary>
public class DefaultProjectContextLoaderTests : IDisposable
{
    private readonly string _projectRoot;

    public DefaultProjectContextLoaderTests()
    {
        var baseTempDir = Path.Combine(AppContext.BaseDirectory, ".tmp", "project-context");
        Directory.CreateDirectory(baseTempDir);
        _projectRoot = Path.Combine(baseTempDir, $"tnzi-ctx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_projectRoot);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_projectRoot))
        {
            return;
        }

        try
        {
            Directory.Delete(_projectRoot, true);
        }
        catch (IOException)
        {
            // Best-effort cleanup only.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup only.
        }
    }

    private DefaultProjectContextLoader CreateLoader(Action<CoderOptions>? configure = null)
    {
        var options = new CoderOptions
        {
            ProjectRoot = _projectRoot,
            IncludeGlobalInstructionFiles = false
        };
        configure?.Invoke(options);
        return new DefaultProjectContextLoader(
            Microsoft.Extensions.Options.Options.Create(options),
            Mock.Of<ILogger<DefaultProjectContextLoader>>());
    }

    #region 基本加载

    [Fact]
    public async Task LoadAsync_NoFiles_ReturnsEmptyContext()
    {
        var loader = CreateLoader();

        var result = await loader.LoadAsync();

        result.ShouldNotBeNull();
        result.ProjectRoot.ShouldBe(Path.GetFullPath(_projectRoot));
        result.Instructions.ShouldBeNull();
        result.LoadedFiles.Count.ShouldBe(0);
    }

    [Fact]
    public async Task LoadAsync_SingleInstructionFile_LoadsContent()
    {
        File.WriteAllText(Path.Combine(_projectRoot, "TNZI.md"), "# Project Instructions\nDo something.");
        var loader = CreateLoader();

        var result = await loader.LoadAsync();

        result.Instructions.ShouldNotBeNull();
        result.Instructions.ShouldContain("Project Instructions");
        result.LoadedFiles.Count.ShouldBe(1);
    }

    [Fact]
    public async Task LoadAsync_MultipleInstructionFiles_MergesWithSeparator()
    {
        File.WriteAllText(Path.Combine(_projectRoot, "TNZI.md"), "File 1 content");
        File.WriteAllText(Path.Combine(_projectRoot, "AGENTS.md"), "File 2 content");

        var claudeDir = Path.Combine(_projectRoot, ".claude");
        Directory.CreateDirectory(claudeDir);
        File.WriteAllText(Path.Combine(claudeDir, "CLAUDE.md"), "File 3 content");

        var loader = CreateLoader();
        var result = await loader.LoadAsync();

        result.Instructions.ShouldNotBeNull();
        result.Instructions.ShouldContain("File 1 content");
        result.Instructions.ShouldContain("File 2 content");
        result.Instructions.ShouldContain("File 3 content");
        // 使用 \n---\n 分隔
        result.Instructions.ShouldContain("---");
        result.LoadedFiles.Count.ShouldBe(3);
    }

    #endregion

    #region YAML Frontmatter 解析

    [Fact]
    public async Task LoadAsync_WithFrontmatter_ParsesMetadata()
    {
        var content = """
            ---
            model: gpt-4
            temperature: 0.7
            ---
            # Instructions
            Use GPT-4 model.
            """;
        File.WriteAllText(Path.Combine(_projectRoot, "TNZI.md"), content);

        var loader = CreateLoader();
        var result = await loader.LoadAsync();

        result.Metadata.ShouldContainKey("model");
        result.Metadata["model"].ShouldBe("gpt-4");
        result.Metadata["temperature"].ShouldBe("0.7");
        // 内容不应包含 frontmatter
        result.Instructions.ShouldNotBeNull();
        result.Instructions.ShouldContain("Instructions");
        result.Instructions.ShouldNotContain("---\nmodel:");
    }

    [Fact]
    public async Task LoadAsync_FrontmatterWithoutClosingDelimiter_TreatedAsContent()
    {
        var content = """
            ---
            model: gpt-4
            Some random text without closing delimiter
            """;
        File.WriteAllText(Path.Combine(_projectRoot, "TNZI.md"), content);

        var loader = CreateLoader();
        var result = await loader.LoadAsync();

        // 没有关闭的 ---，整个内容作为普通文本
        result.Instructions.ShouldNotBeNull();
        result.Instructions.ShouldContain("model: gpt-4");
        result.Metadata.Count.ShouldBe(0);
    }

    [Fact]
    public async Task LoadAsync_NoFrontmatter_ReturnsAllContent()
    {
        File.WriteAllText(Path.Combine(_projectRoot, "TNZI.md"), "Plain content without frontmatter");

        var loader = CreateLoader();
        var result = await loader.LoadAsync();

        result.Instructions.ShouldNotBeNull();
        result.Instructions.ShouldBe("Plain content without frontmatter");
        result.Metadata.Count.ShouldBe(0);
    }

    [Fact]
    public async Task LoadAsync_MultipleFrontmatter_MergesMetadata()
    {
        var content1 = """
            ---
            model: gpt-4
            ---
            Content 1
            """;
        var content2 = """
            ---
            provider: openai
            model: claude-3
            ---
            Content 2
            """;

        File.WriteAllText(Path.Combine(_projectRoot, "TNZI.md"), content1);
        var tnziDir = Path.Combine(_projectRoot, ".tnzi");
        Directory.CreateDirectory(tnziDir);
        File.WriteAllText(Path.Combine(tnziDir, "TNZI.md"), content2);

        var loader = CreateLoader();
        var result = await loader.LoadAsync();

        // 后面的文件覆盖前面的同名 key
        result.Metadata["model"].ShouldBe("claude-3");
        result.Metadata["provider"].ShouldBe("openai");
    }

    #endregion

    #region 自定义 InstructionFiles

    [Fact]
    public async Task LoadAsync_CustomInstructionFiles_LoadsOnlySpecified()
    {
        File.WriteAllText(Path.Combine(_projectRoot, "TNZI.md"), "Should not load");
        File.WriteAllText(Path.Combine(_projectRoot, "custom.md"), "Custom content");

        var loader = CreateLoader(opts =>
        {
            opts.InstructionFiles = ["custom.md"];
        });

        var result = await loader.LoadAsync();

        result.Instructions.ShouldNotBeNull();
        result.Instructions.ShouldContain("Custom content");
        result.Instructions.ShouldNotContain("Should not load");
    }

    [Fact]
    public async Task LoadAsync_DefaultInstructionFiles_LoadsAgentsAndClaudeFiles()
    {
        File.WriteAllText(Path.Combine(_projectRoot, "AGENTS.md"), "Agents instructions");
        File.WriteAllText(Path.Combine(_projectRoot, "CLAUDE.md"), "Claude instructions");

        var loader = CreateLoader();
        var result = await loader.LoadAsync();

        result.Instructions.ShouldNotBeNull();
        result.Instructions.ShouldContain("Agents instructions");
        result.Instructions.ShouldContain("Claude instructions");
        result.LoadedFiles.Count.ShouldBe(2);
    }

    [Fact]
    public async Task LoadAsync_DuplicateInstructionFiles_AreDeduplicated()
    {
        File.WriteAllText(Path.Combine(_projectRoot, "AGENTS.md"), "Agents instructions");

        var loader = CreateLoader(opts =>
        {
            opts.InstructionFiles = ["AGENTS.md", ".\\AGENTS.md"];
        });

        var result = await loader.LoadAsync();

        result.LoadedFiles.Count.ShouldBe(1);
        result.Instructions.ShouldBe("Agents instructions");
    }

    #endregion

    #region 自定义 projectRoot 参数

    [Fact]
    public async Task LoadAsync_ExplicitProjectRoot_OverridesOptions()
    {
        var altRoot = Path.Combine(AppContext.BaseDirectory, ".tmp", "project-context", $"tnzi-alt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(altRoot);
        try
        {
            File.WriteAllText(Path.Combine(altRoot, "TNZI.md"), "Alt root content");
            var loader = CreateLoader();

            var result = await loader.LoadAsync(altRoot);

            result.ProjectRoot.ShouldBe(Path.GetFullPath(altRoot));
            result.Instructions.ShouldNotBeNull();
            result.Instructions.ShouldContain("Alt root content");
        }
        finally
        {
            try
            {
                Directory.Delete(altRoot, true);
            }
            catch (IOException)
            {
                // Best-effort cleanup only.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup only.
            }
        }
    }

    #endregion

    #region 错误处理

    [Fact]
    public async Task LoadAsync_UnreadableFile_SkipsAndContinues()
    {
        // 使用一个正常文件和一个不存在的引用来模拟
        File.WriteAllText(Path.Combine(_projectRoot, "TNZI.md"), "Valid content");

        var loader = CreateLoader(opts =>
        {
            opts.InstructionFiles = ["TNZI.md", "nonexistent.md"];
        });

        var result = await loader.LoadAsync();

        // 应该加载有效文件，跳过不存在的文件
        result.Instructions.ShouldNotBeNull();
        result.Instructions.ShouldContain("Valid content");
        result.LoadedFiles.Count.ShouldBe(1);
    }

    [Fact]
    public async Task LoadAsync_EmptyFile_SkipsContent()
    {
        File.WriteAllText(Path.Combine(_projectRoot, "TNZI.md"), "   ");

        var loader = CreateLoader();
        var result = await loader.LoadAsync();

        // 空白内容文件被跳过
        result.Instructions.ShouldBeNull();
    }

    #endregion
}
