namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// claude's stream-json frames around a user message written to stdin while a turn runs, in the shapes claude 2.1.322
/// printed in a recorded session: the message (written with a <c>uuid</c> and <c>priority: "next"</c>) is reported
/// <c>command_lifecycle queued</c> at once, <c>started</c> right after the next tool result (it is folded into the turn),
/// <c>completed</c> before the turn's result, and that result lists it in <c>user_message_uuids</c>. A message that arrives
/// with the turn's last answer gets a turn of its own after the result (whose <c>queued_turn_count</c> is still 0).
/// </summary>
internal static class MidTurnClaudeFrames
{
    public const string Session = "s-mid-turn";

    public static string Init() => JsonSerializer.Serialize(new { type = "system", subtype = "init", session_id = Session });

    public static string Text(string id, string text) => JsonSerializer.Serialize(new
    {
        type = "assistant", uuid = $"entry-{id}", session_id = Session, parent_tool_use_id = (string?)null,
        message = new { model = "m", id, role = "assistant", content = new[] { new { type = "text", text } } }
    });

    public static string ToolUse(string id, string callId, string tool) => new JsonObject
    {
        ["type"] = "assistant", ["uuid"] = $"entry-{id}", ["session_id"] = Session, ["parent_tool_use_id"] = null,
        ["message"] = new JsonObject
        {
            ["model"] = "m", ["id"] = id, ["role"] = "assistant",
            ["content"] = new JsonArray(new JsonObject
            {
                ["type"] = "tool_use", ["id"] = callId, ["name"] = tool, ["input"] = new JsonObject { ["command"] = "npm test" }
            })
        }
    }.ToJsonString();

    public static string ToolResult(string callId, string output) => new JsonObject
    {
        ["type"] = "user", ["uuid"] = $"entry-result-{callId}", ["session_id"] = Session, ["parent_tool_use_id"] = null,
        ["message"] = new JsonObject
        {
            ["role"] = "user",
            ["content"] = new JsonArray(new JsonObject
            {
                ["tool_use_id"] = callId, ["type"] = "tool_result", ["content"] = output, ["is_error"] = false
            })
        }
    }.ToJsonString();

    public static string Lifecycle(string uuid, string state) => JsonSerializer.Serialize(new
    {
        type = "command_lifecycle", command_uuid = uuid, state, uuid = Guid.NewGuid().ToString(), session_id = Session
    });

    public static string Result(string text, params string[] userMessageUuids)
    {
        var frame = new JsonObject
        {
            ["type"] = "result", ["subtype"] = "success", ["is_error"] = false, ["result"] = text, ["session_id"] = Session,
            ["num_turns"] = 3, ["queued_turn_count"] = 0,
            ["modelUsage"] = new JsonObject { ["m"] = new JsonObject { ["inputTokens"] = 1, ["outputTokens"] = 1, ["costUSD"] = 0.01 } }
        };
        if (userMessageUuids.Length > 0)
        {
            frame["user_message_uuids"] = new JsonArray(userMessageUuids.Select(u => (JsonNode?)JsonValue.Create(u)).ToArray());
        }

        return frame.ToJsonString();
    }

    /// <summary>The uuid of a user frame written to stdin, or null when it has none (the turn's prompt).</summary>
    public static string? UuidOf(string written)
    {
        using var frame = JsonDocument.Parse(written);
        return frame.RootElement.TryGetProperty("uuid", out var uuid) ? uuid.GetString() : null;
    }
}
