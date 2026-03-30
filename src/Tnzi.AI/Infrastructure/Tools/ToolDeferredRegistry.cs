namespace Tnzi.AI.Infrastructure.Tools;

public record DeferredToolEntry(string Name, string Description, object? ToolObject);

public class ToolDeferredRegistry
{
    private const int MaxResults = 5;
    private readonly List<DeferredToolEntry> _entries = [];
    private readonly object _lock = new();
    private static readonly AsyncLocal<ToolDeferredRegistry?> _current = new();

    public static ToolDeferredRegistry? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }

    public int Count => _entries.Count;

    public void Register(string name, string description, object? toolObject)
    {
        Check.NotNullOrWhiteSpace(name);
        Check.NotNullOrWhiteSpace(description);
        lock (_lock) { _entries.Add(new DeferredToolEntry(name, description, toolObject)); }
    }

    public IReadOnlyList<DeferredToolEntry> Search(string query)
    {
        Check.NotNullOrWhiteSpace(query);

        List<DeferredToolEntry> snapshot;
        lock (_lock) { snapshot = [.. _entries]; }

        // Mode 1: select:name1,name2
        if (query.StartsWith("select:", StringComparison.OrdinalIgnoreCase))
        {
            var names = query[7..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var nameSet = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            return snapshot.Where(e => nameSet.Contains(e.Name)).Take(MaxResults).ToList();
        }

        // Mode 2: +keyword rest
        if (query.StartsWith('+'))
        {
            var parts = query[1..].Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var nameKeyword = parts[0];
            var rest = parts.Length > 1 ? parts[1] : "";

            return snapshot
                .Where(e => e.Name.Contains(nameKeyword, StringComparison.OrdinalIgnoreCase))
                .Select(e => (Entry: e, Score: ScoreMatch(e, rest)))
                .OrderByDescending(x => x.Score)
                .Take(MaxResults)
                .Select(x => x.Entry)
                .ToList();
        }

        // Mode 3: keyword search
        return snapshot
            .Select(e => (Entry: e, Score: ScoreMatch(e, query)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(MaxResults)
            .Select(x => x.Entry)
            .ToList();
    }

    private static int ScoreMatch(DeferredToolEntry entry, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return 1;

        var score = 0;
        if (entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            score += 2;
        if (entry.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
            score += 1;
        return score;
    }

    public IReadOnlyList<DeferredToolEntry> Entries { get { lock (_lock) { return [.. _entries]; } } }
}
