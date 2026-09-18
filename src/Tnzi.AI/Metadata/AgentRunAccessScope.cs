namespace Tnzi.AI.Metadata;

/// <summary>
/// <c>IAgentRuntimeControlService</c> 一次操作的可见范围。
/// </summary>
/// <remarks>
/// 默认 <see cref="Caller"/>：只看得见、只动得了调用方自己起的运行（归属键 <c>AgentRun.CreatorId</c>；
/// 调用方身份取当前运行请求的 <c>UserId</c>，无则环境用户；都没有 = 只看得见同样无主的运行）。
/// 别人的运行一律 404，不泄露存在性。<c>task</c> 工具组走这一档。
/// <see cref="Tenant"/> 是管理端的整租户范围，由管理控制器显式传入（那一面已经由 <c>ai.agentRun.*</c> 门住）。
/// </remarks>
public enum AgentRunAccessScope
{
    /// <summary>调用方自己的运行（默认，失败关闭）。</summary>
    Caller = 0,

    /// <summary>整租户范围（管理端）。</summary>
    Tenant = 1
}
