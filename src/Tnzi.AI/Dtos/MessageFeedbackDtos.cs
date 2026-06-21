namespace Tnzi.AI.Dtos;

/// <summary>
/// 消息反馈提交 DTO
/// </summary>
public class MessageFeedbackDto
{
    /// <summary>Rating: true=👍, false=👎</summary>
    [Required]
    public bool Rating { get; set; }

    /// <summary>Feedback tags (for negative rating), e.g. ["incorrect","too_long"]</summary>
    public List<string>? Tags { get; set; }

    /// <summary>Optional feedback comment</summary>
    [MaxLength(2000)]
    public string? Comment { get; set; }
}

/// <summary>
/// 已知反馈标签常量
/// </summary>
public static class FeedbackTags
{
    public const string NotHelpful = "not_helpful";
    public const string Incorrect = "incorrect";
    public const string Unsafe = "unsafe";
    public const string TooLong = "too_long";
    public const string TooShort = "too_short";
    public const string Outdated = "outdated";
    public const string Other = "other";

    /// <summary>All known tags for validation</summary>
    public static readonly HashSet<string> All =
    [
        NotHelpful, Incorrect, Unsafe, TooLong, TooShort, Outdated, Other
    ];
}

/// <summary>
/// Agent 反馈统计 DTO（替代原 AgentRatingDto）
/// </summary>
public class AgentFeedbackStatsDto
{
    /// <summary>Agent ID</summary>
    public Guid AgentId { get; set; }

    /// <summary>Agent name</summary>
    public string AgentName { get; set; } = string.Empty;

    /// <summary>Total messages with feedback</summary>
    public int TotalRated { get; set; }

    /// <summary>Positive (thumbs up) count</summary>
    public int PositiveCount { get; set; }

    /// <summary>Negative (thumbs down) count</summary>
    public int NegativeCount { get; set; }

    /// <summary>Positive rate (0.0 - 1.0)</summary>
    public double PositiveRate { get; set; }

    /// <summary>Negative tag distribution (tag → count)</summary>
    public Dictionary<string, int> TagDistribution { get; set; } = [];
}
