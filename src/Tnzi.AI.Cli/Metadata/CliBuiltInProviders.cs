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
        // 截断续接由框架按线程里的消息决定（编辑、重新生成），用户参数改写它会让会话与线程对不上
        ["--resume-session-at"] = BlockedArgMode.WithValue,
        ["--resume-drops-turn"] = BlockedArgMode.WithValue,
        ["--fork-session"] = BlockedArgMode.Standalone,
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
                BlockedArgs = StreamJsonBlockedArgs,
                // 以下四项均实测（2.1）：auth status 看得见本进程环境里的 API key 与 OAuth 令牌，
                // 也跟随 CLAUDE_CONFIG_DIR；--setting-sources 去掉 user 后个人 hooks / 插件 / 技能不再加载，
                // 但个人全局 CLAUDE.md 仍会加载（它不算设置来源）。
                AuthStatusArgs = ["auth", "status", "--json"],
                AuthTokenEnvironmentVariable = "CLAUDE_CODE_OAUTH_TOKEN",
                ExcludeUserSettingsArgs = ["--setting-sources", "project,local"],
                ConfigDirectoryEnvironmentVariable = "CLAUDE_CONFIG_DIR",
                // 以下按 2.1.308 实测：项目目录名固定后，记录在 <配置目录>/projects/<名字>/<会话 id>.jsonl，
                // 两个 worktree 共用一份、--resume 能续上；--append-system-prompt-file 在 stream-json 输入下生效；
                // --add-dir 目录里的技能照常加载；--setting-sources user 加 --settings 交回去的仓库设置，权限规则照样生效。
                // 与 .jsonl 同名的目录是 CLI 为单个会话另存内容（子 agent 的记录等）的位置，实测的简单轮次里没出现，不存在就跳过。
                ProjectDirectoryNameEnvironmentVariable = "CLAUDE_CODE_PROJECT_DIR_NAME",
                SessionStorePaths = ["projects/{project}/{session}.jsonl", "projects/{project}/{session}"],
                AppendSystemPromptFileArg = "--append-system-prompt-file",
                AddDirectoryArg = "--add-dir",
                BuiltInToolsArg = "--tools",
                SettingSourcesArg = "--setting-sources",
                SettingsArg = "--settings",
                SessionRetentionSettingName = "cleanupPeriodDays",
                // 实测（2.1.303）：stream-json 输入的 user 消息接受 image（base64 source）与 document（base64 PDF）内容块，
                // 模型读得到；--resume <会话> --resume-session-at <条目 uuid> --fork-session 分叉出只含该条目之前对话的新会话，
                // 原会话不变。两者都有真机冒烟测试守着（ClaudeStreamJsonSmokeTests）。
                AcceptsAttachments = true,
                SupportsResumeAtEntry = true
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
                // 权限档位不是全权时不带 --yolo：带着它 CLI 从不请求权限，档位落不到应答上
                LaunchArgsWithoutAutoApproval = ["--acp"],
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
                LaunchArgsWithoutAutoApproval = ["agent", "stdio"],
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
