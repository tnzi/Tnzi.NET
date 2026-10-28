using Microsoft.EntityFrameworkCore;
using Mapster;
using MapsterMapper;
using Tnzi.Mapster;
using Tnzi.Results;
using Tnzi.Signing.Dtos;
using Tnzi.Signing.Entities;
using Tnzi.Signing.Services.Internal;

namespace Tnzi.Signing.Tests;

/// <summary>
/// 模板的「可用于哪些宿主类型」（逗号分隔，空 = 不限）：按整项、不分大小写比较，发起请求时真的生效。
/// </summary>
/// <remarks>
/// 此前列表筛选对整串做子串匹配（按 <c>Matter</c> 筛会带出只限 <c>MatterNote</c> 的模板），
/// 发起请求时则根本不看这份清单：一份只限 <c>Matter</c> 的模板绑到 <c>Employee</c> 上，
/// 合并变量去另一个 provider 那里解析、全都解析不出，必填的在发出时被拦下，不必填的留白密封。
/// </remarks>
public class EnvelopeTemplateHostTypeTests : SigningStorageTestBase
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData(" Matter , ,client,MATTER ", "Matter,client")]
    [InlineData("Employee", "Employee")]
    public void The_list_is_stored_normalized(string? input, string? expected)
    {
        HostEntityTypeList.Normalize(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null, "Employee", true)]
    [InlineData(null, null, true)]
    [InlineData("Matter", "matter", true)]
    [InlineData("Client, Matter", " MATTER ", true)]
    [InlineData("Matter", "MatterNote", false)]
    [InlineData("MatterNote", "Matter", false)]
    [InlineData("Matter", "Employee", false)]
    [InlineData("Matter", null, false)]
    [InlineData("Matter", "  ", false)]
    public void A_restricted_list_allows_only_its_own_items(string? list, string? hostType, bool allowed)
    {
        HostEntityTypeList.Allows(list, hostType).ShouldBe(allowed);
    }

    private async Task<Guid> TemplateRestrictedToAsync(string? hostEntityTypes, string name = "Engagement Letter")
    {
        var (templateId, _) = await ArrangeTemplateAsync();
        var template = await DbContext.Set<EnvelopeTemplate>().SingleAsync(t => t.Id == templateId);
        template.Name = name;
        template.HostEntityTypes = hostEntityTypes;
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return templateId;
    }

    /// <summary>筛选按整项匹配；不限的模板对每种宿主都可用，留在结果里；规范化之前存下的带空格的行照样认得。</summary>
    [Fact]
    public async Task Filtering_by_host_type_matches_whole_items_case_insensitively()
    {
        using var mapping = MapperExtensions.PushMapper(new Mapper(new TypeAdapterConfig()));

        var matter = await TemplateRestrictedToAsync("Matter", "A");
        var matterNote = await TemplateRestrictedToAsync("MatterNote", "B");
        var legacySpaced = await TemplateRestrictedToAsync("Client, Matter", "C");
        var unrestricted = await TemplateRestrictedToAsync(null, "D");

        using var request = BeginRequest();
        var result = await request.Templates.GetPagedAsync(new EnvelopeTemplateQueryDto { HostEntityType = "matter" });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Items.Select(t => t.Id).ShouldBe([matter, legacySpaced, unrestricted], ignoreOrder: true);
        result.Data.Items.ShouldNotContain(t => t.Id == matterNote);
    }

    private async Task<Result<EnvelopeDto>> CreateAsync(Guid templateId, string? hostEntityType)
    {
        using var create = BeginRequest();
        return await create.Envelopes.CreateAsync(new CreateEnvelopeDto
        {
            TemplateId = templateId,
            Title = "Engagement Letter",
            Recipients = SingleClient(),
            HostEntityType = hostEntityType,
            HostEntityId = hostEntityType is null ? null : Guid.NewGuid(),
        });
    }

    [Theory]
    [InlineData("Employee")]
    [InlineData("MatterNote")]
    [InlineData(null)]
    public async Task A_template_restricted_to_other_host_types_cannot_start_a_request(string? hostEntityType)
    {
        var templateId = await TemplateRestrictedToAsync("Matter,Client");

        var result = await CreateAsync(templateId, hostEntityType);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("Matter, Client");
        (await DbContext.Set<Envelope>().AsNoTracking().AnyAsync()).ShouldBeFalse();
    }

    [Theory]
    [InlineData("matter")]
    [InlineData("Client")]
    public async Task A_template_restricted_to_a_host_type_starts_requests_for_it(string hostEntityType)
    {
        var templateId = await TemplateRestrictedToAsync("Matter,Client");

        var result = await CreateAsync(templateId, hostEntityType);

        result.Succeeded.ShouldBeTrue(result.Message);
    }

    [Theory]
    [InlineData("Employee")]
    [InlineData(null)]
    public async Task An_unrestricted_template_starts_requests_for_any_host_or_none(string? hostEntityType)
    {
        var templateId = await TemplateRestrictedToAsync(null);

        var result = await CreateAsync(templateId, hostEntityType);

        result.Succeeded.ShouldBeTrue(result.Message);
    }
}
