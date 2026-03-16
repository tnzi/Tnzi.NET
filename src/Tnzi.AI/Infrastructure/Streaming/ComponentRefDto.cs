namespace Tnzi.AI.Infrastructure.Streaming;

/// <summary>
/// Rich component reference for frontend interactive rendering.
/// Emitted via SSE to instruct the frontend to render an interactive component
/// (e.g., paginated table, calendar, contract preview) instead of a Markdown table.
/// </summary>
public class ComponentRefDto
{
    /// <summary>Persistent component ID (for data fetching and session restore)</summary>
    public Guid Id { get; set; }

    /// <summary>Component type: "DataTable", "Calendar", "ContractReview", etc.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Component category: "display" (read-only) or "action" (submittable)</summary>
    public string Category { get; set; } = "display";

    /// <summary>Human-readable title shown above the component</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Total record count (for pagination info)</summary>
    public int TotalCount { get; set; }

    /// <summary>Column definitions JSON (ColumnDef[])</summary>
    public string? ColumnsJson { get; set; }

    /// <summary>Component status: "active", "superseded", "submitted", "expired"</summary>
    public string? Status { get; set; }
}
