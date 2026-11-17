namespace Tnzi.AI.Cli.Metadata;

/// <summary>
/// 内置 provider 描述表。
/// </summary>
/// <remarks>
/// <para>
/// 表里只放有适配器实现的 provider；一个协议没有实现时（<see cref="CliAgentProtocol.VendorAppServer"/>），
/// 配置里指向它的 provider 选中时得到一个明确的 501，而不是一次令人困惑的启动失败。
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
        // --effort 由框架拥有（这一轮选的强度，没选时是绑定上的 ThinkingLevel）：用户在 custom args 里再写一个，
        // CLI 会收到两个互相冲突的值，而哪个生效取决于 CLI 内部的解析顺序。
        ["--effort"] = BlockedArgMode.WithValue,
        ["--resume"] = BlockedArgMode.WithValue,
        // 截断续接由框架按线程里的消息决定（编辑、重新生成），用户参数改写它会让会话与线程对不上
        ["--resume-session-at"] = BlockedArgMode.WithValue,
        ["--resume-drops-turn"] = BlockedArgMode.WithValue,
        ["--fork-session"] = BlockedArgMode.Standalone,
        ["--model"] = BlockedArgMode.WithValue,
        // 谁回答权限提示由框架按这一轮有没有人值守决定（HostPromptArgs）：用户参数改写它，要么让请求没人回答，要么让 agent 在无人值守时拿到提问工具
        ["--permission-prompt-tool"] = BlockedArgMode.WithValue,
        ["--permission-prompts"] = BlockedArgMode.WithValue
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
                Attachments = CliAttachmentKinds.Images | CliAttachmentKinds.Documents,
                SupportsResumeAtEntry = true,
                // 实测（2.1.316）：带上它 can_use_tool 才会发出，AskUserQuestion / ExitPlanMode 才在工具列表里（见 HostPromptArgs 的说明）。
                // 真机冒烟测试守着（ClaudeStreamJsonSmokeTests 的提问、计划、先问我三条）
                HostPromptArgs = ["--permission-prompt-tool", "stdio"],
                // 实测（2.1.317）：--tools 收窄内置工具时这三个跟着消失，有人值守时执行器把它们补回去（见 InteractionTools 的说明）
                InteractionTools = ["AskUserQuestion", "EnterPlanMode", "ExitPlanMode"],
                // 实测（2.1.317）：shouldQuery:false 的用户帧只进会话、不开一轮，CLI 回一条空 result；斜杠命令前的上下文靠它（见 AcceptsContextFrames）
                AcceptsContextFrames = true,
                // 实测（2.1.322）：一轮进行中写进 stdin、带 uuid 与 priority "next" 的用户帧在下一个工具结果之后并进这一轮
                // （command_lifecycle queued → started），result 的 user_message_uuids 列出它；赶上最后一段回答的另开一轮
                TakesMessagesMidTurn = true,
                // 实测（2.1.308）：apply_flag_settings {effortLevel} 把以 --effort high 起的会话改成别的档（get_settings 的 applied.effort 随之变），
                // 不写设置文件；认不出的值也回 success 但不生效，effortLevel null 回到模型默认档（不是 --effort 那个值）
                ChangesEffortInSession = true
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
                // 新版 Kimi Code（npm 包 @moonshot-ai/kimi-code，2.4 读包内代码核对）：可执行文件仍叫 kimi，`kimi acp` 起 ACP 服务。
                // 握手报 promptCapabilities.image = true；推理强度是 category 为 thought_level 的会话选项（id "thinking"，取值 off / on 或各档）；
                // 项目里读 AGENTS.md 与 .agents/skills；配置目录随 KIMI_CODE_HOME（缺省 ~/.kimi-code）。
                // prompt 的应答只有 stopReason，不报 token 数：用量由使用方在模型网关那一侧计量。
                DefaultExecutable = "kimi",
                LaunchArgs = ["acp"],
                BriefFileName = "AGENTS.md",
                SkillsRelativePath = ".agents/skills",
                LaunchHeader = "kimi acp",
                ConfigDirectoryEnvironmentVariable = "KIMI_CODE_HOME",
                Attachments = CliAttachmentKinds.Images,
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
                Protocol = CliAgentProtocol.Acp,
                // 经 ACP 适配器 codex-acp 接入（npm 包 @agentclientprotocol/codex-acp，原 zed-industries 版已归档；2.5 读包内代码核对）。
                // 它自己起 codex app-server（随包带一份 @openai/codex，CODEX_PATH 可换）；握手报 promptCapabilities.image = true、
                // 支持 session/resume；模型与推理强度是 category 为 model / thought_level 的会话选项（id "model" / "reasoning_effort"）。
                // 登录：已登录的 CODEX_HOME，或 DEFAULT_AUTH_REQUEST={"methodId":"api-key"} 加 CODEX_API_KEY / OPENAI_API_KEY，由使用方经环境给出。
                // 同名的 MCP server 以用户自己的 Codex 配置为准（codex-acp 跳过客户端给的那条），平台的 server 要用不会撞名的名字。
                DefaultExecutable = "codex-acp",
                BriefFileName = "AGENTS.md",
                SkillsRelativePath = ".agents/skills",
                LaunchHeader = "codex-acp",
                ConfigDirectoryEnvironmentVariable = "CODEX_HOME",
                Attachments = CliAttachmentKinds.Images,
                BlockedArgs = AcpBlockedArgs
            }
        }.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);
}
