// R3：二级目录（Adapters/Acp/）只是开发期分类，不产生子命名空间。
namespace Tnzi.AI.Cli.Adapters;

/// <summary>
/// 从 <c>session/request_permission</c> 请求里挑一个可安全选择的选项。
/// </summary>
/// <remarks>
/// <para>
/// ACP 的权限契约是「从我给的这些选项里选一个」——回一个 agent 从未提供过的 id 等同于拒绝，
/// 而某些实现会因此静默阻止每一次文件写入。所以<b>必须</b>从 <c>options</c> 数组里挑。
/// </para>
/// <para>
/// 只有 <c>allow_once</c> / <c>allow_always</c> 两种 kind 算放行；任何未知或新增的 kind
/// 一律视为不放行 —— 让一个未来才出现的 kind 被自动批准，是最不该发生的失败方向。
/// 没有任何放行项时退而求其次选一个 <c>reject_once</c>（只拒这一个动作），
/// 而不是回 <c>cancelled</c>（那会中止整个 turn）。
/// </para>
/// </remarks>
internal static class AcpPermissionSelector
{
    private const string AllowOnce = "allow_once";
    private const string AllowAlways = "allow_always";
    private const string RejectOnce = "reject_once";

    /// <summary>
    /// 已知「只在本会话内放行、不持久化决定」的选项 id。
    /// </summary>
    /// <remarks>
    /// ACP 没有会话级的 kind，于是「本会话放行」和「永久放行」都报成 <c>allow_always</c>；
    /// 单看 kind 分不出来，只能按 id 认。优先选会话级的，避免在用户的机器上留下永久授权。
    /// </remarks>
    private static readonly string[] SessionScopedOptionIds = ["allow_session", "approve_for_session"];

    /// <summary>挑一个 optionId；没有可安全选择的返回 null。</summary>
    public static string? Select(JsonElement parameters)
    {
        var options = EnumerateOptions(parameters).ToList();
        if (options.Count == 0)
        {
            return null;
        }

        // 1) 会话级放行（不留永久授权）
        var sessionScoped = options.FirstOrDefault(o =>
            IsAllowKind(o.Kind) && SessionScopedOptionIds.Contains(o.OptionId, StringComparer.OrdinalIgnoreCase));
        if (sessionScoped.OptionId is not null)
        {
            return sessionScoped.OptionId;
        }

        // 2) 单次放行
        var allowOnce = options.FirstOrDefault(o => string.Equals(o.Kind, AllowOnce, StringComparison.OrdinalIgnoreCase));
        if (allowOnce.OptionId is not null)
        {
            return allowOnce.OptionId;
        }

        // 3) 永久放行（只有当 agent 没提供更窄的选项时才用）
        var allowAlways = options.FirstOrDefault(o => string.Equals(o.Kind, AllowAlways, StringComparison.OrdinalIgnoreCase));
        if (allowAlways.OptionId is not null)
        {
            return allowAlways.OptionId;
        }

        // 4) 单次拒绝
        var rejectOnce = options.FirstOrDefault(o => string.Equals(o.Kind, RejectOnce, StringComparison.OrdinalIgnoreCase));
        return rejectOnce.OptionId;
    }

    /// <summary>判断选中的 optionId 是不是放行项（用于日志措辞）。</summary>
    public static bool IsGrant(JsonElement parameters, string optionId)
        => EnumerateOptions(parameters).Any(o =>
            string.Equals(o.OptionId, optionId, StringComparison.Ordinal) && IsAllowKind(o.Kind));

