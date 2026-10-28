using Microsoft.EntityFrameworkCore;
using Tnzi.Signing.Dtos;
using Tnzi.Signing.Entities;

namespace Tnzi.Signing.Tests;

/// <summary>
/// 只注册了 <see cref="IDocumentHostSink"/>、没有 <see cref="IMergeSourceProvider"/> 的宿主类型：绑定到它的某条记录要被拒绝。
/// </summary>
/// <remarks>
/// 绑定唯一的授权点是同类型 provider 的 <see cref="IMergeSourceProvider.ResolveAsync"/>。缺了它，发起人点名哪条记录就绑哪条，
/// 完成时 sink 照样把这份自己签完的 PDF 挂进那条记录：契约要求两者成对注册，这里守的是「没有成对时失败关闭」。
/// </remarks>
public class EnvelopeHostSinkWithoutProviderTests : SigningStorageTestBase
{
    private const string SinkOnlyType = "Matter";

    private readonly RecordingSink _sink = new();

    protected override IEnumerable<IDocumentHostSink> HostSinks => [_sink];

    private sealed class RecordingSink : IDocumentHostSink
    {
        public string EntityType => SinkOnlyType;

        public List<Guid> Attached { get; } = [];

        public Task AttachAsync(Guid entityId, Guid fileId, string fileName, Guid requestId, CancellationToken cancellationToken = default)
        {
            Attached.Add(entityId);
            return Task.CompletedTask;
        }
    }

    private async Task<Tnzi.Results.Result<EnvelopeDto>> CreateAsync(string? hostType, Guid? hostId)
    {
        var (templateId, _) = await ArrangeTemplateAsync();
        using var create = BeginRequest();
        return await create.Envelopes.CreateAsync(new CreateEnvelopeDto
        {
            TemplateId = templateId,
            Title = "Engagement Letter",
            HostEntityType = hostType,
            HostEntityId = hostId,
            Recipients = SingleClient(),
        });
    }

    [Fact]
    public async Task Binding_a_record_of_a_host_type_that_has_a_sink_but_no_provider_is_refused()
    {
        var result = await CreateAsync(SinkOnlyType, Guid.NewGuid());

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(500);
        (await DbContext.Set<Envelope>().AsNoTracking().AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task The_host_type_is_matched_case_insensitively_like_the_registry()
    {
        var result = await CreateAsync("matter", Guid.NewGuid());

        result.Succeeded.ShouldBeFalse();
        (await DbContext.Set<Envelope>().AsNoTracking().AnyAsync()).ShouldBeFalse();
    }

    /// <summary>对照：不点名记录就没有可归档的地方，也就没有要授权的绑定。</summary>
    [Fact]
    public async Task Naming_the_type_without_a_record_is_still_allowed()
    {
        var result = await CreateAsync(SinkOnlyType, null);

        result.Succeeded.ShouldBeTrue(result.Message);
    }

    /// <summary>对照：既没有 sink 也没有 provider 的类型，绑定只是一个标签，完成时无处归档。</summary>
    [Fact]
    public async Task A_host_type_with_neither_sink_nor_provider_can_still_be_bound()
    {
        var result = await CreateAsync("Unwired", Guid.NewGuid());

        result.Succeeded.ShouldBeTrue(result.Message);
    }
}
