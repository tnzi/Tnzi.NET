namespace Tnzi.AI.Infrastructure.Tools;

public record DeferredToolEntry(
    string Name,
    string Description,
    object? ToolObject,
    string? SearchHint = null,
    IReadOnlyList<string>? Aliases = null,
    bool ShouldDefer = true,
    bool AlwaysLoad = false,
    int Priority = 0,
    string? ServerHint = null);

public class ToolDeferredRegistry
{
    private const int MaxResults = 5;
    private readonly List<DeferredToolEntry> _entries = [];
    private readonly HashSet<string> _loadedToolNames = new(StringComparer.OrdinalIgnoreCase);
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
        Register(name, description, toolObject, null, null);
    }

    public void Register(
        string name,
        string description,
        object? toolObject,
        string? searchHint,
        IReadOnlyList<string>? aliases,
        bool shouldDefer = true,
        bool alwaysLoad = false,
        int priority = 0,
        string? serverHint = null)
    {
        Check.NotNullOrWhiteSpace(name);
        Check.NotNullOrWhiteSpace(description);
        lock (_lock)
        {
            _entries.RemoveAll(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            var entry = new DeferredToolEntry(
                name,
                description,
                toolObject,
                searchHint,
                aliases,
                shouldDefer,
                alwaysLoad,
                priority,
                serverHint);
            _entries.Add(entry);

            if (alwaysLoad || !shouldDefer)
            {
                _loadedToolNames.Add(name);
            }
        }
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
            var restTerms = parts.Length > 1
                ? parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : [];

            return snapshot
                .Where(e => e.Name.Contains(nameKeyword, StringComparison.OrdinalIgnoreCase))
                .Select(e => (Entry: e, Score: ScoreMatchTerms(e, restTerms)))
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Entry.Priority)
                .ThenBy(x => x.Entry.Name, StringComparer.OrdinalIgnoreCase)
                .Take(MaxResults)
                .Select(x => x.Entry)
                .ToList();
        }

        // Mode 3: keyword search — 预分割查询词，避免每条目重复 split
        var queryTerms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return snapshot
            .Select(e => (Entry: e, Score: ScoreMatchTerms(e, queryTerms)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Entry.Priority)
            .ThenBy(x => x.Entry.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxResults)
            .Select(x => x.Entry)
            .ToList();
    }

    /// <summary>
    /// 分层评分：name 精确 12 > name 子串 5 > alias 精确 10 > alias 子串 4 > searchHint 4 > description 2。
    /// 多个查询词分别评分并累加。
    /// </summary>
    public static int ScoreMatch(DeferredToolEntry entry, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return 1;
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return ScoreMatchTerms(entry, terms);
    }

    /// <summary>
    /// 分层评分（接受预分割的查询词，避免每条目重复 split）
    /// </summary>
    public static int ScoreMatchTerms(DeferredToolEntry entry, string[] terms)
    {
        if (terms.Length == 0) return 1;

        var score = 0;

        foreach (var term in terms)
        {
            var termScore = 0;

            // name 精确匹配
            if (string.Equals(entry.Name, term, StringComparison.OrdinalIgnoreCase))
                termScore = Math.Max(termScore, 12);
            // name 子串匹配
            else if (entry.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                termScore = Math.Max(termScore, 5);

            // alias 匹配
            if (entry.Aliases is { Count: > 0 })
            {
                foreach (var alias in entry.Aliases)
                {
                    if (string.Equals(alias, term, StringComparison.OrdinalIgnoreCase))
                        termScore = Math.Max(termScore, 10);
                    else if (alias.Contains(term, StringComparison.OrdinalIgnoreCase))
                        termScore = Math.Max(termScore, 4);
                }
            }

            // searchHint 匹配
            if (!string.IsNullOrEmpty(entry.SearchHint) && entry.SearchHint.Contains(term, StringComparison.OrdinalIgnoreCase))
                termScore = Math.Max(termScore, 4);

            // description 匹配
            if (entry.Description.Contains(term, StringComparison.OrdinalIgnoreCase))
                termScore = Math.Max(termScore, 2);

            score += termScore;
        }

        return score;
    }

    public IReadOnlyList<DeferredToolEntry> GetDeferredEntries()
    {
        lock (_lock)
        {
            return _entries
                .Where(e => e.ShouldDefer && !e.AlwaysLoad && !_loadedToolNames.Contains(e.Name))
                .ToList();
        }
    }

    public IReadOnlyList<string> GetLoadedToolNames()
    {
        lock (_lock)
        {
            return [.. _loadedToolNames];
        }
    }

    public IReadOnlyList<DeferredToolEntry> LoadTools(IEnumerable<string> toolNames)
    {
        Check.NotNull(toolNames);

        var nameSet = new HashSet<string>(
            toolNames.Where(x => !string.IsNullOrWhiteSpace(x)),
            StringComparer.OrdinalIgnoreCase);

        if (nameSet.Count == 0)
        {
            return [];
        }

        lock (_lock)
        {
            var loaded = _entries
                .Where(e => nameSet.Contains(e.Name))
                .ToList();

            foreach (var entry in loaded)
            {
                _loadedToolNames.Add(entry.Name);
            }

            return loaded;
        }
    }

    public bool IsLoaded(string toolName)
    {
        Check.NotNullOrWhiteSpace(toolName);
        lock (_lock)
        {
            return _loadedToolNames.Contains(toolName);
        }
    }

    /// <summary>
    /// 注销工具 — 同时从 _entries 和 _loadedToolNames 中移除
    /// </summary>
    internal void Unregister(string name)
    {
        Check.NotNullOrWhiteSpace(name);
        lock (_lock)
        {
            _entries.RemoveAll(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            _loadedToolNames.Remove(name);
        }
    }

    /// <summary>
    /// 批量注销工具 — 同时从 _entries 和 _loadedToolNames 中移除
    /// </summary>
    internal void UnregisterAll(IEnumerable<string> names)
    {
        Check.NotNull(names);
        var nameSet = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        if (nameSet.Count == 0) return;

        lock (_lock)
        {
            _entries.RemoveAll(x => nameSet.Contains(x.Name));
            _loadedToolNames.ExceptWith(nameSet);
        }
    }

    public IReadOnlyList<DeferredToolEntry> Entries { get { lock (_lock) { return [.. _entries]; } } }
}
