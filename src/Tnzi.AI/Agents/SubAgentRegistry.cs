namespace Tnzi.AI.Agents;

/// <summary>
/// 子 Agent 类型注册表实现 — 内置 3 种标准类型 + 运行时扩展
/// </summary>
/// <remarks>
/// 内置类型：
/// - general-purpose: 通用子 Agent，完整工具集（排除 task/clarification/present_files），50 轮次
/// - bash: 沙箱专用子 Agent，仅 sandbox 工具，30 轮次
/// - researcher: 研究子 Agent，web-search + file 工具，30 轮次
/// </remarks>
public class SubAgentRegistry : ISubAgentRegistry
{
    private readonly ConcurrentDictionary<string, SubAgentTypeDefinition> _types = new(StringComparer.OrdinalIgnoreCase);

    public SubAgentRegistry()
    {
        RegisterBuiltInTypes();
    }

    /// <inheritdoc />
    public IReadOnlyList<SubAgentTypeDefinition> GetAll()
        => _types.Values.ToList().AsReadOnly();

    /// <inheritdoc />
    public SubAgentTypeDefinition? Get(string name)
    {
        Check.NotNullOrWhiteSpace(name);
        return _types.GetValueOrDefault(name);
    }

    /// <inheritdoc />
    public void Register(SubAgentTypeDefinition definition)
    {
        Check.NotNull(definition);
        Check.NotNullOrWhiteSpace(definition.Name);
        _types[definition.Name] = definition;
    }

    /// <inheritdoc />
    public bool Unregister(string name)
    {
        Check.NotNullOrWhiteSpace(name);
        return _types.TryRemove(name, out _);
    }

    private void RegisterBuiltInTypes()
    {
        Register(new SubAgentTypeDefinition(
            Name: "general-purpose",
            Description: "General-purpose sub-agent with full toolset minus orchestration tools",
            ToolGroups: ["default", "file", "code", "web-search", "sandbox"],
            ExcludedToolGroups: ["task", "clarification", "present-files"],
            MaxTurns: 50,
            Instructions: "You are a general-purpose assistant. Complete the delegated task thoroughly.",
            DefaultApprovalMode: ToolApprovalMode.Specific,
            CapabilityTags: ["general", "analysis", "implementation"]));

        Register(new SubAgentTypeDefinition(
            Name: "bash",
            Description: "Sandbox-only sub-agent for command execution and file operations",
            ToolGroups: ["sandbox"],
            ExcludedToolGroups: [],
            MaxTurns: 30,
            Instructions: "You are a command-line assistant. Execute commands in the sandbox to complete the task.",
            DefaultApprovalMode: ToolApprovalMode.Specific,
            CapabilityTags: ["shell", "filesystem", "sandbox"]));

        Register(new SubAgentTypeDefinition(
            Name: "researcher",
            Description: "Research sub-agent with web search and file access",
            ToolGroups: ["web-search", "file"],
            ExcludedToolGroups: [],
            MaxTurns: 30,
            Instructions: "You are a research assistant. Search the web and read files to gather information for the task.",
            DefaultApprovalMode: ToolApprovalMode.NeverRequire,
            CapabilityTags: ["research", "web", "files"]));
    }
}
