using Microsoft.EntityFrameworkCore;
using Tnzi.Documents.Models;
using Tnzi.Signing.Dtos;
using Tnzi.Signing.Entities;
using Tnzi.Signing.Metadata;

namespace Tnzi.Signing.Tests;

/// <summary>
/// Composed 模板发起时，宿主记录的合并变量只解析一次，正文 <c>{{var}}</c> 与绑定字段用同一份、同一种（不分大小写的）匹配。
/// </summary>
/// <remarks>
/// 此前同一次发起调两次 provider：绑定字段按 provider 字典自己的比较器（文档示例是区分大小写的）查，
/// 正文按不分大小写查。正文写 <c>{{clientName}}</c>、字段绑定 <c>clientName</c>、provider 给 <c>ClientName</c> 时，
/// 正文渲染出了值，绑定字段却是空的：发出时被必填校验拦下，或非必填时留白密封。每次发起也因此读两遍宿主记录，
/// 两次之间记录变了，两处还会各取一份。
/// </remarks>
public class EnvelopeMergeResolutionTests : SigningStorageTestBase
{
    private const string HostType = "Matter";

    private sealed class CountingMergeSource : IMergeSourceProvider
    {
        public int Calls { get; private set; }

        public string EntityType => HostType;

        public IReadOnlyList<MergeFieldDescriptor> Describe() => [new("ClientName", "Client name")];

        public Task<IReadOnlyDictionary<string, object?>> ResolveAsync(Guid entityId, CancellationToken cancellationToken = default)
        {
            Calls++;
            // 与文档示例同形：区分大小写的普通字典。
            IReadOnlyDictionary<string, object?> values = new Dictionary<string, object?> { ["ClientName"] = "Alice Corp" };
            return Task.FromResult(values);
        }
    }

    private readonly CountingMergeSource _source = new();

    protected override IEnumerable<IMergeSourceProvider> MergeProviders => [_source];

    private async Task<Guid> ArrangeComposedTemplateAsync()
    {
        var template = new EnvelopeTemplate
        {
            Name = "Engagement Letter",
            Category = "General",
            Source = TemplateSource.Composed,
            BodyTemplate = "Dear {{clientName}},",
            HostEntityTypes = HostType,
            IsActive = true,
        };
        DbContext.Set<EnvelopeTemplate>().Add(template);
        await DbContext.SaveChangesAsync();

        DbContext.Set<Field>().Add(new Field
        {
            TemplateId = template.Id,
            Key = "client",
            Label = "Client",
            Type = SigningFieldType.Text,
            RecipientRole = null,
            Binding = "clientName",
            Required = true,
            PlacementMode = FieldPlacementMode.Absolute,
            Page = 1,
            X = 0.1m,
            Y = 0.8m,
            W = 0.4m,
            H = 0.04m,
        });
        await DbContext.SaveChangesAsync();
        return template.Id;
    }

    [Fact]
    public async Task Body_token_and_bound_field_resolve_from_one_lookup_with_the_same_matching()
    {
        var templateId = await ArrangeComposedTemplateAsync();

        using var create = BeginRequest();
        var created = await create.Envelopes.CreateAsync(new CreateEnvelopeDto
        {
            TemplateId = templateId,
            Title = "Engagement Letter",
            HostEntityType = HostType,
            HostEntityId = Guid.NewGuid(),
            Recipients = SingleClient(),
        });

        created.Succeeded.ShouldBeTrue(created.Message);
        _source.Calls.ShouldBe(1);

        // 正文按不分大小写取到了值……
        Stamper.Creates.ShouldHaveSingleItem().Stamps.OfType<PdfTextStamp>()
            .ShouldContain(s => s.Text.Contains("Dear Alice Corp"));

        // ……绑定字段也一样，于是发出时的必填校验不会把它当作缺值。
        var value = await DbContext.Set<FieldValue>().AsNoTracking().SingleAsync(v => v.RequestId == created.Data!.Id);
        value.FieldKey.ShouldBe("client");
        value.Value.ShouldBe("Alice Corp");
    }
}
