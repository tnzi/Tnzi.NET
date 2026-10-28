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

    /// <summary>只读（与先问我）档放行的工具类别：读、搜索、思考，不改任何东西。</summary>
    private static readonly HashSet<string> ReadOnlyKinds = new(StringComparer.OrdinalIgnoreCase) { "read", "search", "think" };

    /// <summary>改工作区档放行的工具类别：只读的那几类，加上文件的改、删、挪。执行命令、联网与其它一律拒绝。</summary>
    private static readonly HashSet<string> EditWorkspaceKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "read", "search", "think", "edit", "delete", "move"
    };

    /// <summary>
    /// 挑一个 optionId；没有可安全选择的返回 null。
    /// </summary>
    /// <remarks>
    /// 档位不放行这个工具（按 <c>toolCall.kind</c> 判：没有 kind 或认不出来的一律不放行）时只挑单次拒绝。
    /// </remarks>
    public static string? Select(JsonElement parameters, CliPermissionMode mode = CliPermissionMode.Full)
    {
        var options = EnumerateOptions(parameters).ToList();
        if (options.Count == 0)
        {
            return null;
        }

        if (!Permits(parameters, mode))
        {
            return options.FirstOrDefault(o => string.Equals(o.Kind, RejectOnce, StringComparison.OrdinalIgnoreCase)).OptionId;
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

    /// <summary>档位是否放行这次请求的工具。</summary>
    public static bool Permits(JsonElement parameters, CliPermissionMode mode)
    {
        if (mode == CliPermissionMode.Full)
        {
            return true;
        }

        var kind = parameters.ValueKind == JsonValueKind.Object
                   && parameters.TryGetProperty("toolCall", out var toolCall)
                   && toolCall.ValueKind == JsonValueKind.Object
                   && toolCall.TryGetProperty("kind", out var kindElement)
                   && kindElement.ValueKind == JsonValueKind.String
            ? kindElement.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(kind))
        {
            return false;
        }

        return mode == CliPermissionMode.EditWorkspace ? EditWorkspaceKinds.Contains(kind) : ReadOnlyKinds.Contains(kind);
    }

    /// <summary>请求里有没有可选的选项。</summary>
    public static bool HasOptions(JsonElement parameters) => EnumerateOptions(parameters).Any();

    /// <summary>请求给出的选项（交给使用方挑）。名字没给时用 id。</summary>
    public static List<CliPermissionOption> ReadOptions(JsonElement parameters)
    {
        var result = new List<CliPermissionOption>();
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("options", out var options)
            || options.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var option in options.EnumerateArray())
        {
            if (option.ValueKind != JsonValueKind.Object
                || !option.TryGetProperty("optionId", out var id) || id.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(id.GetString()))
            {
                continue;
            }

            var name = option.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            var kind = option.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() : null;
            result.Add(new CliPermissionOption(id.GetString()!, string.IsNullOrWhiteSpace(name) ? id.GetString()! : name, kind ?? string.Empty));
        }

        return result;
    }

    /// <summary>
    /// 按使用方的回答挑一个 optionId：指定了且是请求给出的就用它（种类须与允许 / 拒绝一致）；没指定时允许挑单次允许（没有则永久允许），
    /// 拒绝挑单次拒绝（没有则永久拒绝）。挑不出来返回 null。
    /// </summary>
    public static string? SelectForDecision(JsonElement parameters, bool allow, string? optionId)
    {
        var options = EnumerateOptions(parameters).ToList();
        if (optionId is not null)
        {
            var chosen = options.FirstOrDefault(o => string.Equals(o.OptionId, optionId, StringComparison.Ordinal));
            if (chosen.OptionId is not null && IsAllowKind(chosen.Kind) == allow)
            {
                return chosen.OptionId;
            }
        }

        string[] preference = allow ? [AllowOnce, AllowAlways] : [RejectOnce, "reject_always"];
        foreach (var kind in preference)
        {
            var match = options.FirstOrDefault(o => string.Equals(o.Kind, kind, StringComparison.OrdinalIgnoreCase));
            if (match.OptionId is not null)
            {
                return match.OptionId;
            }
        }

        return null;
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
/// <para>
/// 形状按 ACP 的 <c>McpServer</c> 写全，不能照搬 Claude 的配置格式：http / sse 的 <c>headers</c> 是
/// <c>{name, value}</c> 数组且必填（没有请求头就给空数组），stdio 的 <c>args</c> 与 <c>env</c> 都给数组
///（<c>env</c> 同为 <c>{name, value}</c>）。按 schema 校验参数的运行时会以「参数无效」拒掉整个 <c>session/new</c>，
/// 每次运行都失败；宽松的运行时则丢掉对象形的请求头，回写端点的 Authorization 就这样没了，回写调用一律被拒。
/// </para>
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

            server["headers"] = value.TryGetProperty("headers", out var headers)
                ? NameValueList(headers, name, "header", logger)
                : [];

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

        stdio["args"] = value.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Array
            ? args.EnumerateArray()
                .Where(a => a.ValueKind == JsonValueKind.String)
                .Select(a => a.GetString()!)
                .ToList()
            : new List<string>();

        // ACP 的 env 是 {name, value} 数组，不是对象。
        stdio["env"] = value.TryGetProperty("env", out var env)
            ? NameValueList(env, name, "environment variable", logger)
            : [];

        return stdio;
    }

    /// <summary>
    /// 把 Claude 格式的 <c>{"key": "value"}</c> 对象翻成 ACP 的 <c>[{name, value}]</c> 数组。
    /// 值不是字符串的项丢弃并记 Warning：ACP 要求值是字符串，带着它整条 <c>session/new</c> 都会被拒。
    /// </summary>
    private static List<Dictionary<string, object?>> NameValueList(
        JsonElement source, string serverName, string kind, ILogger logger)
    {
        var entries = new List<Dictionary<string, object?>>();
        if (source.ValueKind != JsonValueKind.Object)
        {
            return entries;
        }

        foreach (var property in source.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                logger.LogWarning("MCP server '{Name}': dropping {Kind} '{Key}' because its value is not a string",
                    serverName, kind, property.Name);
                continue;
            }

            entries.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = property.Name,
                ["value"] = property.Value.GetString()
            });
        }

        return entries;
    }
}
