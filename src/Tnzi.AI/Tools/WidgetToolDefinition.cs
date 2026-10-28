namespace Tnzi.AI.Tools;

/// <summary>
/// The <c>show_widget</c> tool as one definition: an agent puts a live, sandboxed HTML widget
/// (sliders, a small chart, an animated diagram) into its reply, shown inline in the message.
/// </summary>
/// <remarks>
/// <para>
/// Two ways of wiring an agent reach the same front end: the framework's own engine offers the
/// tool through <see cref="WidgetTools"/> (tool group <see cref="GroupName"/>), and an external CLI
/// agent reaches it through whatever MCP server the application runs for it. Both declare the tool
/// from these constants (name, description, input schema) and check a call with
/// <see cref="Validate(JsonElement)"/>, so the two paths cannot drift apart. The front end
/// (<c>@tnzi/ui-ai</c>, <c>createWidgetToolRenderers</c>) matches the name, also as
/// <c>mcp__&lt;server&gt;__show_widget</c>, and renders the call from its stored input.
/// </para>
/// <para>
/// The tool itself only acknowledges: the widget is drawn by the client from the call's
/// arguments, in a sandboxed frame served by the widget runner (<c>AI:Widgets</c>).
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "Inline widgets are new; the theme variables and the size limits may still change.")]
public static class WidgetToolDefinition
{
    /// <summary>The tool's name.</summary>
    public const string Name = "show_widget";

    /// <summary>The tool group <see cref="WidgetTools"/> registers it under (grant this group to an agent to offer the tool).</summary>
    public const string GroupName = "widget";

    /// <summary>The argument holding the widget's title.</summary>
    public const string TitleParameter = "title";

    /// <summary>The argument holding the widget's HTML document.</summary>
    public const string HtmlParameter = "html";

    /// <summary>The longest title accepted, in characters.</summary>
    public const int MaxTitleLength = 120;

    /// <summary>
    /// The longest HTML document accepted, in characters. A widget is meant to be small; the
    /// limit also bounds what every message holding one stores and sends to the client.
    /// </summary>
    public const int MaxHtmlLength = 200_000;

    /// <summary>The title argument's description.</summary>
    public const string TitleDescription = "A short title for the widget, shown above it (at most 120 characters).";

    /// <summary>The html argument's description.</summary>
    public const string HtmlDescription =
        "One complete, self-contained HTML document: all CSS in <style> and all JavaScript in inline <script>.";

    /// <summary>
    /// What the model reads about the tool: when to use it and how to write the HTML. Kept short:
    /// agents already know how to write HTML, they only need the rules of this frame.
    /// </summary>
    public const string Description =
        "Show an interactive widget inline in your reply: a live, sandboxed HTML document (sliders, a small chart, " +
        "an animated diagram, a calculator). Use it when seeing or playing with something explains it better than prose; " +
        "keep explaining in text around it. Rules: " +
        "(1) One self-contained document, all CSS and JS inline. Nothing loads from the network: external scripts, " +
        "stylesheets, fonts, images and fetch are blocked; draw with inline SVG, canvas or data: URIs. " +
        "No forms, alert/prompt or storage. " +
        "(2) Use the host's theme so it fits light and dark: var(--color-text-primary), var(--color-text-secondary), " +
        "var(--color-background-primary), var(--color-background-secondary), var(--color-border-primary), " +
        "var(--color-accent), var(--font-sans), var(--font-mono), var(--border-radius-md). Leave the page background transparent. " +
        "(3) The frame takes your content's height (long content scrolls) and the message column's width, as narrow as 320px: " +
        "lay out fluidly and do not set height:100% or 100vh on html or body. " +
        "(4) Links open in a new tab. Keep it small (well under 50 KB).";

    /// <summary>
    /// The input schema (JSON Schema), for an MCP server declaring the tool. The two <c>maxLength</c> values are
    /// <see cref="MaxTitleLength"/> and <see cref="MaxHtmlLength"/> (a constant string cannot interpolate a number).
    /// </summary>
    public const string InputSchemaJson = $$"""
        {
          "type": "object",
          "properties": {
            "{{TitleParameter}}": { "type": "string", "description": "{{TitleDescription}}", "maxLength": 120 },
            "{{HtmlParameter}}": { "type": "string", "description": "{{HtmlDescription}}", "maxLength": 200000 }
          },
          "required": [ "{{TitleParameter}}", "{{HtmlParameter}}" ],
          "additionalProperties": false
        }
        """;

    /// <summary>What a successful call returns to the model.</summary>
    public const string SuccessResult =
        "The widget is shown to the user inline in your reply. Do not repeat its contents; continue in text if there is more to say.";

    /// <summary>
    /// Checks a call's arguments. Returns null when they are fine, else what is wrong, worded for the model
    /// (return it as the tool's error so the model can fix the call).
    /// </summary>
    public static string? Validate(string? title, string? html)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return $"'{TitleParameter}' is required.";
        }

        if (title.Length > MaxTitleLength)
        {
            return $"'{TitleParameter}' is {title.Length} characters; the limit is {MaxTitleLength}.";
        }

        if (string.IsNullOrWhiteSpace(html))
        {
            return $"'{HtmlParameter}' is required: one complete HTML document.";
        }

        if (html.Length > MaxHtmlLength)
        {
            return $"'{HtmlParameter}' is {html.Length} characters; the limit is {MaxHtmlLength}. Make the widget smaller.";
        }

        return null;
    }

    /// <summary>
    /// Checks a call's arguments as the JSON object the agent sent (an MCP <c>tools/call</c>'s <c>arguments</c>).
    /// Returns null when they are fine, else what is wrong.
    /// </summary>
    public static string? Validate(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return $"The arguments must be an object with '{TitleParameter}' and '{HtmlParameter}'.";
        }

        return Validate(ReadString(arguments, TitleParameter), ReadString(arguments, HtmlParameter));
    }

    private static string? ReadString(JsonElement arguments, string name)
    {
        return arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