    private static bool IsAllowKind(string? kind)
        => string.Equals(kind, AllowOnce, StringComparison.OrdinalIgnoreCase)
           || string.Equals(kind, AllowAlways, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<(string? OptionId, string? Kind)> EnumerateOptions(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("options", out var options)
            || options.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var option in options.EnumerateArray())
        {
            if (option.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var optionId = option.TryGetProperty("optionId", out var id) ? id.GetString() : null;
            var kind = option.TryGetProperty("kind", out var k) ? k.GetString() : null;
            if (!string.IsNullOrWhiteSpace(optionId))
            {
                yield return (optionId, kind);
            }
        }
    }
}

/// <summary>
/// 把 ACP 的一次 turn 拆成「最终交付物」与「完整文本流」。
/// </summary>
/// <remarks>
/// <para>
/// ACP 运行时把过程叙述和最终答案发成<b>同一种</b> chunk，唯一可用的边界是工具调用。
/// 于是交付物 = 最后一次工具调用之后的文本；若为空，回落到上一个非空文本块
/// （一个以工具调用收尾的 turn 不该给出空回复）。这是启发式，直到运行时肯显式标注最终答案为止。
/// </para>
/// <para>
/// 完整文本流必须同时保留：错误嗅探要读每一个 chunk —— 适配器会把
/// 「重试 N 次后放弃」当成普通 agent 消息发出，而它可能落在最后一次工具调用<b>之前</b>，
/// 只看交付物会整段漏掉。
/// </para>
/// </remarks>
internal sealed class AcpDeliverableTracker
{
    private readonly StringBuilder _full = new();
    private readonly StringBuilder _deliverable = new();
    private string _lastTextBlock = string.Empty;
    private readonly Lock _gate = new();

    /// <summary>记录一条已接受的事件。</summary>
    public void Observe(CliAgentEvent evt)
    {
        lock (_gate)
        {
            switch (evt.Type)
            {
                case CliAgentEventType.Text when !string.IsNullOrEmpty(evt.Content):
                    _full.Append(evt.Content);
                    _deliverable.Append(evt.Content);
                    break;

                case CliAgentEventType.ToolUse:
                    var block = _deliverable.ToString();
                    if (!string.IsNullOrWhiteSpace(block))
                    {
                        _lastTextBlock = block;
                    }

                    _deliverable.Clear();
                    break;
            }
        }
    }

    /// <summary>取交付物与完整文本流。</summary>
    public (string Deliverable, string Full) Result()
    {
        lock (_gate)
        {
            var deliverable = _deliverable.ToString();
            if (string.IsNullOrWhiteSpace(deliverable))
            {
                deliverable = _lastTextBlock;
            }

            return (deliverable, _full.ToString());
        }
    }
}

/// <summary>
/// 从 stderr 与文本流里嗅探 provider 级的终态失败。
/// </summary>
/// <remarks>
/// 多个 ACP 运行时在上游 HTTP 调用失败时<b>仍然</b>返回 <c>stopReason = end_turn</c>。
/// 不嗅探的话，用户看到的是「空输出」而不是「token 过期 / 被限流 / 上游 5xx」——
/// 前者无从下手，后者一看就知道该做什么。
/// <para>
/// 只认<b>终态</b>措辞（"after N retries" / "giving up"）与明确的鉴权失败，
/// 不认单次重试警告 —— 重试成功的运行必须保持成功。
/// </para>
/// </remarks>
internal sealed class AcpProviderErrorSniffer
{
    private static readonly (string Phrase, CliRunFailureReason Reason)[] Signals =
    [
        ("401 unauthorized", CliRunFailureReason.AuthenticationFailed),
        ("invalid api key", CliRunFailureReason.AuthenticationFailed),
        ("authentication failed", CliRunFailureReason.AuthenticationFailed),
        ("token expired", CliRunFailureReason.AuthenticationFailed),
        ("429 too many requests", CliRunFailureReason.RateLimited),
        ("rate limit exceeded", CliRunFailureReason.RateLimited),
        ("quota exceeded", CliRunFailureReason.QuotaExceeded),
        ("insufficient credit", CliRunFailureReason.QuotaExceeded),
        ("api call failed after", CliRunFailureReason.ProviderError),
        ("giving up after", CliRunFailureReason.ProviderError),
        ("503 service unavailable", CliRunFailureReason.ProviderError),
        ("internal server error", CliRunFailureReason.ProviderError)
    ];

