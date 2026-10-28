using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// Two replicas never run two turns of one thread at the same time, even when they claim in the same instant.
/// </summary>
/// <remarks>
/// The busy-thread check and the claiming update are two statements. Two replicas can both pass the check
/// before either has written its claim, each claim a different queued run of the same thread, and run them
/// concurrently in the same per-thread workspace. A replica that finds another active run of the thread right
/// after its own claim landed gives the claim back. The race is reproduced by letting "another replica" claim
/// the sibling run in the gap between this replica's check and its update.
/// </remarks>
public class CliRunClaimThreadRaceTests : IntegratedTestBase<CliQueueDbContext>
{
    private static readonly Guid AgentId = Guid.Parse("11111111-1111-1111-1111-1111111111d1");
    private static readonly Guid RuntimeId = Guid.Parse("22222222-2222-2222-2222-2222222222d2");

    // Field initialisers run before the base constructor, which builds the DbContext options.
    private readonly BeforeNextUpdateInterceptor _interceptor = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<CliRun, Guid>, EFCoreRepository<CliQueueDbContext, CliRun, Guid>>();
        services.AddScoped<IRepository<CliRuntime, Guid>, EFCoreRepository<CliQueueDbContext, CliRuntime, Guid>>();
    }

    protected override void ConfigureDbContextOptions(DbContextOptionsBuilder options)
        => options.AddInterceptors(_interceptor);

    [Fact]
    public async Task ASiblingClaimedElsewhereBetweenTheCheckAndTheClaim_MakesThisReplicaGiveItsClaimBack()
    {
        var thread = Guid.NewGuid();
        var first = Seed(thread, priority: 10);
        var sibling = Seed(thread, priority: 0);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        // The other replica claims the sibling right before this replica's claiming update executes
        _interceptor.BeforeNextUpdate = async () =>
        {
            using var otherReplica = ServiceProvider.CreateScope();
            await otherReplica.ServiceProvider.GetRequiredService<CliQueueDbContext>().Set<CliRun>()
                .Where(r => r.Id == sibling.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, CliRunStatus.Dispatched)
                    .SetProperty(r => r.ClaimedByHostId, "OTHER-HOST")
                    .SetProperty(r => r.DispatchedAt, DateTime.UtcNow));
        };

        var claimed = await CreateProcessor().TryClaimAsync(CancellationToken.None);

        claimed.ShouldBeNull("the thread already has a run in progress on the other replica");
        var runs = await DbContext.Set<CliRun>().AsNoTracking().ToListAsync();
        runs.Count(r => r.Status is CliRunStatus.Dispatched or CliRunStatus.Running).ShouldBe(1);

        var givenBack = runs.Single(r => r.Id == first.Id);
        givenBack.Status.ShouldBe(CliRunStatus.Queued);
        givenBack.ClaimedByHostId.ShouldBeNull();
        givenBack.LeaseExpiresAt.ShouldBeNull();
        givenBack.DispatchedAt.ShouldBeNull();
    }

    [Fact]
    public async Task WithoutARace_TheClaimStands()
    {
        // Control: the post-claim check does not give back a claim that had no competitor
        var thread = Guid.NewGuid();
        var only = Seed(thread, priority: 0);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var claimed = await CreateProcessor().TryClaimAsync(CancellationToken.None);

        claimed.ShouldBe(only.Id);
        (await DbContext.Set<CliRun>().AsNoTracking().SingleAsync(r => r.Id == only.Id))
            .Status.ShouldBe(CliRunStatus.Dispatched);
    }

    private CliRun Seed(Guid threadId, int priority)
    {
        var run = new CliRun
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            ThreadId = threadId,
            Status = CliRunStatus.Queued,
            Priority = priority,
            Prompt = "turn"
        };
        DbContext.Set<CliRun>().Add(run);
        return run;
    }

    private CliRunQueueProcessor CreateProcessor()
    {
        var options = new Mock<IOptionsMonitor<CliAgentOptions>>();
        options.Setup(m => m.CurrentValue).Returns(new CliAgentOptions { Enabled = true });

        return new CliRunQueueProcessor(
            ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            new CliRunSignalHub(),
            new CliRunCancellationRegistry(),
            options.Object,
            NullLogger<CliRunQueueProcessor>.Instance);
    }

    /// <summary>Runs a one-shot hook right before the next UPDATE statement is sent to the database.</summary>
    private sealed class BeforeNextUpdateInterceptor : DbCommandInterceptor
    {
        public Func<Task>? BeforeNextUpdate { get; set; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (BeforeNextUpdate is { } hook
                && command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                BeforeNextUpdate = null;
                await hook();
            }

            return result;
        }
    }
}
