
namespace Tnzi.AspNetCore.Dtos;

/// <summary>
/// Exception summary DTO containing aggregated exception statistics
/// </summary>
public class ExceptionSummaryDto
{
    /// <summary>
    /// Total number of exceptions in the time window
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Exception counts grouped by exception type
    /// </summary>
    public Dictionary<string, int> ByType { get; set; } = new();

    /// <summary>
    /// Exception counts grouped by HTTP status code
    /// </summary>
    public Dictionary<int, int> ByStatusCode { get; set; } = new();

    /// <summary>
    /// Exception counts grouped by error code
    /// </summary>
    public Dictionary<string, int> ByErrorCode { get; set; } = new();

    /// <summary>
    /// Top N exceptions ranked by occurrence count
    /// </summary>
    public List<ExceptionTypeCountDto> TopExceptions { get; set; } = new();

    /// <summary>
    /// Most recent exceptions
    /// </summary>
    public List<ExceptionEntryDto> RecentExceptions { get; set; } = new();

    /// <summary>
    /// Start of the statistics time window (UTC)
    /// </summary>
    public DateTime Since { get; set; }
}

/// <summary>
/// Exception type count DTO for top exceptions ranking
/// </summary>
public class ExceptionTypeCountDto
{
    /// <summary>
    /// Exception type name
    /// </summary>
    public string ExceptionType { get; set; } = string.Empty;

    /// <summary>
    /// Number of occurrences
    /// </summary>
    public int Count { get; set; }
}

/// <summary>
/// Individual exception entry DTO
/// </summary>
public class ExceptionEntryDto
{
    /// <summary>
    /// Exception type name (e.g., "NotFoundException", "BusinessException")
    /// </summary>
    public string ExceptionType { get; set; } = string.Empty;

    /// <summary>
    /// Exception message
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// HTTP status code associated with the exception
    /// </summary>
    public int? StatusCode { get; set; }

    /// <summary>
    /// Business error code
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Request ID that triggered the exception
    /// </summary>
    public string? RequestId { get; set; }

    /// <summary>
    /// Timestamp when the exception occurred (UTC)
    /// </summary>
    public DateTime Timestamp { get; set; }
}
