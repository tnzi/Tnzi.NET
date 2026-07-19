namespace Tnzi.Authorization.Security;

/// <summary>
/// Runs <see cref="AuthorizationModule.RunStartupTasksAsync"/> (permission-catalogue
/// seed + <c>PermissionManager.RefreshAsync</c> + built-in super-admin role seed +
/// first-super-admin bootstrap + role-existence diagnostics) AFTER database migrations,
/// via the framework's post-migration startup pipeline.
/// </summary>
/// <remarks>
/// This work needs the schema to exist. It used to run in <c>AuthorizationModule</c>'s
/// module-init hook, which executes BEFORE migrations, so on a brand-new empty database
/// it failed silently and required a second boot to take effect. Registered as an
/// <see cref="IPostMigrationStartupTask"/>, it now runs on the post-migration pass —
/// every boot, independent of the seed gate (the catalogue sync is framework
/// infrastructure, not demo data).
/// </remarks>
internal sealed class AuthorizationStartupTask : IPostMigrationStartupTask
{
    public Task ExecuteAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        => AuthorizationModule.RunStartupTasksAsync(serviceProvider);
}
