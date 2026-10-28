namespace Tnzi.AI.Options;

/// <summary>
/// Inline widgets (<c>AI:Widgets</c>): the <c>show_widget</c> tool and the widget runner page that
/// clients load the widgets in.
/// </summary>
/// <remarks>
/// Read at startup: the runner endpoint is mapped, and the tool registered, once.
/// </remarks>
[ConfigSection("AI:Widgets")]
public class WidgetOptions
{
    /// <summary>The default path of the runner page.</summary>
    public const string DefaultRunnerPath = "/ai/widget-runner";

    /// <summary>
    /// Serve the runner page and offer the <c>show_widget</c> tool (tool group <c>widget</c>) to
    /// agents granted it. Off by default.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Where the runner page is served, relative to the application's path base. Point a separate
    /// host name at this application and give the client that host's URL of this path: the widget
    /// then also runs on an origin apart from the chat's.
    /// </summary>
    public string RunnerPath { get; set; } = DefaultRunnerPath;

    /// <summary>
    /// The CSP <c>frame-ancestors</c> source list of the runner page: which pages may embed it.
    /// <c>*</c> by default: the page only ever runs the HTML its embedder hands it, in a sandbox
    /// with no origin, so embedding it grants nothing. Narrow it to your chat's origins if you prefer.
    /// </summary>
    public string FrameAncestors { get; set; } = "*";
}
