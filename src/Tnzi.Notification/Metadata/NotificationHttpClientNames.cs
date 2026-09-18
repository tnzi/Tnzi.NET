namespace Tnzi.Notification.Metadata;

/// <summary>
/// 本模块向 <see cref="IHttpClientFactory"/> 登记的具名 HttpClient。
/// </summary>
/// <remarks>
/// ★ <b>取远程附件的客户端不跟随重定向。</b><c>EgressGuard</c> 只看得见原始 URL：一个公网地址回
/// <c>302 Location: http://169.254.169.254/…</c>，自动跟随的客户端会把第二跳直接发出去，云元数据 /
/// 内网端点的回应装进信里寄给收件人，而检查全过。所以附件下载走一个 <c>AllowAutoRedirect = false</c>
/// 的具名客户端，发送器把任何 3xx 当作取件失败（与 <c>Tnzi.AI</c> 的 A2A / MCP 客户端同一口径）。
/// 消费方可以按这个名字重配超时、代理、弹性策略，但不要把重定向开回来。
/// </remarks>
public static class NotificationHttpClientNames
{
    /// <summary>按 URL 取远程附件用的客户端：禁自动重定向。</summary>
    public const string Attachments = "Tnzi.Notification.Attachments";
}
