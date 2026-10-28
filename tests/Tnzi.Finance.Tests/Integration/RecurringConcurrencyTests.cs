namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 暂停 / 恢复 / 结束 / 删除周期模板 × 另一个写者（后台扫描推进排期、另一个管理员编辑）先改了同一个模板。
/// </summary>
/// <remarks>
/// <c>RecurringDocument</c> 带并发戳，过期的写会被挡住；挡住的形状必须是 409（重新加载再试），
/// 而不是一个没人接的异常变成的 500。用仓储钩子把另一个写者精确地插进「读」与「写」之间。
/// </remarks>
public class RecurringConcurrencyTests : FinanceIntegrationTestBase
{
    private readonly WriteHook _hook = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddSingleton(_hook);
        services.AddScoped<IRepository<RecurringDocument, Guid>>(sp =>
            new HookedRecurringRepository(sp.GetRequiredService<FinanceTestDbContext>(), sp, _hook));
        services.AddScoped<IReadOnlyRepository<RecurringDocument, Guid>>(sp => sp.GetRequiredService<IRepository<RecurringDocument, Guid>>());
    }

    private async Task<Guid> TemplateAsync()
    {
        await SeedCoaAsync();
        var customer = await InScopeAsync<ICustomerService, Result<CustomerDto>>(
            s => s.CreateAsync(new CreateCustomerDto { Name = "Subscription Co", Currency = "USD" }));
        customer.Succeeded.ShouldBeTrue(customer.Message);
        var revenue = await AccountIdByCodeAsync("4100");

        var created = await InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(s => s.CreateAsync(new CreateRecurringDocumentDto
        {
            Name = "Monthly retainer",
            Kind = RecurringDocKind.Invoice,
            PartyId = customer.Data!.Id,
            Currency = "USD",
            Frequency = RecurrenceFrequency.Monthly,
            Interval = 1,
            StartDate = new DateTime(2026, 1, 1),
            DueDays = 30,
            Lines = [new CreateRecurringLineDto { AccountId = revenue, Quantity = 1, UnitPrice = 500m }],
        }));
        created.Succeeded.ShouldBeTrue(created.Message);
        return created.Data!.Id;
    }

    /// <summary>另一个写者：在独立作用域里改名并提交（并发戳随之轮换）。</summary>
    private async Task RenameInAnotherScopeAsync(Guid id, string name)
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<RecurringDocument, Guid>>();
        var entity = (await repo.GetAsync(id))!;
        entity.Name = name;
        await repo.UpdateAsync(entity);
        await repo.SaveChangesAsync();
    }

    [Fact]
    public async Task Pause_AfterAConcurrentWriteToTheSameTemplate_Returns409()
    {
        var id = await TemplateAsync();
        _hook.BeforeFirstWrite = () => RenameInAnotherScopeAsync(id, "Renamed meanwhile");

        var paused = await InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(s => s.PauseAsync(id));

        _hook.BeforeFirstWrite.ShouldBeNull("the hook must have run the concurrent write");
        paused.Succeeded.ShouldBeFalse();
        paused.Code.ShouldBe(409);
        var reloaded = (await ReloadAsync<RecurringDocument>(id))!;
        reloaded.Status.ShouldBe(RecurringStatus.Active);
        reloaded.Name.ShouldBe("Renamed meanwhile");
    }

    [Fact]
    public async Task Delete_AfterAConcurrentWriteToTheSameTemplate_Returns409()
    {
        var id = await TemplateAsync();
        _hook.BeforeFirstWrite = () => RenameInAnotherScopeAsync(id, "Renamed meanwhile");

        var deleted = await InScopeAsync<IRecurringDocumentService, Result>(s => s.DeleteAsync(id));

        _hook.BeforeFirstWrite.ShouldBeNull("the hook must have run the concurrent write");
        deleted.Succeeded.ShouldBeFalse();
        deleted.Code.ShouldBe(409);
        (await ReloadAsync<RecurringDocument>(id)).ShouldNotBeNull();
    }

    /// <summary>对照：没有并发写时，暂停、恢复、结束依次成功（轮换过的戳不会让下一次误报冲突）。</summary>
    [Fact]
    public async Task Transitions_WithoutAConcurrentWrite_StillSucceed()
    {
        var id = await TemplateAsync();

        var paused = await InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(s => s.PauseAsync(id));
        paused.Succeeded.ShouldBeTrue(paused.Message);
        var resumed = await InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(s => s.ResumeAsync(id));
        resumed.Succeeded.ShouldBeTrue(resumed.Message);
        var ended = await InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(s => s.EndAsync(id));
        ended.Succeeded.ShouldBeTrue(ended.Message);
        ended.Data!.Status.ShouldBe(RecurringStatus.Ended);
    }

    private sealed class WriteHook
    {
        public Func<Task>? BeforeFirstWrite { get; set; }

        public async Task RunOnceAsync()
        {
            var action = BeforeFirstWrite;
            BeforeFirstWrite = null;
            if (action != null)
                await action();
        }
    }

    private sealed class HookedRecurringRepository(FinanceTestDbContext dbContext, IServiceProvider sp, WriteHook hook)
        : EFCoreRepository<FinanceTestDbContext, RecurringDocument, Guid>(dbContext, serviceProvider: sp)
    {
        public override async Task UpdateAsync(RecurringDocument entity, CancellationToken cancellationToken = default)
        {
            await hook.RunOnceAsync();
            await base.UpdateAsync(entity, cancellationToken);
        }

        public override async Task DeleteAsync(RecurringDocument entity, CancellationToken cancellationToken = default)
        {
            await hook.RunOnceAsync();
            await base.DeleteAsync(entity, cancellationToken);
        }
    }
}
