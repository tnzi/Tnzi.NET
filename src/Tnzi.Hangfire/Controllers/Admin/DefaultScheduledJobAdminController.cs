using global::Hangfire;
using global::Hangfire.Storage;

namespace Tnzi.Hangfire.Controllers.Admin;

/// <summary>
/// Admin controller for Hangfire recurring (scheduled) jobs. Backs the
/// Phase 3 System ScheduledJob admin page.
///
/// Accesses Hangfire's recurring-job storage directly via
/// <c>JobStorage.Current.GetConnection().GetRecurringJobs()</c> and
/// <c>RecurringJob.TriggerJob / RemoveIfExists</c>. Deliberately does NOT
/// go through <see cref="IBackgroundJobManager"/> because that interface
/// is immediate-mode oriented and does not expose a list API; adding a
/// list method there would force every alternative implementation (test
/// doubles, in-memory) to grow surface they do not need.
///
/// Pause/resume is not implemented because Hangfire has no first-class
/// paused state for recurring jobs, and the canonical workaround is to
/// delete + recreate, which is a UX decision the admin UI should drive
/// rather than the backend hiding.
/// </summary>
[DefaultController]
[Route("admin/scheduled-jobs")]
[ApiAuthorize(PermissionName = "system.scheduledJob.view")]
public class DefaultScheduledJobAdminController : ApiAdminControllerBase
{
    /// <summary>
    /// List every recurring job currently known to Hangfire storage.
    /// Recurring job counts are typically in the tens to low hundreds,
    /// so we return all of them in a single call rather than paginate.
    /// </summary>
    [HttpGet]
    public virtual ApiResult<IEnumerable<ScheduledJobDto>> GetList()
    {
        using var connection = JobStorage.Current.GetConnection();
        var jobs = connection.GetRecurringJobs();
        var dtos = jobs.Select(MapToDto).ToList();
        return Result.Success<IEnumerable<ScheduledJobDto>>(dtos).ToApiResult();
    }

    /// <summary>
    /// Fetch a single recurring job by id.
    /// </summary>
    [HttpGet("{id}")]
    public virtual ApiResult<ScheduledJobDto> Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return Result.Failure<ScheduledJobDto>("id is required", 400).ToApiResult();

        using var connection = JobStorage.Current.GetConnection();
        var jobs = connection.GetRecurringJobs();
        var job = jobs.FirstOrDefault(j => j.Id == id);
        if (job is null)
            return Result.Failure<ScheduledJobDto>($"Recurring job '{id}' not found", 404).ToApiResult();

        return Result.Success(MapToDto(job)).ToApiResult();
    }

    /// <summary>
    /// Manually trigger a recurring job so it runs immediately without
    /// waiting for its next cron fire.
    /// </summary>
    [HttpPost("{id}/trigger")]
    [ApiAuthorize(PermissionName = "system.scheduledJob.execute")]
    public virtual ApiResult Trigger(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return Result.Failure("id is required", 400).ToApiResult();

        RecurringJob.TriggerJob(id);
        return Result.Success().ToApiResult();
    }

    /// <summary>
    /// Remove a recurring job from Hangfire storage. Subsequent scheduler
    /// cycles will not fire it; existing in-flight invocations complete.
    /// </summary>
    [HttpDelete("{id}")]
    [ApiAuthorize(PermissionName = "system.scheduledJob.delete")]
    public virtual ApiResult Delete(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return Result.Failure("id is required", 400).ToApiResult();

        RecurringJob.RemoveIfExists(id);
        return Result.Success().ToApiResult();
    }

    private static ScheduledJobDto MapToDto(RecurringJobDto job) => new()
    {
        Id = job.Id,
        Cron = job.Cron,
        Queue = job.Queue,
        LastExecution = job.LastExecution,
        NextExecution = job.NextExecution,
        CreatedAt = job.CreatedAt,
        TimeZoneId = job.TimeZoneId,
        LastJobId = job.LastJobId,
        LastJobState = job.LastJobState,
        Error = job.Error,
        Removed = job.Removed,
    };
}
