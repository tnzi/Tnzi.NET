namespace Tnzi.Tests.Architecture;

/// <summary>
/// 外部 CLI agent 执行域的三条红线门禁。
/// </summary>
/// <remarks>
/// <para>
/// 这组测试守的不是风格，而是一次<b>已经发生过</b>的架构失败。上一代
/// <c>Tnzi.AI.Cli</c>（≤ 0.1.26）把外部执行做成 <c>AgentExecutionMode</c> 的一个取值，
/// 于是它必须和内建执行共用同一条中间件管线；每个不适用的中间件都要一个「跳过」开关，
/// 最终 15 个中间件里散落 28 处补丁，模块整体删除
/// （归档 tag <c>archive/pre-ai-client-removal</c>）。
/// </para>
/// <para>
/// 那次失败的直接原因是<b>没有门禁</b>：skip 开关是一处一处加上去的，每一处单看都合理。
/// 所以本轮重新设计的第一件事就是把红线变成会红的测试。
/// </para>
/// </remarks>
public class CliAgentRedLineTests
{
    /// <summary>
    /// 红线 1a：执行模式枚举里不得出现外部 CLI 取值。
    /// </summary>
    [Fact]
    public void AgentExecutionMode_MustNotGainAnExternalCliValue()
    {
        var repoRoot = FindRepoRoot();
        Assert.NotNull(repoRoot);

        var path = Path.Combine(repoRoot!, "src", "Tnzi.AI", "Metadata", "AgentExecutionMode.cs");
        Assert.True(File.Exists(path), $"expected {path} to exist");

        var content = File.ReadAllText(path);

        // 外部执行是与 IAgentExecutor **平级**的独立执行域，不是它的一个模式。
        // 一旦它变成模式取值，中间件管线就必须为它做例外 —— 那是上一版的死因。
        Assert.DoesNotContain("ExternalCli", content, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 红线 1b：中间件上下文不得出现任何「跳过中间件」类开关。
    /// </summary>
    [Fact]
    public void AiMiddlewareContext_MustNotGainSkipSwitches()
    {
        var repoRoot = FindRepoRoot();
        Assert.NotNull(repoRoot);

        var middlewareDirectory = Path.Combine(repoRoot!, "src", "Tnzi.AI", "Middleware");
        Assert.True(Directory.Exists(middlewareDirectory), $"expected {middlewareDirectory} to exist");

        var offenders = Directory.EnumerateFiles(middlewareDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file)
                .Contains("ShouldSkipMiddleware", StringComparison.OrdinalIgnoreCase))
            .Select(file => Path.GetRelativePath(repoRoot!, file))
            .ToList();

        Assert.True(offenders.Count == 0,
            "a per-middleware skip switch is how the previous external-CLI attempt accumulated 28 patches across 15 middlewares; "
            + $"external execution must stay outside the pipeline instead. Offenders: {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// 红线 2：「内建 vs 外部」的分支只允许出现在路由门面一处。
    /// </summary>
    /// <remarks>
    /// 判据是「同一个文件里同时引用 <c>IAgentRuntime</c> 与 <c>ICliAgentDispatcher</c>」——
    /// 那正是做出路由判断所必需的条件。允许的只有门面本身、它的契约、以及 NoOp 回退。
    /// </remarks>
    [Fact]
    public void OnlyTheDispatchFacadeMayBridgeBuiltInAndExternalExecution()
    {
        var repoRoot = FindRepoRoot();
        Assert.NotNull(repoRoot);

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // 门面本身 —— 唯一允许的分支点。
            Path.Combine("src", "Tnzi.AI", "Services", "AgentDispatchFacade.cs"),
            // 门面的契约：XML 注释里必须能指向两条路径来解释自己存在的理由。
            Path.Combine("src", "Tnzi.AI", "Services", "Interfaces", "IAgentDispatchFacade.cs"),
            Path.Combine("src", "Tnzi.AI", "Services", "Interfaces", "ICliAgentDispatcher.cs"),
            // 组合根必然同时提到两者（各注册一次）。它做的是**装配**不是**判断**：
            // 这里没有任何 if，两条路径都被无条件注册进容器。
            Path.Combine("src", "Tnzi.AI", "AIModule.Registration.cs")
        };

        var offenders = EnumerateFrameworkSources(repoRoot!)
            .Where(file =>
            {
                var content = File.ReadAllText(file);
                return content.Contains("IAgentRuntime", StringComparison.Ordinal)
                       && content.Contains("ICliAgentDispatcher", StringComparison.Ordinal);
            })
            .Select(file => Path.GetRelativePath(repoRoot!, file))
            .Where(relative => !allowed.Contains(relative))
            .ToList();

        Assert.True(offenders.Count == 0,
            "the built-in vs external routing decision must exist in exactly one place (IAgentDispatchFacade); "
            + $"unexpected bridges: {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// 红线 3：外部执行域不得复用沙箱的批量执行契约或 Channels 的入站网关。
    /// </summary>
    /// <remarks>
    /// 两者都「看起来能复用」，实际都装不下：<c>ISandbox.ExecuteCommandAsync</c> 批量返回
    /// <c>CommandResult</c>，没有流式也没有 stdin 交互，装不下 ACP 的双向 JSON-RPC；
    /// <c>IGateway</c> 的语义是「入站消息 → 路由到 Agent」，不是「运行时注册 + 任务下发」，
    /// 塞进去就是红线 1 的翻版。
    /// </remarks>
    [Fact]
    public void CliModule_MustNotReuseSandboxExecutionOrChannelsGateway()
    {
        var repoRoot = FindRepoRoot();
        Assert.NotNull(repoRoot);

        var moduleDirectory = Path.Combine(repoRoot!, "src", "Tnzi.AI.Cli");
        Assert.True(Directory.Exists(moduleDirectory), $"expected {moduleDirectory} to exist");

        var forbidden = new[] { "ISandbox", "IGateway", "ISandboxProvider" };

        var offenders = Directory.EnumerateFiles(moduleDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                           && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(file => (File: Path.GetRelativePath(repoRoot!, file), Content: File.ReadAllText(file)))
            // XML 注释里解释「为什么不用它」是允许的；出现在代码里才是违规。
            .Where(entry => forbidden.Any(symbol => ContainsOutsideComments(entry.Content, symbol)))
            .Select(entry => entry.File)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Tnzi.AI.Cli must not consume the sandbox execution contract or the Channels gateway: {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// 描述表里存在 ≠ 可用：无适配器实现的协议必须能被诚实地报出来。
    /// </summary>
    [Fact]
    public void ProviderCatalogue_ExposesWhetherEachProtocolIsImplemented()
    {
        var repoRoot = FindRepoRoot();
        Assert.NotNull(repoRoot);

        var factory = Path.Combine(repoRoot!, "src", "Tnzi.AI.Cli", "Adapters", "CliProtocolAdapterFactory.cs");
        Assert.True(File.Exists(factory), $"expected {factory} to exist");
        Assert.Contains("IsImplemented", File.ReadAllText(factory), StringComparison.Ordinal);

        // 管理端下拉必须带上这一位，否则管理员会选中一个必然 501 的 provider。
        var dto = Path.Combine(repoRoot!, "src", "Tnzi.AI", "Dtos", "CliAgentDtos.cs");
        Assert.Contains("Implemented", File.ReadAllText(dto), StringComparison.Ordinal);
    }

    /// <summary>
    /// 枚举框架 C# 源码。
    /// </summary>
    /// <remarks>
    /// 刻意<b>不</b>对 <c>src/</c> 整棵树做 <c>AllDirectories</c>：<c>src/Tnzi.UI</c> 下有
    /// 前端 monorepo 的 <c>node_modules</c>，递归它会让测试宿主直接崩掉
    /// （实测就是这样发现的）。这里按项目目录逐个扫，并跳过 UI 与构建产物目录。
    /// </remarks>
    private static IEnumerable<string> EnumerateFrameworkSources(string repoRoot)
    {
        var srcRoot = Path.Combine(repoRoot, "src");

        foreach (var projectDirectory in Directory.EnumerateDirectories(srcRoot))
        {
            var name = Path.GetFileName(projectDirectory);
            if (string.Equals(name, "Tnzi.UI", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return file;
            }
        }
    }

    private static bool ContainsOutsideComments(string content, string symbol)
    {
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal)
                || trimmed.StartsWith("///", StringComparison.Ordinal)
                || trimmed.StartsWith("*", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.Contains(symbol, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Tnzi.NET.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
