using Microsoft.EntityFrameworkCore;
using Tnzi.Results;
using Tnzi.Signing.Dtos;
using Tnzi.Signing.Entities;
using Tnzi.Signing.Metadata;

namespace Tnzi.Signing.Tests;

/// <summary>
/// 发出前的必填校验：发起方负责的字段（<c>RecipientRole</c> 为空，值来自合并变量或预填）缺值不能发。
/// </summary>
/// <remarks>
/// <para>
/// <c>IMergeSourceProvider.ResolveAsync</c> 的契约说「解析不出的键要省略，好让调用方拦下一份不完整的合并」，
/// <c>CreateAsync</c> 也照此不写值。但那道「拦」此前全仓不存在：唯一的必填强制点在 <c>SubmitAsync</c>，
/// 只作用于收件人自己角色的字段，而发起方字段的角色是 null，永远不在任何人的 mine 里 ——
/// 于是宿主记录缺地址的合同照常发出、照常签完，密封时该处留白并被算进哈希与完成证书。
/// </para>
/// </remarks>
public class EnvelopeSendRequiredFieldTests : SigningStorageTestBase
{
    private const string HostType = "Matter";
    private static readonly Guid HostWithAddress = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid HostWithoutAddress = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    /// <summary>一个宿主有地址、一个没有 —— 没有的那个按契约省略键。</summary>
    private sealed class MatterMergeSource : IMergeSourceProvider
    {
        public string EntityType => HostType;

        public IReadOnlyList<MergeFieldDescriptor> Describe() => [new("ClientAddress", "Client address")];

        public Task<IReadOnlyDictionary<string, object?>> ResolveAsync(Guid entityId, CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<string, object?> values = entityId == HostWithAddress
                ? new Dictionary<string, object?> { ["ClientAddress"] = "1 Main St" }
                : new Dictionary<string, object?>();
            return Task.FromResult(values);
        }
    }

    protected override IEnumerable<IMergeSourceProvider> MergeProviders => [new MatterMergeSource()];

    private static Field RequiredSenderField(string key = "Addr", string label = "Client address", string? binding = "ClientAddress") => new()
    {
        Key = key,
        Label = label,
        Type = SigningFieldType.Text,
        RecipientRole = null,
        Binding = binding,
        Required = true,
        PlacementMode = FieldPlacementMode.Absolute,
        Page = 1,
        X = 0.1m,
        Y = 0.1m,
        W = 0.5m,
        H = 0.04m,
    };

    private async Task<Guid> CreateDraftAsync(IReadOnlyList<Field> fields, Guid? hostId, Dictionary<string, string?>? prefilled = null)
    {
        var (templateId, _) = await ArrangeTemplateAsync(fields);
        using var create = BeginRequest();
        var created = await create.Envelopes.CreateAsync(new CreateEnvelopeDto
        {
            TemplateId = templateId,
            Title = "Engagement Letter",
            HostEntityType = hostId is null ? null : HostType,
            HostEntityId = hostId,
            Recipients = SingleClient(),
            PrefilledValues = prefilled,
        });
        created.Succeeded.ShouldBeTrue(created.Message);
        return created.Data!.Id;
    }

    private async Task<Result<IReadOnlyList<IssuedSigningLink>>> SendAsync(Guid requestId)
    {
        using var send = BeginRequest();
        return await send.Envelopes.SendAsync(requestId);
    }

    [Fact]
    public async Task Send_refuses_when_a_required_sender_filled_field_has_no_value()
    {
        var requestId = await CreateDraftAsync([RequiredSenderField()], HostWithoutAddress);

        var sent = await SendAsync(requestId);

        sent.Succeeded.ShouldBeFalse();
        sent.Code.ShouldBe(409);
        sent.Message!.ShouldContain("Client address");

        // 失败方向关闭：仍是草稿，没有签发任何链接。
        var envelope = await DbContext.Set<Envelope>().AsNoTracking().SingleAsync(e => e.Id == requestId);
        envelope.Status.ShouldBe(EnvelopeStatus.Draft);
        (await DbContext.Set<Signer>().AsNoTracking().SingleAsync(s => s.RequestId == requestId)).TokenHash.ShouldBeNull();
    }

    [Fact]
    public async Task Send_names_every_missing_required_field()
    {
        var requestId = await CreateDraftAsync(
            [RequiredSenderField("Addr", "Client address"), RequiredSenderField("Ref", "File number", binding: null)],
            HostWithoutAddress);

        var sent = await SendAsync(requestId);

        sent.Succeeded.ShouldBeFalse();
        sent.Message!.ShouldContain("Client address");
        sent.Message!.ShouldContain("File number");
    }

    [Fact]
    public async Task Send_passes_when_the_provider_resolves_the_binding()
    {
        var requestId = await CreateDraftAsync([RequiredSenderField()], HostWithAddress);

        var sent = await SendAsync(requestId);

        sent.Succeeded.ShouldBeTrue(sent.Message);
    }

    [Fact]
    public async Task Send_passes_when_the_value_comes_from_prefilled_values()
    {
        var requestId = await CreateDraftAsync(
            [RequiredSenderField()], HostWithoutAddress,
            prefilled: new Dictionary<string, string?> { ["Addr"] = "2 Side St" });

        var sent = await SendAsync(requestId);

        sent.Succeeded.ShouldBeTrue(sent.Message);
    }

    /// <summary>空白算缺：一个只有空格的预填值与没有值是同一件事。</summary>
    [Fact]
    public async Task Send_treats_a_blank_prefilled_value_as_missing()
    {
        var requestId = await CreateDraftAsync(
            [RequiredSenderField()], HostWithoutAddress,
            prefilled: new Dictionary<string, string?> { ["Addr"] = "   " });

        var sent = await SendAsync(requestId);

        sent.Succeeded.ShouldBeFalse();
        sent.Code.ShouldBe(409);
    }

    /// <summary>收件人负责的必填字段在提交时校验，不在发出时 —— 那时它本来就还没有值。</summary>
    [Fact]
    public async Task Send_does_not_demand_values_for_recipient_fields()
    {
        var recipientField = RequiredSenderField("Notes", "Notes", binding: null);
        recipientField.RecipientRole = "Client";
        var requestId = await CreateDraftAsync([recipientField], hostId: null);

        var sent = await SendAsync(requestId);

        sent.Succeeded.ShouldBeTrue(sent.Message);
    }

    /// <summary>非必填的发起方字段缺值不拦：留白是它自己的选择。</summary>
    [Fact]
    public async Task Send_passes_when_an_optional_sender_field_has_no_value()
    {
        var optional = RequiredSenderField();
        optional.Required = false;
        var requestId = await CreateDraftAsync([optional], HostWithoutAddress);

        var sent = await SendAsync(requestId);

        sent.Succeeded.ShouldBeTrue(sent.Message);
    }
}
