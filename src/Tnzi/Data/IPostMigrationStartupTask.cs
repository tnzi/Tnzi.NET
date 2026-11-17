namespace Tnzi.Data;

/// <summary>
/// A startup task that runs AFTER database migrations are applied, on EVERY boot,
/// independent of the seed gate.
/// </summary>
/// <remarks>
/// <para>Unlike <see cref="IDataSeeder"/> (demo / reference data that is skipped in
/// production and gated by the seed switch), this is for <b>framework infrastructure
/// that must run each boot once the schema exists</b> - e.g. syncing the code-declared
/// permission catalogue to the database and refreshing the in-memory snapshot.</para>
/// <para>It is <b>not</b> discovered by <c>DataSeederManager</c> (which scans only
/// <see cref="IDataSeeder"/>), so registering one never produces a duplicate seeder.
/// The framework resolves and runs all registered tasks after the migration phase
/// (see <c>RunPostMigrationStartupTasksAsync</c>), which is why a task's DB access is
/// safe on a brand-new empty database in a single boot - the tables already exist by
/// the time it runs.</para>
/// <para>Errors are isolated by the runner: a failing task logs and startup continues.
/// A task that failed is <b>retried in the background</b> with exponential backoff until
/// it completes once (a database that is unreachable for a few minutes at startup is the
/// case this exists for), and the health check <c>startup-tasks</c> reports it until then:
/// <see cref="RequiredForReadiness"/> decides whether that report takes the instance out
/// of <c>/health/ready</c> (Unhealthy) or only shows it (Degraded).</para>
/// <para>So an implementation must <b>throw</b> when its work is not done, rather than log
/// and return: a swallowed failure is never retried. It must also be idempotent and safe
/// to run while the app serves requests and on several instances at once, since a retry
/// runs after the host has started.</para>
/// </remarks>
[StableApi(Since = "0.1.0")]
public interface IPostMigrationStartupTask
{
    /// <summary>
    /// Execute the post-migration work. Called once per application boot with the
    /// <b>root</b> service provider - an implementer doing scoped work (repositories /
    /// DbContext) MUST create its own scope (<c>serviceProvider.CreateAsyncScope()</c>)
    /// rather than resolve scoped services directly off the root.
    /// </summary>
    Task ExecuteAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the app must not serve until this task has completed once (default
    /// <see langword="false"/>). While a task that failed has not completed, the health
    /// check <c>startup-tasks</c> reports Unhealthy for a required task (so
    /// <c>/health/ready</c> answers 503) and Degraded for any other (200, visible in the
    /// report). Required is for work whose absence makes serving wrong (a security repair),
    /// not for work whose absence only leaves a feature short.
    /// </summary>
    bool RequiredForReadiness => false;
}
