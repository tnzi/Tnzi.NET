namespace Tnzi.AI.Channels.Adapters;

/// <summary>
/// Channel adapter 共享发送逻辑 - 分块 + 指数退避重试。
/// 避免各 adapter 重复实现相同的 chunking + retry 模式。
/// </summary>
internal static class ChannelSendHelper
{
    /// <summary>
    /// 将文本按 maxLength 分块，每块通过 sendChunk 发送，失败时指数退避重试。
    /// </summary>
    /// <param name="text">待发送文本</param>
    /// <param name="maxLength">单块最大长度</param>
    /// <param name="maxRetries">最大重试次数</param>
    /// <param name="sendChunk">发送单块的委托</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="adapterName">适配器名称（用于日志）</param>
    /// <param name="ct">取消令牌</param>
    internal static async Task SendChunkedWithRetryAsync(
        string text,
        int maxLength,
        int maxRetries,
        Func<string, CancellationToken, Task> sendChunk,
        ILogger logger,
        string adapterName,
        CancellationToken ct = default)
    {
        while (text.Length > 0)
        {
            var chunk = text.Length > maxLength ? text[..maxLength] : text;
            text = text.Length > maxLength ? text[maxLength..] : string.Empty;

            var retryCount = 0;
            while (true)
            {
                try
                {
                    await sendChunk(chunk, ct).ConfigureAwait(false);
                    break;
                }
                // 取消不是"发送失败"：不重试、直接冒泡。此前 catch(Exception) 会把
                // OperationCanceledException 也当成可重试失败，于是关停适配器时每个分块
                // 还要先睡满一轮退避（最长 30s）才退出。
                catch (Exception ex) when (ex is not OperationCanceledException && retryCount < maxRetries)
                {
                    retryCount++;
                    var delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, retryCount), 30));
                    logger.LogWarning(ex, "{Adapter} send failed (attempt {Attempt}/{Max}), retrying in {Delay}s",
                        adapterName, retryCount, maxRetries, delay.TotalSeconds);
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }
            }
        }
    }
}
