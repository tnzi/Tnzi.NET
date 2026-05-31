using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tnzi.AspNetCore.Mvc;

namespace Tnzi.AI.Channels.Controllers;

/// <summary>
/// 入站 Webhook 控制器 — 接收 Slack / Discord / DingTalk / Feishu 的外部回调，
/// 在分发到对应适配器之前强制按平台规范验签（防伪造/重放），并处理各平台的握手挑战。
/// </summary>
/// <remarks>
/// 标记 <see cref="AllowAnonymousAttribute"/>：这些是外部平台的服务器到服务器回调，
/// 认证手段是请求签名（HMAC / Ed25519）而非用户登录态。每个平台的密钥由其适配器持有，
/// 验签逻辑封装在适配器的 <see cref="IInboundWebhookAdapter.ProcessWebhookAsync"/> 中。
/// </remarks>
[DefaultController]
[AllowAnonymous]
[Route("channels/webhook")]
public class DefaultChannelWebhookController : ApiControllerBase
{
    private readonly IEnumerable<IInboundWebhookAdapter>? _webhookAdapters;

    /// <summary>
    /// 初始化 Webhook 控制器（适配器可选，无任何 Webhook 适配器加载时所有平台返回 404）。
    /// </summary>
    public DefaultChannelWebhookController(IEnumerable<IInboundWebhookAdapter>? webhookAdapters = null)
    {
        _webhookAdapters = webhookAdapters;
    }

    /// <summary>
    /// 接收指定平台的 Webhook 回调。
    /// 验签通过且为握手挑战 → 回显挑战；验签通过且为事件 → 分发并返回 200；
    /// 验签失败 / 缺少签名（已配置密钥时）→ 401，不分发。
    /// </summary>
    /// <param name="platform">平台标识（slack / discord / dingtalk / feishu）。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpPost("{platform}")]
    public virtual async Task<IActionResult> Receive(string platform, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            return new NotFoundResult();
        }

        var adapter = (_webhookAdapters ?? [])
            .FirstOrDefault(a => string.Equals(a.Platform, platform, StringComparison.OrdinalIgnoreCase));

        if (adapter == null)
        {
            // 平台未启用 / 未加载对应适配器。
            return new NotFoundResult();
        }

        var rawBody = await ReadRawBodyAsync(ct);
        var headers = CollectHeaders();

        var result = await adapter.ProcessWebhookAsync(rawBody, headers, ct);

        return result.Outcome switch
        {
            WebhookOutcome.Challenge => Content(result.ChallengeResponse ?? string.Empty, result.ChallengeContentType),
            WebhookOutcome.Accepted => Content(string.Empty, "text/plain"),
            WebhookOutcome.Rejected => StatusCode(StatusCodes.Status401Unauthorized, result.RejectReason ?? "Signature verification failed"),
            _ => StatusCode(StatusCodes.Status400BadRequest)
        };
    }

    /// <summary>读取请求 body 原文（用于 HMAC / 签名计算，不可经反序列化丢失原始字节）。</summary>
    private async Task<string> ReadRawBodyAsync(CancellationToken ct)
    {
        Request.EnableBuffering();
        Request.Body.Position = 0;
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var body = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;
        return body;
    }

    /// <summary>收集请求头为大小写不敏感字典（适配器据此取签名/时间戳头）。</summary>
    private Dictionary<string, string> CollectHeaders()
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in Request.Headers)
        {
            headers[header.Key] = header.Value.ToString();
        }
        return headers;
    }
}
