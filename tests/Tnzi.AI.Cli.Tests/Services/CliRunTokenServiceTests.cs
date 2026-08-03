namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 运行范围回写凭据。
/// </summary>
public class CliRunTokenServiceTests : IntegratedTestBase<CliQueueDbContext>
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<CliRun, Guid>, EFCoreRepository<CliQueueDbContext, CliRun, Guid>>();
        services.AddScoped<CliRunTokenService>();
    }

    private async Task<CliRun> SeedRunAsync(CliRunStatus status)
    {
        var run = new CliRun
        {
            AgentId = Guid.NewGuid(),
            CliRuntimeId = Guid.NewGuid(),
            Status = status,
            Prompt = "work"
        };

        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();
        return run;
    }

    [Fact]
    public async Task Issue_StoresOnlyAHashNeverThePlaintext()
    {
        // 数据库泄漏不该等于「拿到一把还能用的钥匙」。
        var run = await SeedRunAsync(CliRunStatus.Running);
        var service = ServiceProvider.GetRequiredService<CliRunTokenService>();

        var token = await service.IssueAsync(run, TimeSpan.FromHours(1), CancellationToken.None);

        token.ShouldStartWith(CliRunTokenService.TokenPrefix);

        var stored = await DbContext.Set<CliRun>().AsNoTracking().SingleAsync(r => r.Id == run.Id);
        stored.WriteBackTokenHash.ShouldNotBeNull();
        stored.WriteBackTokenHash!.ShouldNotContain(token);
        stored.WriteBackTokenExpiresAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Validate_AcceptsALiveTokenAndReportsTheAgentItIsScopedTo()
    {
        var run = await SeedRunAsync(CliRunStatus.Running);
        var service = ServiceProvider.GetRequiredService<CliRunTokenService>();
        var token = await service.IssueAsync(run, TimeSpan.FromHours(1), CancellationToken.None);

        var credential = await service.ValidateAsync(token);

        credential.ShouldNotBeNull();
        credential!.RunId.ShouldBe(run.Id);
        // 权限上限由 Agent 决定，而不是由派发这次运行的那个人决定。
        credential.AgentId.ShouldBe(run.AgentId);
    }

    [Fact]
    public async Task Validate_RejectsATokenWhoseRunHasFinished()
    {
        // 运行结束后凭据必须立刻失效，哪怕名义上还没到期 ——
        // 否则一个已经交付完的 agent 仍握着一把能调平台的钥匙。
        var run = await SeedRunAsync(CliRunStatus.Running);
        var service = ServiceProvider.GetRequiredService<CliRunTokenService>();
        var token = await service.IssueAsync(run, TimeSpan.FromHours(1), CancellationToken.None);

        await DbContext.Set<CliRun>()
            .Where(r => r.Id == run.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, CliRunStatus.Completed));

        (await service.ValidateAsync(token)).ShouldBeNull();
    }

    [Fact]
    public async Task Validate_RejectsAnExpiredToken()
    {
        var run = await SeedRunAsync(CliRunStatus.Running);
        var service = ServiceProvider.GetRequiredService<CliRunTokenService>();
        var token = await service.IssueAsync(run, TimeSpan.FromMilliseconds(-1), CancellationToken.None);

        (await service.ValidateAsync(token)).ShouldBeNull();
    }

    [Fact]
    public async Task Validate_RejectsGarbageWithoutTouchingTheDatabase()
    {
        var service = ServiceProvider.GetRequiredService<CliRunTokenService>();

        (await service.ValidateAsync("not-a-tnzi-token")).ShouldBeNull();
        (await service.ValidateAsync("")).ShouldBeNull();
    }

    [Fact]
    public async Task Revoke_ClearsTheStoredHash()
    {
        var run = await SeedRunAsync(CliRunStatus.Running);
        var service = ServiceProvider.GetRequiredService<CliRunTokenService>();
        var token = await service.IssueAsync(run, TimeSpan.FromHours(1), CancellationToken.None);

        await service.RevokeAsync(run.Id, CancellationToken.None);

        (await service.ValidateAsync(token)).ShouldBeNull();
        var stored = await DbContext.Set<CliRun>().AsNoTracking().SingleAsync(r => r.Id == run.Id);
        stored.WriteBackTokenHash.ShouldBeNull();
    }
}
