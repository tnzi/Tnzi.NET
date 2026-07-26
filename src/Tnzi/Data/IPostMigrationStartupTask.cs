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
/// <para>Errors are isolated by the runner: a failing task logs and startup continues.</para>
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
}
