namespace Tnzi.AI.Infrastructure.Providers;

/// <summary>
/// 把 <see cref="IChatMessageProcessor"/> 接进请求链：出站消息经 <c>ProcessOutgoing</c>，
/// 非流式响应经 <c>ProcessIncoming</c>；流式响应把 <c>&lt;think&gt;…&lt;/think&gt;</c> 跨块切成
/// <see cref="TextReasoningContent"/>，正文只剩标签之外的文本。
/// </summary>
/// <remarks>
/// 2026-09-12 前五个处理器只被注册、从未被调用：Kimi / GLM / MiniMax 的推理草稿原样进入
/// <c>AgentRunResult.Response</c>、被 HistoryMiddleware 存进线程、下一轮又作为 assistant 内容回灌，
/// 用户在聊天界面直接看到 <c>&lt;think&gt;</c> 原文。由 <see cref="ChatClientFactory"/> 按
/// <see cref="ChatMessageProcessorSelector"/> 的判定包在 SDK 客户端外面。
/// </remarks>
public sealed class MessageProcessingChatClient : DelegatingChatClient
{
    private const string ThinkOpen = "<think>";
    private const string ThinkClose = "</think>";

    private readonly IChatMessageProcessor _processor;

    public MessageProcessingChatClient(IChatClient innerClient, IChatMessageProcessor processor)
        : base(Check.NotNull(innerClient))
    {
        _processor = Check.NotNull(processor);
    }

    /// <summary>接进来的处理器（诊断用）</summary>
    public IChatMessageProcessor Processor => _processor;

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var outgoing = _processor.ProcessOutgoing(messages);
        var response = await base.GetResponseAsync(outgoing, options, cancellationToken).ConfigureAwait(false);

        var incoming = _processor.ProcessIncoming(response.Messages);
        response.Messages = incoming.ToList();
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var outgoing = _processor.ProcessOutgoing(messages);
        var splitter = new ThinkTagStreamSplitter();

        await foreach (var update in base.GetStreamingResponseAsync(outgoing, options, cancellationToken).ConfigureAwait(false))
        {
            if (!ContainsPlainText(update))
            {
                yield return update;
                continue;
            }

            var rewritten = new List<AIContent>(update.Contents.Count);
            foreach (var content in update.Contents)
            {
                if (content is TextContent text && content is not TextReasoningContent)
                {
                    rewritten.AddRange(splitter.Push(text.Text));
                }
                else
                {
                    rewritten.Add(content);
                }
            }

            if (rewritten.Count > 0)
            {
                update.Contents = rewritten;
                yield return update;
            }
        }

        var tail = splitter.Flush();
        if (tail.Count > 0)
        {
            yield return new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = tail };
        }
    }

    private static bool ContainsPlainText(ChatResponseUpdate update)
        => update.Contents.Any(c => c is TextContent && c is not TextReasoningContent);

    /// <summary>
    /// 跨块的 <c>&lt;think&gt;</c> 切分器：标签可能被切在任意字符处，正文只在确定不是标签前缀时才放行；
    /// 流结束时未闭合的标签前缀按原文放出（不吞内容）。
    /// </summary>
    private sealed class ThinkTagStreamSplitter
    {
        private readonly StringBuilder _pending = new();
        private bool _inThink;

        public List<AIContent> Push(string? text)
        {
            var output = new List<AIContent>();
            if (string.IsNullOrEmpty(text))
            {
                return output;
            }

            _pending.Append(text);
            Drain(output, final: false);
            return output;
        }

        public List<AIContent> Flush()
        {
            var output = new List<AIContent>();
            Drain(output, final: true);
            return output;
        }

        private void Drain(List<AIContent> output, bool final)
        {
            while (_pending.Length > 0)
            {
                var buffer = _pending.ToString();
                var marker = _inThink ? ThinkClose : ThinkOpen;
                var index = buffer.IndexOf(marker, StringComparison.Ordinal);

                if (index >= 0)
                {
                    Emit(output, buffer[..index]);
                    _pending.Remove(0, index + marker.Length);
                    _inThink = !_inThink;
                    continue;
                }

                // 没找到完整标签：保留可能是标签前缀的尾巴，其余放行
                var keep = final ? 0 : PartialMarkerSuffixLength(buffer, marker);
                var release = buffer.Length - keep;
                if (release <= 0)
                {
                    return;
                }

                Emit(output, buffer[..release]);
                _pending.Remove(0, release);
                return;
            }
        }

        private void Emit(List<AIContent> output, string text)
        {
            if (text.Length == 0)
            {
                return;
            }

            output.Add(_inThink ? new TextReasoningContent(text) : new TextContent(text));
        }

        /// <summary>buffer 末尾与 marker 开头重叠的最长长度（"ans&lt;thi" 对 "&lt;think&gt;" 是 4）</summary>
        private static int PartialMarkerSuffixLength(string buffer, string marker)
        {
            var max = Math.Min(buffer.Length, marker.Length - 1);
            for (var len = max; len > 0; len--)
            {
                if (buffer.EndsWith(marker.AsSpan(0, len), StringComparison.Ordinal))
                {
                    return len;
                }
            }

            return 0;
        }
    }
}
