namespace Tnzi.Identity.Organization.Tests.Integration;

using Tnzi.Identity.Organization.Entities;
using Tnzi.Identity.Organization.Events;

/// <summary>
/// <see cref="OrganizationUpdatedEvent.UpdatedFields"/> 说的是这次真正改了什么，单条与批量同一个答案。真实 SQLite。
/// </summary>
/// <remarks>
/// ★ 批量更新先把 DTO 映射进被跟踪的实体，再拿 DTO 去比这个已经被改过的实体：每一项都相等，
/// 事件里的字段列表恒为空。订阅它把改名同步到外部目录的消费方，对批量改名永远看不到 <c>Name</c>。
/// </remarks>
public class OrganizationUpdateEventTests : OrganizationIntegrationTestBase
{
    private readonly Mock<IEventBus> _eventBus = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddSingleton(_eventBus.Object);
    }

    private List<OrganizationUpdatedEvent> PublishedUpdates()
        => _eventBus.Invocations
            .Where(i => i.Method.Name == nameof(IEventBus.PublishAsync))
            .Select(i => i.Arguments[0])
            .OfType<OrganizationUpdatedEvent>()
            .ToList();

    private async Task<Organization> SeedAsync(string name, string code)
    {
        var id = Guid.NewGuid();
        var org = new Organization
        {
            Id = id, Name = name, Code = code, Remark = "r", SortOrder = 1, IsEnabled = true,
            Path = $"/{id}/", Level = 1, CreationTime = DateTime.UtcNow,
        };
        DbContext.Set<Organization>().Add(org);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return org;
    }

    [Fact]
    public async Task A_batch_rename_reports_the_renamed_field()
    {
        var sales = await SeedAsync("Sales", "S");
        var support = await SeedAsync("Support", "SP");

        var result = await Service.UpdateManyAsync(
        [
            (sales.Id, new UpdateOrganizationDto { Name = "Sales & Marketing", Code = "S", Remark = "r", SortOrder = 1, IsEnabled = true }),
            (support.Id, new UpdateOrganizationDto { Name = "Support", Code = "SP", Remark = "r", SortOrder = 5, IsEnabled = false }),
        ]);

        result.Succeeded.ShouldBeTrue(result.Message);
        var events = PublishedUpdates().ToDictionary(e => e.OrganizationId);
        events[sales.Id].UpdatedFields.ShouldBe([nameof(Organization.Name)]);
        events[support.Id].UpdatedFields.ShouldBe([nameof(Organization.SortOrder), nameof(Organization.IsEnabled)], ignoreOrder: true);
    }

    [Fact]
    public async Task A_batch_update_reports_the_same_fields_as_a_single_update()
    {
        var a = await SeedAsync("A", "CODE-A");
        var b = await SeedAsync("B", "CODE-B");
        var change = new UpdateOrganizationDto { Name = "A2", Code = "code-a", Remark = "changed", SortOrder = 1, IsEnabled = true };

        (await Service.UpdateAsync(a.Id, change)).Succeeded.ShouldBeTrue();
        var single = PublishedUpdates().Single().UpdatedFields;
        _eventBus.Invocations.Clear();

        var batchChange = new UpdateOrganizationDto { Name = "B2", Code = "code-b", Remark = "changed", SortOrder = 1, IsEnabled = true };
        (await Service.UpdateManyAsync([(b.Id, batchChange)])).Succeeded.ShouldBeTrue();
        var batch = PublishedUpdates().Single().UpdatedFields;

        // 代码只差大小写不算改动（与单条更新、与唯一性判定同一口径）。
        batch.ShouldBe(single, ignoreOrder: true);
        batch.ShouldBe([nameof(Organization.Name), nameof(Organization.Remark)], ignoreOrder: true);
    }
}
