using System.ComponentModel;

namespace Tnzi.AI.Infrastructure.Tools;

/// <summary>
/// AI tool that allows agents to search for and load deferred tools.
/// Works with ToolDeferredRegistry to enable lazy tool loading.
/// </summary>
public static class ToolSearchTool
{
    [Description("Search for available tools by keyword, exact name, or name prefix. " +
        "Use 'select:name1,name2' for exact match, '+prefix rest' for name prefix search, " +
        "or just keywords for general search. Returns up to 5 matching tools.")]
    public static object Search(string query, int maxResults = 5)
    {
        Check.NotNullOrWhiteSpace(query);

        var registry = ToolDeferredRegistry.Current;
        if (registry is null || registry.Count == 0)
            return new { tools = Array.Empty<object>(), message = "No deferred tools available" };

        var results = registry.Search(query);
        var limited = results.Take(maxResults).ToList();

        return new
        {
            tools = limited.Select(t => new { name = t.Name, description = t.Description }).ToArray(),
            total_available = registry.Count,
            matched = limited.Count
        };
    }
}
