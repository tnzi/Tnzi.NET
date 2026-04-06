namespace Tnzi.AI.Coder.Context;

/// <summary>
/// Context management tools — summarize and condense long conversations
/// </summary>
[AIToolGroup("context", "Context Management", "Summarize and manage conversation context")]
public class ContextTools : IAIToolProvider
{
    private readonly IMemoryStore _memoryStore;
    private readonly ILogger<ContextTools> _logger;

    /// <summary>
    /// Maximum characters before recommending condensation
    /// </summary>
    private const int CondensationThresholdChars = 50_000;

    public ContextTools(IMemoryStore memoryStore, ILogger<ContextTools> logger)
    {
        _memoryStore = Check.NotNull(memoryStore);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// Analyze current conversation length and recommend condensation
    /// </summary>
    [AIFunction("context_stats", "Analyze conversation context size and provide recommendations", IsReadOnly = true, IsConcurrencySafe = true)]
    public Task<object> ContextStatsAsync(
        [AIParameter("conversation_text", "Full conversation text to analyze")] string conversationText)
    {
        if (string.IsNullOrWhiteSpace(conversationText))
        {
            return Task.FromResult<object>(new { error = "Conversation text cannot be empty" });
        }

        var charCount = conversationText.Length;
        var estimatedTokens = charCount / 3; // rough estimate: ~3 chars per token
        var turnCount = CountTurns(conversationText);
        var needsCondensation = charCount > CondensationThresholdChars;

        _logger.LogDebug("Context stats: {Chars} chars, ~{Tokens} tokens, {Turns} turns, needsCondensation={Needs}",
            charCount, estimatedTokens, turnCount, needsCondensation);

        return Task.FromResult<object>(new
        {
            character_count = charCount,
            estimated_tokens = estimatedTokens,
            turn_count = turnCount,
            needs_condensation = needsCondensation,
            threshold_chars = CondensationThresholdChars,
            recommendation = needsCondensation
                ? "Conversation is large. Consider using condense_context to summarize older parts."
                : "Conversation size is within limits."
        });
    }

    /// <summary>
    /// Condense conversation by extracting key information and creating a summary
    /// </summary>
    /// <remarks>
    /// This is a local condensation tool that extracts structured information from conversation text.
    /// It does not call an LLM — it performs heuristic extraction of key facts, decisions, and tasks.
    /// For LLM-based summarization, use the SummarizeChatReducer in the Agent pipeline.
    /// </remarks>
    [AIFunction("condense_context", "Extract key information from a conversation to create a condensed summary", SearchHint = "condense compress context summary")]
    public async Task<object> CondenseContextAsync(
        [AIParameter("conversation_text", "Full conversation text to condense")] string conversationText,
        [AIParameter("scope", "Memory scope to store the condensed summary", false)] string scope = "context-summary",
        [AIParameter("keep_recent_turns", "Number of recent turns to keep verbatim", false)] int keepRecentTurns = 5)
    {
        if (string.IsNullOrWhiteSpace(conversationText))
        {
            return new { error = "Conversation text cannot be empty" };
        }

        try
        {
            var lines = conversationText.Split('\n');
            var totalLines = lines.Length;

            // Extract key sections
            var decisions = ExtractPatterns(lines, ["decided", "agreed", "confirmed", "will use", "chosen", "selected"]);
            var tasks = ExtractPatterns(lines, ["TODO", "task:", "need to", "should", "must", "action item"]);
            var codeFiles = ExtractPatterns(lines, [".cs", ".ts", ".js", ".py", ".json", ".xml", ".yaml", ".yml", ".sql"]);
            var errors = ExtractPatterns(lines, ["error", "exception", "failed", "bug", "issue", "fix"]);

            // Build condensed summary
            var summary = new StringBuilder();
            summary.AppendLine("# Conversation Summary");
            summary.AppendLine($"- Original: {totalLines} lines, ~{conversationText.Length} chars");
            summary.AppendLine($"- Condensed at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            summary.AppendLine();

            if (decisions.Count > 0)
            {
                summary.AppendLine("## Key Decisions");
                foreach (var d in decisions.Take(20))
                {
                    summary.AppendLine($"- {d.Trim()}");
                }
                summary.AppendLine();
            }

            if (tasks.Count > 0)
            {
                summary.AppendLine("## Tasks & Action Items");
                foreach (var t in tasks.Take(20))
                {
                    summary.AppendLine($"- {t.Trim()}");
                }
                summary.AppendLine();
            }

            if (codeFiles.Count > 0)
            {
                summary.AppendLine("## Referenced Files");
                var uniqueFiles = codeFiles
                    .SelectMany(ExtractFilePaths)
                    .Distinct()
                    .Take(30);
                foreach (var f in uniqueFiles)
                {
                    summary.AppendLine($"- `{f}`");
                }
                summary.AppendLine();
            }

            if (errors.Count > 0)
            {
                summary.AppendLine("## Errors & Issues");
                foreach (var e in errors.Take(15))
                {
                    summary.AppendLine($"- {e.Trim()}");
                }
                summary.AppendLine();
            }

            // Keep recent turns verbatim
            var recentTurns = ExtractRecentTurns(lines, keepRecentTurns);
            if (recentTurns.Count > 0)
            {
                summary.AppendLine("## Recent Context (verbatim)");
                foreach (var turn in recentTurns)
                {
                    summary.AppendLine(turn);
                }
            }

            var summaryText = summary.ToString();

            // Persist to memory store
            await _memoryStore.WriteAsync(scope, summaryText);

            var compressionRatio = conversationText.Length > 0
                ? (double)summaryText.Length / conversationText.Length
                : 1.0;

            _logger.LogDebug("Condensed context: {Original} -> {Condensed} chars ({Ratio:P0})",
                conversationText.Length, summaryText.Length, compressionRatio);

            return new
            {
                success = true,
                original_chars = conversationText.Length,
                condensed_chars = summaryText.Length,
                compression_ratio = Math.Round(compressionRatio, 3),
                decisions_found = decisions.Count,
                tasks_found = tasks.Count,
                files_referenced = codeFiles.Count,
                errors_found = errors.Count,
                scope,
                message = "Context condensed and saved to memory. Use memory_read to retrieve."
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to condense context");
            return new { error = $"Failed to condense context: {ex.Message}" };
        }
    }

    private static List<string> ExtractPatterns(string[] lines, string[] keywords)
    {
        var results = new List<string>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            foreach (var keyword in keywords)
            {
                if (line.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(line.Length > 200 ? line[..200] + "..." : line);
                    break;
                }
            }
        }
        return results;
    }

    private static List<string> ExtractFilePaths(string line)
    {
        var paths = new List<string>();
        // Match common file path patterns
        var matches = Regex.Matches(line, @"[\w./\\-]+\.\w{1,10}");
        foreach (Match match in matches)
        {
            var path = match.Value;
            if (path.Contains('.') && !path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                paths.Add(path);
            }
        }
        return paths;
    }

    private static int CountTurns(string text)
    {
        var count = 0;
        var lines = text.Split('\n');
        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("user:", StringComparison.OrdinalIgnoreCase) ||
                line.TrimStart().StartsWith("User:", StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }
        return Math.Max(count, 1);
    }

    private static List<string> ExtractRecentTurns(string[] lines, int turnCount)
    {
        if (turnCount <= 0) return [];

        // Walk backwards to find the last N turn boundaries
        var turnBoundaries = new List<int>();
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("user:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("User:", StringComparison.OrdinalIgnoreCase))
            {
                turnBoundaries.Add(i);
                if (turnBoundaries.Count >= turnCount) break;
            }
        }

        if (turnBoundaries.Count == 0) return [];

        var startIndex = turnBoundaries[^1]; // earliest boundary
        var results = new List<string>();
        for (var i = startIndex; i < lines.Length; i++)
        {
            results.Add(lines[i]);
        }
        return results;
    }
}
