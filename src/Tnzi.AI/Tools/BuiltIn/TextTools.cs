namespace Tnzi.AI.Tools.BuiltIn;

/// <summary>
/// Built-in text processing tools for AI agents
/// </summary>
[AIToolGroup("text", "Text Tools", "Analyze and transform text content")]
public class TextTools : IAIToolProvider
{
    private readonly ILogger<TextTools> _logger;

    public TextTools(ILogger<TextTools> logger)
    {
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// Get text statistics
    /// </summary>
    [AIFunction("text_statistics", "Count characters, words, and lines in the given text",
        IsReadOnly = true, IsConcurrencySafe = true, SearchHint = "count words lines characters")]
    public Task<object> GetTextStatisticsAsync(
        [AIParameter("text", "The text to analyze")]
        string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Task.FromResult<object>(new { error = "Text cannot be empty" });
        }

        return Task.FromResult<object>(new
        {
            character_count = text.Length,
            word_count = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length,
            line_count = text.Split('\n').Length
        });
    }

    /// <summary>
    /// Convert text case
    /// </summary>
    [AIFunction("convert_case", "Convert text to uppercase, lowercase, or title case",
        IsReadOnly = true, IsConcurrencySafe = true)]
    public Task<string> ConvertCaseAsync(
        [AIParameter("text", "The text to convert")]
        string text,
        [AIParameter("case", "Target case: 'upper', 'lower', or 'title'")]
        string targetCase)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Task.FromResult(string.Empty);
        }

        // 固定 InvariantCulture：大小写转换若跟随当前请求区域，土耳其语等 locale 下
        // i/I 的映射会变（Turkish-I 问题），同一输入在不同请求得到不同结果。
        return (targetCase ?? string.Empty).ToLowerInvariant() switch
        {
            "upper" => Task.FromResult(text.ToUpperInvariant()),
            "lower" => Task.FromResult(text.ToLowerInvariant()),
            "title" => Task.FromResult(CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text.ToLowerInvariant())),
            _ => Task.FromResult(text)
        };
    }
}
