namespace Tnzi.Data;

public interface IDbInitializer
{
    Task MigrateAsync(CancellationToken cancellationToken = default);
    Task SeedAsync(CancellationToken cancellationToken = default);
}

public interface IDataSeeder
{
    Task SeedAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default);
}

