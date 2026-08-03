namespace Tnzi.AI.Cli.Metadata;

/// <summary>
/// 内置 provider 描述表。
/// </summary>
/// <remarks>
/// <para>
/// 表里存在 <b>不等于</b> 可用：<c>codex</c> 的 <see cref="CliAgentProtocol.VendorAppServer"/>
/// 尚无适配器实现，选中它会得到一个明确的 501 而不是一次令人困惑的启动失败。
/// 之所以仍然列出来，是为了让管理端能诚实地展示「这个 provider 存在，但本版本不支持」。
/// </para>
/// <para>
/// 新增一个说 ACP 的 CLI <b>不需要动这张表</b>，走 <c>AI:Cli:CustomProviders</c> 配置即可。
/// 这里只放框架愿意为其协议正确性背书的那些。
/// </para>
/// </remarks>
public static class CliBuiltInProviders
{
    /// <summary>stream-json 系共有的被禁参数：改动它们等于改掉框架与 CLI 的通信协议。</summary>
    private static readonly Dictionary<string, BlockedArgMode> StreamJsonBlockedArgs = new(StringComparer.Ordinal)
    {
        ["-p"] = BlockedArgMode.Standalone,
        ["--print"] = BlockedArgMode.Standalone,
        ["--output-format"] = BlockedArgMode.WithValue,
        ["--input-format"] = BlockedArgMode.WithValue,
        ["--permission-mode"] = BlockedArgMode.WithValue,
        ["--mcp-config"] = BlockedArgMode.WithValue,
        // --effort 由绑定上的 ThinkingLevel 拥有：用户在 custom args 里再写一个，
        // CLI 会收到两个互相冲突的值，而哪个生效取决于 CLI 内部的解析顺序。
        ["--effort"] = BlockedArgMode.WithValue,
        ["--resume"] = BlockedArgMode.WithValue,
        ["--model"] = BlockedArgMode.WithValue
    };

    private static readonly Dictionary<string, BlockedArgMode> AcpBlockedArgs = new(StringComparer.Ordinal)
    {
        ["acp"] = BlockedArgMode.Standalone,
        ["--acp"] = BlockedArgMode.Standalone,
        ["serve"] = BlockedArgMode.Standalone,
        ["stdio"] = BlockedArgMode.Standalone
    };

    /// <summary>内置描述表，按 provider 键索引（大小写不敏感）。</summary>
    public static IReadOnlyDictionary<string, CliProviderDescriptor> All { get; } =
        new[]
        {
            new CliProviderDescriptor
            {
                Key = "claude",
                DisplayName = "Claude Code",
                Protocol = CliAgentProtocol.StreamJson,
                DefaultExecutable = "claude",
                BriefFileName = "CLAUDE.md",
                SkillsRelativePath = ".claude/skills",
                LaunchHeader = "claude (stream-json)",
                // 实测可区分：resume 一个不存在的会话时 exit=1 且 stderr 打印
                // "No conversation found with session ID: <id>"。注意 result 事件的
                // subtype 是通用的 error_during_execution，**不能**用它判断。
                ResumeRejectionDetectable = true,
                BlockedArgs = StreamJsonBlockedArgs
            },
            new CliProviderDescriptor
            {
                Key = "codebuddy",
                DisplayName = "CodeBuddy",
                Protocol = CliAgentProtocol.StreamJson,
                DefaultExecutable = "codebuddy",
                BriefFileName = "CODEBUDDY.md",
                SkillsRelativePath = ".codebuddy/skills",
                LaunchHeader = "codebuddy (stream-json)",
                ResumeRejectionDetectable = true,
                BlockedArgs = StreamJsonBlockedArgs
            },
            new CliProviderDescriptor
            {
                Key = "qwen",
                DisplayName = "Qwen Code",
                Protocol = CliAgentProtocol.StreamJson,
                DefaultExecutable = "qwen",
                BriefFileName = "QWEN.md",
                SkillsRelativePath = ".qwen/skills",
                LaunchHeader = "qwen -p (stream-json)",
                ResumeRejectionDetectable = true,
                BlockedArgs = StreamJsonBlockedArgs
            },
            new CliProviderDescriptor
            {
                Key = "hermes",
                DisplayName = "Hermes",
                Protocol = CliAgentProtocol.Acp,
                DefaultExecutable = "hermes",
                LaunchArgs = ["acp"],
                BriefFileName = "AGENTS.md",
                LaunchHeader = "hermes acp",
                BlockedArgs = AcpBlockedArgs
            },
            new CliProviderDescriptor
            {
                Key = "kimi",
                DisplayName = "Kimi Code",
                Protocol = CliAgentProtocol.Acp,
                DefaultExecutable = "kimi",
                LaunchArgs = ["acp"],
                BriefFileName = "AGENTS.md",
                LaunchHeader = "kimi acp",
                BlockedArgs = AcpBlockedArgs
            },
            new CliProviderDescriptor
            {
                Key = "kiro",
                DisplayName = "Kiro",
                Protocol = CliAgentProtocol.Acp,
                DefaultExecutable = "kiro-cli",
                LaunchArgs = ["acp"],
                BriefFileName = "AGENTS.md",
                LaunchHeader = "kiro-cli acp",
                BlockedArgs = AcpBlockedArgs
            },
            new CliProviderDescriptor
            {
                Key = "qoder",
                DisplayName = "Qoder",
                Protocol = CliAgentProtocol.Acp,
                DefaultExecutable = "qodercli",
                LaunchArgs = ["--yolo", "--acp"],
                BriefFileName = "AGENTS.md",
                SkillsRelativePath = ".qoder/skills",
                LaunchHeader = "qodercli --acp",
                BlockedArgs = AcpBlockedArgs
            },
            new CliProviderDescriptor
            {
                Key = "trae",
                DisplayName = "Trae",
                Protocol = CliAgentProtocol.Acp,
                DefaultExecutable = "traecli",
                LaunchArgs = ["acp", "serve"],
                // Trae 不从工作目录读记忆文件，brief 必须内联进 prompt。
                RequiresInlineSystemPrompt = true,
                LaunchHeader = "traecli acp serve",
                BlockedArgs = AcpBlockedArgs
            },
            new CliProviderDescriptor
            {
                Key = "grok",
                DisplayName = "Grok Build",
                Protocol = CliAgentProtocol.Acp,
                DefaultExecutable = "grok",
                LaunchArgs = ["agent", "--always-approve", "stdio"],
                BriefFileName = "AGENTS.md",
                LaunchHeader = "grok agent stdio",
                BlockedArgs = AcpBlockedArgs
            },
            new CliProviderDescriptor
            {
                Key = "codex",
                DisplayName = "Codex",
                Protocol = CliAgentProtocol.VendorAppServer,
                DefaultExecutable = "codex",
                LaunchArgs = ["app-server"],
                BriefFileName = "AGENTS.md",
                LaunchHeader = "codex app-server",
                BlockedArgs = new Dictionary<string, BlockedArgMode>(StringComparer.Ordinal)
                {
                    ["app-server"] = BlockedArgMode.Standalone
                }
            }
        }.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);
}
