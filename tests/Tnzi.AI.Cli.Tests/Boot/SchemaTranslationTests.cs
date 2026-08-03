namespace Tnzi.AI.Cli.Tests;

/// <summary>All four entity configurations at once.</summary>
public class CliSchemaDbContext : TnziDbContext<CliSchemaDbContext>
{
    public CliSchemaDbContext(DbContextOptions<CliSchemaDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new CliRuntimeConfiguration());
        modelBuilder.ApplyConfiguration(new CliAgentBindingConfiguration());
        modelBuilder.ApplyConfiguration(new CliRunConfiguration());
        modelBuilder.ApplyConfiguration(new CliRunMessageConfiguration());
        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

/// <summary>
/// The four entity configurations must translate into a schema a database will accept.
/// </summary>
/// <remarks>
/// Entity configuration is the one layer a compile does not check: a hardcoded index
/// filter, a provider-specific column type, or an index over a column that does not
/// exist all build fine and fail when the schema is created. No application loads this
/// module, so no migration has ever been generated from these four files - this is
/// where they get translated for the first time.
/// <para>
/// The other suites happened to cover three of them (their contexts needed those
/// tables); <c>CliRunMessage</c> was in none of them.
/// </para>
/// </remarks>
public class SchemaTranslationTests : IntegratedTestBase<CliSchemaDbContext>
{
    private async Task<HashSet<string>> ReadObjectsAsync(string type)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var connection = DbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT name FROM sqlite_master WHERE type = '{type}'";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    [Fact]
    public async Task AllFourEntities_ProduceTables_UnderTheSharedAiPrefix()
    {
        // The module declares TableNamePrefix "AI", deliberately sharing the AI core's
        // prefix: what was split is the assembly, not the schema. Nothing in a build
        // checks that the convention actually reaches a new module's entities, and
        // getting it wrong would put four unprefixed tables in a consumer's database.
        var tables = await ReadObjectsAsync("table");

        tables.ShouldContain("AI_CliRuntime");
        tables.ShouldContain("AI_CliAgentBinding");
        tables.ShouldContain("AI_CliRun");
        tables.ShouldContain("AI_CliRunMessage");

        tables.ShouldNotContain("CliRun", "an unprefixed table means the prefix convention did not run");
    }

    [Fact]
    public async Task Indexes_AreCreated_IncludingTheFilteredOnes()
    {
        // A filtered index is where database-specific SQL leaks in. If one had been
        // hardcoded rather than built through IndexFilterFactory, schema creation is
        // where it surfaces.
        var indexes = await ReadObjectsAsync("index");

        indexes.ShouldNotBeEmpty();
        indexes.Count.ShouldBeGreaterThan(4, "each of the four tables declares at least one index");
    }

    [Fact]
    public async Task WriteBackTokenHash_IsAColumn_AndIsNotProjectedAway()
    {
        // The column carries a credential hash and is marked [AuditIgnore]. That
        // attribute must keep it out of audit collection without removing it from the
        // model - if it ever did, run-scoped write-back would break with no failing test.
        var run = new CliRun
        {
            AgentId = Guid.NewGuid(),
            CliRuntimeId = Guid.NewGuid(),
            Status = CliRunStatus.Queued,
            Prompt = "x",
            WriteBackTokenHash = "hash-value"
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();

        var stored = await DbContext.Set<CliRun>().AsNoTracking().FirstAsync(r => r.Id == run.Id);
        stored.WriteBackTokenHash.ShouldBe("hash-value");
    }

    [Fact]
    public async Task CliRunMessage_RoundTrips()
    {
        // This configuration had never been translated to DDL before this test existed.
        var message = new CliRunMessage
        {
            RunId = Guid.NewGuid(),
            Sequence = 1,
            Type = CliAgentEventType.Text,
            Content = "hello"
        };
        DbContext.Set<CliRunMessage>().Add(message);
        await DbContext.SaveChangesAsync();

        var stored = await DbContext.Set<CliRunMessage>().AsNoTracking().FirstAsync(m => m.Id == message.Id);
        stored.Sequence.ShouldBe(1);
        stored.Type.ShouldBe(CliAgentEventType.Text);
        stored.Content.ShouldBe("hello");
    }
}
