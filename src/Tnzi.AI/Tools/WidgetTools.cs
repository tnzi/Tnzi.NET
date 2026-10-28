namespace Tnzi.AI.Tools;

/// <summary>
/// The <c>show_widget</c> tool for the framework's own engine (tool group <c>widget</c>): an agent
/// shows a live, sandboxed HTML widget inline in its reply. Offered only while
/// <c>AI:Widgets:Enabled</c> is on, and only to agents granted the group.
/// </summary>
/// <remarks>
/// The tool checks the arguments and acknowledges; the client draws the widget from the call's stored
/// input. Name, description and the arguments' wording come from <see cref="WidgetToolDefinition"/>,
/// the same definition an application's MCP server uses for CLI agents.
/// </remarks>
[AIToolGroup(WidgetToolDefinition.GroupName, "Widgets", Description = "Show interactive HTML widgets inline in a reply")]
public class WidgetTools : IAIToolProvider
{
    /// <summary>
    /// Shows an interactive widget. Invalid arguments throw, so the call is reported as failed and the
    /// model reads what to fix.
    /// </summary>
    [AIFunction(WidgetToolDefinition.Name, WidgetToolDefinition.Description, IsReadOnly = true, IsConcurrencySafe = true)]
    public string ShowWidget(
        [AIParameter(WidgetToolDefinition.TitleParameter, WidgetToolDefinition.TitleDescription)] string title,
        [AIParameter(WidgetToolDefinition.HtmlParameter, WidgetToolDefinition.HtmlDescription)] string html)
    {
        var problem = WidgetToolDefinition.Validate(title, html);
        if (problem != null)
        {
            throw new ArgumentException(problem);
        }

        return WidgetToolDefinition.SuccessResult;
    }
}
