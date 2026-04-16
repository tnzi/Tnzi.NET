namespace Tnzi.Hangfire.Dtos;

/// <summary>
/// Read-only projection of a Hangfire recurring job, shaped for the admin
/// ScheduledJob page. Mirrors the fields of <c>Hangfire.Storage.Monitoring.RecurringJobDto</c>
/// that admins care about, without re-exporting the Hangfire type through the
/// Tnzi API surface.
/// </summary>
public class ScheduledJobDto
{
    /// <summary>
    /// Recurring job identifier (unique within the Hangfire storage).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Cron expression driving the schedule.
    /// </summary>
    public string? Cron { get; set; }

    /// <summary>
    /// Queue this job will be enqueued to when it fires.
    /// </summary>
    public string? Queue { get; set; }

    /// <summary>
    /// Timestamp of the last execution, null if it has never run.
    /// </summary>
    public DateTime? LastExecution { get; set; }

    /// <summary>
    /// Projected next execution time, null if the job is paused or expired.
    /// </summary>
    public DateTime? NextExecution { get; set; }

    /// <summary>
    /// When the recurring job was first registered.
    /// </summary>
    public DateTime? CreatedAt { get; set; }

    /// <summary>
    /// IANA timezone identifier, if the job was registered with one.
    /// </summary>
    public string? TimeZoneId { get; set; }

    /// <summary>
    /// The last one-off background job id produced by this recurring job.
    /// </summary>
    public string? LastJobId { get; set; }

    /// <summary>
    /// State of the last produced background job (Succeeded / Failed / ...).
    /// </summary>
    public string? LastJobState { get; set; }

    /// <summary>
    /// Error message from the last execution attempt, if any.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// True when Hangfire has marked this recurring job as removed.
    /// Removed jobs stop firing but remain visible until they age out.
    /// </summary>
    public bool Removed { get; set; }
}
