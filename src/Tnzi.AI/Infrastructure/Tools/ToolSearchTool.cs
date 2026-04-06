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
        "or just keywords for general search. Returns up to 5 matching tools with their full parameter schemas, and can load them into the current deferred-tool session.")]
    public static object Search(string query, int maxResults = 5, bool load = true)
    {
        Check.NotNullOrWhiteSpace(query);

        var registry = ToolDeferredRegistry.Current;
        if (registry is null || registry.Count == 0)
            return new { tools = Array.Empty<object>(), message = "No deferred tools available" };

        var results = registry.Search(query);
        var limited = results.Take(maxResults).ToList();
        var loaded = load ? registry.LoadTools(limited.Select(t => t.Name)) : [];

        return new
        {
            tools = limited.Select(t => new
            {
                name = t.Name,
                description = t.Description,
                parameters_schema = ExtractParametersSchema(t.ToolObject),
                searchHint = t.SearchHint,
                aliases = t.Aliases ?? Array.Empty<string>(),
                shouldDefer = t.ShouldDefer,
                alwaysLoad = t.AlwaysLoad,
                priority = t.Priority,
                serverHint = t.ServerHint,
                loaded = registry.IsLoaded(t.Name)
            }).ToArray(),
            loaded_tools = loaded.Select(t => t.Name).ToArray(),
            total_available = registry.Count,
            matched = limited.Count,
            loaded_count = loaded.Count
        };
    }

    /// <summary>
    /// 从 ToolObject 中提取参数 JSON Schema。
    /// 支持 AIFunctionDeclaration（含 AIFunction）和 AITool（通过 GetService 获取内部 AIFunctionDeclaration）。
    /// </summary>
    private static string? ExtractParametersSchema(object? toolObject)
    {
        if (toolObject is null)
            return null;

        // AIFunctionDeclaration（AIFunction 的基类）直接暴露 JsonSchema
        if (toolObject is AIFunctionDeclaration funcDecl)
        {
            return SerializeJsonElement(funcDecl.JsonSchema);
        }

        // AITool 可能内部包装了 AIFunctionDeclaration，通过 GetService 获取
        if (toolObject is AITool aiTool)
        {
            var innerDecl = aiTool.GetService<AIFunctionDeclaration>();
            if (innerDecl is not null)
            {
                return SerializeJsonElement(innerDecl.JsonSchema);
            }
        }

        return null;
    }

    /// <summary>
    /// 将 JsonElement 序列化为紧凑 JSON 字符串
    /// </summary>
    private static string? SerializeJsonElement(JsonElement schema)
    {
        // JsonElement.ValueKind == Undefined 表示无 schema
        if (schema.ValueKind == JsonValueKind.Undefined)
            return null;

        return schema.GetRawText();
    }
}
