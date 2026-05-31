namespace Tnzi.AI.Channels.Abstractions;

/// <summary>
/// 入站 Webhook 适配器 — 由可通过 HTTP 回调接收消息的平台适配器实现
/// （Slack / Discord / DingTalk / Feishu）。
/// 由 <c>DefaultChannelWebhookController</c> 调用：先验签 + 处理握手，再分发到适配器。
/// 验签逻辑与密钥都封装在适配器内部（密钥归属适配器，不应泄漏到控制器）。
/// </summary>
public interface IInboundWebhookAdapter
{
    /// <summary>平台标识（用于路由匹配，如 "slack" / "discord" / "dingtalk" / "feishu"）。</summary>
    string Platform { get; }

    /// <summary>
    /// 处理一次 Webhook 回调：按平台规范验签 / 处理握手挑战 / 分发事件。
    /// 控制器据返回结果决定 HTTP 响应（200+challenge / 200 accepted / 401/403 rejected）。
    /// </summary>
    /// <param name="rawBody">请求 body 原文（用于 HMAC/签名计算，控制器需启用缓冲读取原始字节）。</param>
    /// <param name="headers">HTTP 请求头（含签名/时间戳等）。</param>
    /// <param name="ct">取消令牌。</param>
    Task<WebhookProcessResult> ProcessWebhookAsync(
        string rawBody,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken ct = default);
}

/// <summary>Webhook 处理结果类型。</summary>
public enum WebhookOutcome
{
    /// <summary>URL 验证 / 握手挑战 — 控制器应原样回显挑战内容。</summary>
    Challenge,

    /// <summary>已验签并接受（已分发到消息总线）。</summary>
    Accepted,

    /// <summary>验签失败 / 缺少签名（已配置密钥时）— 控制器应返回 401/403，不分发。</summary>
    Rejected
}

/// <summary>
/// Webhook 处理结果。<see cref="ChallengeResponse"/> 仅在 <see cref="Outcome"/> 为
/// <see cref="WebhookOutcome.Challenge"/> 时有意义。
/// </summary>
public sealed class WebhookProcessResult
{
    /// <summary>结果类型。</summary>
    public WebhookOutcome Outcome { get; init; }

    /// <summary>挑战响应内容（原样回显，可能是纯文本或 JSON）。</summary>
    public string? ChallengeResponse { get; init; }

    /// <summary>挑战响应的 content-type（默认 application/json）。</summary>
    public string ChallengeContentType { get; init; } = "application/json";

    /// <summary>拒绝原因（用于日志/响应消息，不含敏感信息）。</summary>
    public string? RejectReason { get; init; }

    public static WebhookProcessResult Challenge(string response, string contentType = "application/json")
        => new() { Outcome = WebhookOutcome.Challenge, ChallengeResponse = response, ChallengeContentType = contentType };

    public static WebhookProcessResult Accepted()
        => new() { Outcome = WebhookOutcome.Accepted };

    public static WebhookProcessResult Rejected(string reason)
        => new() { Outcome = WebhookOutcome.Rejected, RejectReason = reason };
}
