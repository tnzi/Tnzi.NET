namespace Tnzi.AI.Permissions;

/// <summary>
/// 框架内建敏感工具组的权限码。工具组经 <c>[AIToolGroup(RequiredPermissions = ...)]</c> 引用，
/// <c>AgentResolver</c> 按请求身份检查；持有码的用户才拿得到该组工具。
/// </summary>
/// <remarks>
/// 码的声明随模块走：<see cref="Task"/> 与 <see cref="A2A"/> 由 <c>AIPermissions</c> 声明，
/// <see cref="Sandbox"/> 由 <c>Tnzi.AI.Sandbox</c> 的 provider 声明 —— 常量放在核心是因为
/// 子模块的工具类要引用它，而没加载子模块的宿主不该被种下一个没有工具对应的码。
/// </remarks>
public static class AIToolPermissions
{
    /// <summary>子 Agent 生命周期工具组 <c>task</c>（spawn / list / get / wait / send_input / kill）。</summary>
    public const string Task = "ai.tools.task";

    /// <summary>远端 agent 调用工具组 <c>a2a</c>。</summary>
    public const string A2A = "ai.tools.a2a";

    /// <summary>沙箱工具组 <c>sandbox</c>（bash / 文件读写），由 <c>Tnzi.AI.Sandbox</c> 提供。</summary>
    public const string Sandbox = "ai.tools.sandbox";
}