    /// <summary>
    /// 在文本里找终态失败信号；没找到返回 null。
    /// </summary>
    public static (CliRunFailureReason Reason, string Phrase)? Sniff(params string?[] texts)
    {
        foreach (var text in texts)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            foreach (var (phrase, reason) in Signals)
            {
                if (text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                {
                    return (reason, phrase);
                }
            }
        }

        return null;
    }
}

/// <summary>
/// 把 Claude 风格的 <c>mcpServers</c> 对象翻译成 ACP <c>session/new</c> 期望的数组形态，
/// 并按 agent 声明的能力过滤。
/// </summary>
/// <remarks>
/// 能力过滤不是锦上添花：给一个只支持 stdio 的运行时递一条 http/sse 条目，
/// 会让整个 <c>session/new</c> 失败 —— 于是一个本可以少几个工具照常跑完的任务直接起不来。
/// </remarks>
internal static class AcpMcpServerTranslator
{
    /// <summary>把 <c>{"mcpServers": {...}}</c> 翻成 ACP 数组。</summary>
    public static List<Dictionary<string, object?>> Translate(string? mcpConfigJson, ILogger logger)
    {
        var servers = new List<Dictionary<string, object?>>();
        if (string.IsNullOrWhiteSpace(mcpConfigJson))
        {
            return servers;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(mcpConfigJson);
        }
        catch (JsonException ex)
        {
            // fail-closed：配置写错时不要静默丢掉全部 MCP server，让启动带着真实原因失败。
            throw new InvalidOperationException("Managed MCP config is not valid JSON.", ex);
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("mcpServers", out var mcpServers)
                || mcpServers.ValueKind != JsonValueKind.Object)
            {
                return servers;
            }

            foreach (var entry in mcpServers.EnumerateObject())
            {
                var server = TranslateOne(entry.Name, entry.Value, logger);
                if (server is not null)
                {
                    servers.Add(server);
                }
            }
        }

        return servers;
    }

    /// <summary>按 <c>initialize</c> 响应里声明的 MCP 能力过滤条目。</summary>
    public static List<Dictionary<string, object?>> FilterByCapabilities(
        List<Dictionary<string, object?>> servers, JsonElement initializeResult, string providerKey, ILogger logger)
    {
        var supportsHttp = false;
        var supportsSse = false;

        if (initializeResult.ValueKind == JsonValueKind.Object
            && initializeResult.TryGetProperty("agentCapabilities", out var capabilities)
            && capabilities.ValueKind == JsonValueKind.Object
            && capabilities.TryGetProperty("mcpCapabilities", out var mcp)
            && mcp.ValueKind == JsonValueKind.Object)
        {
            supportsHttp = mcp.TryGetProperty("http", out var http) && http.ValueKind == JsonValueKind.True;
            supportsSse = mcp.TryGetProperty("sse", out var sse) && sse.ValueKind == JsonValueKind.True;
        }

        var filtered = new List<Dictionary<string, object?>>(servers.Count);
        foreach (var server in servers)
        {
            var type = server.GetValueOrDefault("type") as string;
            var allowed = type switch
            {
                "http" => supportsHttp,
                "sse" => supportsSse,
                _ => true // stdio 是 ACP 的基础能力，无须声明。
            };

            if (allowed)
            {
                filtered.Add(server);
            }
            else
            {
                logger.LogWarning(
                    "[{Provider}] Dropping MCP server '{Name}': the runtime did not advertise '{Transport}' support",
                    providerKey, server.GetValueOrDefault("name"), type);
            }
        }

        return filtered;
    }

    private static Dictionary<string, object?>? TranslateOne(string name, JsonElement value, ILogger logger)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var type = value.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
        var url = value.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null;

        if (!string.IsNullOrWhiteSpace(url))
        {
            var server = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = name,
                ["type"] = string.IsNullOrWhiteSpace(type) ? "http" : type,
                ["url"] = url
            };

            if (value.TryGetProperty("headers", out var headers) && headers.ValueKind == JsonValueKind.Object)
            {
                server["headers"] = headers.EnumerateObject()
                    .ToDictionary(p => p.Name, p => (object?)p.Value.GetString(), StringComparer.Ordinal);
            }

            return server;
        }

        var command = value.TryGetProperty("command", out var commandElement) ? commandElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(command))
        {
            logger.LogWarning("Skipping MCP server '{Name}': neither url nor command is set", name);
            return null;
        }

        var stdio = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = name,
            ["command"] = command
        };

        if (value.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Array)
        {
            stdio["args"] = args.EnumerateArray().Select(a => a.GetString()).Where(a => a is not null).ToList();
        }

        if (value.TryGetProperty("env", out var env) && env.ValueKind == JsonValueKind.Object)
        {
            // ACP 的 env 是 {name, value} 数组，不是对象。
            stdio["env"] = env.EnumerateObject()
                .Select(p => new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["name"] = p.Name,
                    ["value"] = p.Value.GetString()
                })
                .ToList();
        }

        return stdio;
    }
}
