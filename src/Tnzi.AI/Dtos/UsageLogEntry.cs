namespace Tnzi.AI.Dtos;

/// <summary>
/// 一行用量日志：<see cref="Services.IUsageLogService.LogUsageAsync(UsageLogEntry, CancellationToken)"/> 的入参。
/// </summary>
/// <remarks>
/// <para><see cref="UserId"/> 是这一行计在谁名下。为空时服务按顺序自己找：当前正在执行的运行请求的用户、
/// 当前登录用户、<see cref="ThreadId"/> 所指线程的创建人；都没有就留空。
/// 调用方知道用户却不在该用户的登录上下文里时（后台任务、按运行令牌认证的回调、模型网关）应当显式给出。</para>
/// <para><see cref="EstimatedCostUsd"/> 为空时按配置的单价计算。</para>
/// </remarks>
public sealed record UsageLogEntry
{
    /// <summary>操作类型（见 <c>AIOperationType</c>）</summary>
    public required string OperationType { get; init; }

    /// <summary>提供商名称</summary>
    public required string Provider { get; init; }

    /// <summary>模型名称</summary>
    public required string Model { get; init; }

    /// <summary>输入 Token 数</summary>
    public int InputTokens { get; init; }

    /// <summary>输出 Token 数</summary>
    public int OutputTokens { get; init; }

    /// <summary>缓存命中的输入 Token 数</summary>
    public int CachedInputTokens { get; init; }

    /// <summary>缓存写入的输入 Token 数</summary>
    public int CacheCreationTokens { get; init; }

    /// <summary>耗时（毫秒）</summary>
    public long DurationMs { get; init; }

    /// <summary>是否成功</summary>
    public bool IsSuccess { get; init; } = true;

    /// <summary>错误信息</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>这一行计在谁名下；为空时由服务推断（见类型说明）</summary>
    public Guid? UserId { get; init; }

    /// <summary>关联的 Agent</summary>
    public Guid? AgentId { get; init; }

    /// <summary>关联的线程</summary>
    public Guid? ThreadId { get; init; }

    /// <summary>调用方给出的成本（美元）；为空按配置的单价计算</summary>
    public decimal? EstimatedCostUsd { get; init; }
}
