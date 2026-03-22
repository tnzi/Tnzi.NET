namespace Tnzi.AI.Cli.Providers;

/// <summary>
/// Claude Code CLI 提供者实现。
/// 解析 claude --output-format stream-json 的 JSON 流输出。
/// </summary>
public class ClaudeCodeCliProvider : ICliAgentProvider
{
    /// <inheritdoc />
    public string ProviderName => "claude-code";

    /// <inheritdoc />
    public bool SupportsSession => true;

    /// <inheritdoc />
    public ProcessStartInfo BuildProcess(CliAgentRequest request, CliProviderOptions providerOptions)
    {
        Check.NotNull(request);
        Check.NotNull(providerOptions);

        var command = string.IsNullOrWhiteSpace(providerOptions.Command)
            ? "claude"
            : providerOptions.Command;

        var args = new List<string> { "--output-format", "stream-json", "--verbose" };

        // 模型：请求级 > Provider 默认
        var model = request.Model ?? providerOptions.DefaultModel;
        if (!string.IsNullOrWhiteSpace(model))
        {
            args.Add("--model");
            args.Add(model);
        }

        // 会话续接
        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            args.Add("--resume");
            args.Add(request.SessionId);
        }

        // 允许的工具：请求级 > Provider 默认
        var allowedTools = request.AllowedTools ?? providerOptions.AllowedTools;
        if (allowedTools is { Count: > 0 })
        {
            args.Add("--allowedTools");
            args.Add(string.Join(",", allowedTools));
        }

