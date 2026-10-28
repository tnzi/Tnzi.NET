using Microsoft.EntityFrameworkCore;
using Tnzi.Exceptions;
using Tnzi.Signing.Dtos;
using Tnzi.Signing.Entities;
using Tnzi.Signing.Metadata;

namespace Tnzi.Signing.Tests;

/// <summary>
/// 宿主绑定的授权点是 <see cref="IMergeSourceProvider.ResolveAsync"/>：它在发起人的请求里、任何落库之前执行，
/// provider 抛 <see cref="ForbiddenException"/> 就什么都建不起来。
/// </summary>
/// <remarks>
/// 签署模块只要求 <c>signing.request.create</c>，不认识宿主记录，无从判断发起人能不能看那一条；
/// 契约因此把授权交给拥有那类记录的 provider（见它与 <see cref="IDocumentHostSink"/> 的文档）。
/// 这条约定只有在「解析发生在任何副作用之前」时才成立：若哪天解析挪到了建信封之后，
/// 抛出的 403 会留下一个绑在那条记录上的半成品，完成时照样归档进去。这里守的就是这个顺序。
/// </remarks>
public class EnvelopeHostBindingAuthorizationTests : SigningStorageTestBase
{
    private const string HostType = "Matter";
    private static readonly Guid Restricted = Guid.NewGuid();
    private static readonly Guid Allowed = Guid.NewGuid();

    /// <summary>按发起人的访问权判定：受限的那一条抛 403，其余照常解析。</summary>
    private sealed class AuthorizingMergeSource : IMergeSourceProvider
    {
        public List<Guid> Resolved { get; } = [];

        public string EntityType => HostType;

        public IReadOnlyList<MergeFieldDescriptor> Describe() => [new("ClientName", "Client name")];

        public Task<IReadOnlyDictionary<string, object?>> ResolveAsync(Guid entityId, CancellationToken cancellationToken = default)
        {
            if (entityId == Restricted)
            {
                throw new ForbiddenException("You do not have access to this record.");
            }

            Resolved.Add(entityId);
            IReadOnlyDictionary<string, object?> values = new Dictionary<string, object?> { ["ClientName"] = "Acme" };
            return Task.FromResult(values);
        }
    }

    private readonly AuthorizingMergeSource _source = new();

    protected override IEnumerable<IMergeSourceProvider> MergeProviders => [_source];

    private static Field SenderField() => new()
    {
        Key = "ClientName",
        Label = "Client name",
        Type = SigningFieldType.Text,
        RecipientRole = null,
        Binding = "ClientName",
        Required = false,
        PlacementMode = FieldPlacementMode.Absolute,
        Page = 1,
        X = 0.1m,
        Y = 0.1m,
        W = 0.5m,
        H = 0.04m,
    };

    private async Task<Func<Task<Tnzi.Results.Result<EnvelopeDto>>>> CreateFor(Guid hostId)
    {
        var (templateId, _) = await ArrangeTemplateAsync([SenderField()]);
        return async () =>
        {
            using var create = BeginRequest();
            return await create.Envelopes.CreateAsync(new CreateEnvelopeDto
            {
                TemplateId = templateId,
                Title = "Engagement Letter",
                HostEntityType = HostType,
                HostEntityId = hostId,
                Recipients = SingleClient(),
            });
        };
    }

    [Fact]
    public async Task ARecordTheProviderRefuses_LeavesNoEnvelopeBoundToIt()
    {
        var create = await CreateFor(Restricted);

        await Should.ThrowAsync<ForbiddenException>(create);

        (await DbContext.Set<Envelope>().AsNoTracking().AnyAsync(e => e.HostEntityId == Restricted)).ShouldBeFalse();
        (await DbContext.Set<FieldValue>().AsNoTracking().AnyAsync()).ShouldBeFalse();
    }

    /// <summary>对照：可访问的记录照常建起来，并且确实经过了 provider。</summary>
    [Fact]
    public async Task ARecordTheProviderAllows_IsBoundAndMerged()
    {
        var create = await CreateFor(Allowed);

        var result = await create();

        result.Succeeded.ShouldBeTrue(result.Message);
        _source.Resolved.ShouldContain(Allowed);
        (await DbContext.Set<Envelope>().AsNoTracking().SingleAsync()).HostEntityId.ShouldBe(Allowed);
    }
}