        var psi = new ProcessStartInfo
        {
            FileName = command,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = request.WorkingDirectory
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        // 合并环境变量：Provider 默认 + 请求级覆盖
        foreach (var kvp in providerOptions.EnvironmentVariables ?? [])
        {
            psi.Environment[kvp.Key] = kvp.Value;
        }

        if (request.EnvironmentVariables != null)
        {
            foreach (var kvp in request.EnvironmentVariables)
            {
                psi.Environment[kvp.Key] = kvp.Value;
            }
        }

        return psi;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<CliAgentEvent> ParseOutputAsync(
        StreamReader stdout,
        [EnumeratorCancellation] CancellationToken ct)
    {
        Check.NotNull(stdout);

        while (!ct.IsCancellationRequested)
        {
            var line = await stdout.ReadLineAsync(ct).ConfigureAwait(false);
            if (line == null) break; // EOF
            if (string.IsNullOrWhiteSpace(line)) continue;

            // 跳过非 JSON 行（如 stderr 混入或调试输出）
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] != '{') continue;

            JsonDocument? doc = null;
            try
            {
                doc = JsonDocument.Parse(trimmed);
            }
            catch (JsonException)
            {
                // 无法解析的行静默跳过
                continue;
            }

            using (doc)
            {
                var events = ParseJsonEvents(doc.RootElement);
                foreach (var evt in events)
                {
                    yield return evt;
                }
            }
        }
    }

    /// <inheritdoc />
    public AgentStreamChunk? MapToStreamChunk(CliAgentEvent evt)
    {
        Check.NotNull(evt);

        return evt.EventType switch
        {
            CliEventType.Text => new AgentStreamChunk { Text = evt.Content },
            CliEventType.Thinking => new AgentStreamChunk { ReasoningText = evt.Content },
            CliEventType.ToolUse => new AgentStreamChunk
            {
                IsToolCall = true,
                ToolCallNames = string.IsNullOrEmpty(evt.ToolName) ? null : [evt.ToolName]
            },
            CliEventType.ToolResult => new AgentStreamChunk { Text = evt.Content },
            CliEventType.Error => new AgentStreamChunk { Error = evt.Content },
            CliEventType.Complete => new AgentStreamChunk { FinishReason = "stop" },
            CliEventType.Status => null,
            _ => null
        };
    }

    /// <summary>
    /// 解析单行 JSON 为一个或多个 CliAgentEvent
    /// </summary>
    private static List<CliAgentEvent> ParseJsonEvents(JsonElement root)
    {
        var events = new List<CliAgentEvent>();

        if (!root.TryGetProperty("type", out var typeElement))
            return events;

        var type = typeElement.GetString();

        switch (type)
        {
            case "system":
                ParseSystemEvent(root, events);
                break;

            case "assistant":
                ParseAssistantEvent(root, events);
                break;

            case "tool_result":
                ParseToolResultEvent(root, events);
                break;

            case "result":
                ParseResultEvent(root, events);
                break;
        }

        return events;
    }

    /// <summary>
    /// 解析 system 事件（init / api_retry 等）
    /// </summary>
    private static void ParseSystemEvent(JsonElement root, List<CliAgentEvent> events)
    {
        var subtype = root.TryGetProperty("subtype", out var st) ? st.GetString() : null;

        string? sessionId = null;
        var content = subtype ?? "system";

        if (subtype == "init" && root.TryGetProperty("session_id", out var sid))
        {
            sessionId = sid.GetString();
        }

        if (subtype == "api_retry" && root.TryGetProperty("error", out var errProp))
        {
            content = errProp.GetString() ?? "api_retry";
        }

        events.Add(new CliAgentEvent
        {
            EventType = CliEventType.Status,
            Content = content,
            SessionId = sessionId
        });
    }

    /// <summary>
    /// 解析 assistant 事件（包含 text / tool_use / thinking 内容块）
    /// </summary>
    private static void ParseAssistantEvent(JsonElement root, List<CliAgentEvent> events)
    {
        if (!root.TryGetProperty("message", out var message)) return;
        if (!message.TryGetProperty("content", out var content)) return;
        if (content.ValueKind != JsonValueKind.Array) return;

        foreach (var block in content.EnumerateArray())
        {
            if (!block.TryGetProperty("type", out var blockType)) continue;

            var blockTypeStr = blockType.GetString();

            switch (blockTypeStr)
            {
                case "text":
                    if (block.TryGetProperty("text", out var textVal))
                    {
                        events.Add(new CliAgentEvent
                        {
                            EventType = CliEventType.Text,
                            Content = textVal.GetString() ?? string.Empty
                        });
                    }
                    break;

                case "thinking":
                    if (block.TryGetProperty("thinking", out var thinkVal))
                    {
                        events.Add(new CliAgentEvent
                        {
                            EventType = CliEventType.Thinking,
                            Content = thinkVal.GetString() ?? string.Empty
                        });
                    }
                    break;

                case "tool_use":
                {
                    var toolName = block.TryGetProperty("name", out var nameVal) ? nameVal.GetString() : null;
                    var toolId = block.TryGetProperty("id", out var idVal) ? idVal.GetString() : null;

                    events.Add(new CliAgentEvent
                    {
                        EventType = CliEventType.ToolUse,
                        ToolName = toolName,
                        ToolId = toolId
                    });
                    break;
                }
            }
        }
    }

    /// <summary>
    /// 解析 tool_result 事件
    /// </summary>
    private static void ParseToolResultEvent(JsonElement root, List<CliAgentEvent> events)
    {
        var toolId = root.TryGetProperty("tool_use_id", out var tidVal) ? tidVal.GetString() : null;
        var isError = root.TryGetProperty("is_error", out var ieVal) && ieVal.GetBoolean();

        var content = ExtractToolResultContent(root);

        events.Add(new CliAgentEvent
        {
            EventType = isError ? CliEventType.Error : CliEventType.ToolResult,
            Content = content,
            ToolId = toolId,
            IsError = isError
        });
    }

    /// <summary>
    /// 解析 result 事件（最终结果，生成 Text + Complete 两个事件）
    /// </summary>
    private static void ParseResultEvent(JsonElement root, List<CliAgentEvent> events)
    {
        var resultText = root.TryGetProperty("result", out var rVal)
            ? rVal.GetString() ?? string.Empty
            : string.Empty;

        var isError = root.TryGetProperty("is_error", out var ieVal) && ieVal.GetBoolean();

        // 提取元数据
        var metadata = new Dictionary<string, JsonElement>();
        if (root.TryGetProperty("total_cost_usd", out var cost))
            metadata["total_cost_usd"] = cost;
        if (root.TryGetProperty("duration_ms", out var duration))
            metadata["duration_ms"] = duration;
        if (root.TryGetProperty("num_turns", out var turns))
            metadata["num_turns"] = turns;

        // 第一个事件：结果文本
        if (!string.IsNullOrEmpty(resultText))
        {
            events.Add(new CliAgentEvent
            {
                EventType = isError ? CliEventType.Error : CliEventType.Text,
                Content = resultText,
                IsError = isError
            });
        }

        // 第二个事件：完成标记
        events.Add(new CliAgentEvent
        {
            EventType = CliEventType.Complete,
            Metadata = metadata.Count > 0 ? metadata : null
        });
    }

    /// <summary>
    /// 提取 tool_result 的 content（支持 string 和 array 两种格式）
    /// </summary>
    private static string ExtractToolResultContent(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var contentElement))
            return string.Empty;

        // 字符串格式
        if (contentElement.ValueKind == JsonValueKind.String)
            return contentElement.GetString() ?? string.Empty;

        // 数组格式：拼接所有 text 类型块
        if (contentElement.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var item in contentElement.EnumerateArray())
            {
                if (item.TryGetProperty("type", out var t) && t.GetString() == "text" &&
                    item.TryGetProperty("text", out var tv))
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(tv.GetString());
                }
            }
            return sb.ToString();
        }

        return string.Empty;
    }
}
