using Microsoft.EntityFrameworkCore;
using Tnzi.Results;
using Tnzi.Signing.Dtos;
using Tnzi.Signing.Entities;
using Tnzi.Signing.Metadata;

namespace Tnzi.Signing.Tests;

/// <summary>
/// 发起方那侧写进 <c>FieldValue.Value</c> 的值（合并变量、预填）也受 <see cref="SigningLimits.MaxFieldValueLength"/> 约束。
/// </summary>
/// <remarks>
/// <para>
/// 列上限（<c>HasMaxLength</c>）是为匿名提交加的，但列不认来源：<c>CreateAsync</c> 经 <c>IMergeSourceProvider</c>
/// 与 <c>PrefilledValues</c> 写同一列，此前不查长度。SQLite 不强制宽度所以仓内测试看不见，
/// SQL Server / PostgreSQL 上一段超长的条款或地址会让发起从「建出草稿」变成 500（<c>DbUpdateException</c>）。
/// </para>
/// <para>
/// 失败方向关闭：越界按字段指名 400，早于任何落库；不截断 —— 悄悄截掉一段合同条款比拒绝更糟。
/// </para>
/// </remarks>
public class EnvelopeInitialValueLengthTests : SigningStorageTestBase
{
    private const string HostType = "Matter";
    private static readonly string TooLong = new('x', SigningLimits.MaxFieldValueLength + 1);
    private static readonly string AtLimit = new('x', SigningLimits.MaxFieldValueLength);

    /// <summary>provider 返回什么由测试用宿主 id 之外的静态槽决定。</summary>
    private sealed class ClauseMergeSource : IMergeSourceProvider
    {
        public string? Clause { get; set; }

        public string EntityType => HostType;

        public IReadOnlyList<MergeFieldDescriptor> Describe() => [new("Clause", "Clause")];

        public Task<IReadOnlyDictionary<string, object?>> ResolveAsync(Guid entityId, CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<string, object?> values = new Dictionary<string, object?> { ["Clause"] = Clause };
            return Task.FromResult(values);
        }
    }

    private readonly ClauseMergeSource _source = new();

    protected override IEnumerable<IMergeSourceProvider> MergeProviders => [_source];

    private static Field SenderField(string key = "Clause", string label = "Clause", string? binding = "Clause") => new()
    {
        Key = key,
        Label = label,
        Type = SigningFieldType.Text,
        RecipientRole = null,
        Binding = binding,
        Required = false,
        PlacementMode = FieldPlacementMode.Absolute,
        Page = 1,
        X = 0.1m,
        Y = 0.1m,
        W = 0.5m,
        H = 0.04m,
    };

    private async Task<Result<EnvelopeDto>> CreateAsync(Guid? hostId, Dictionary<string, string?>? prefilled = null)
    {
        var (templateId, _) = await ArrangeTemplateAsync([SenderField()]);
        using var create = BeginRequest();
        return await create.Envelopes.CreateAsync(new CreateEnvelopeDto
        {
            TemplateId = templateId,
            Title = "Engagement Letter",
            HostEntityType = hostId is null ? null : HostType,
            HostEntityId = hostId,
            Recipients = SingleClient(),
            PrefilledValues = prefilled,
        });
    }

    private async Task<bool> AnyEnvelopeAsync() => await DbContext.Set<Envelope>().AsNoTracking().AnyAsync();

    [Fact]
    public async Task Create_refuses_a_prefilled_value_over_the_limit()
    {
        var result = await CreateAsync(hostId: null, prefilled: new Dictionary<string, string?> { ["Clause"] = TooLong });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("Clause");
        result.Message!.ShouldContain(SigningLimits.MaxFieldValueLength.ToString());
        (await AnyEnvelopeAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Create_refuses_a_merged_value_over_the_limit()
    {
        _source.Clause = TooLong;

        var result = await CreateAsync(Guid.NewGuid());

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("Clause");
        (await AnyEnvelopeAsync()).ShouldBeFalse();
    }

    /// <summary>预填覆盖合并结果：provider 越界但发起方给了一个合规的值，以人的为准，放行。</summary>
    [Fact]
    public async Task Create_passes_when_a_prefilled_value_overrides_an_oversized_merged_one()
    {
        _source.Clause = TooLong;

        var result = await CreateAsync(Guid.NewGuid(), prefilled: new Dictionary<string, string?> { ["Clause"] = "short" });

        result.Succeeded.ShouldBeTrue(result.Message);
    }

    [Fact]
    public async Task Create_accepts_a_value_exactly_at_the_limit()
    {
        _source.Clause = AtLimit;

        var result = await CreateAsync(Guid.NewGuid());

        result.Succeeded.ShouldBeTrue(result.Message);
        var stored = await DbContext.Set<FieldValue>().AsNoTracking().SingleAsync(v => v.RequestId == result.Data!.Id);
        stored.Value.ShouldBe(AtLimit);
    }

    /// <summary>预填的键不对应任何快照字段时，报错拿键指名而不是崩在找标签上。</summary>
    [Fact]
    public async Task Create_names_an_unknown_prefilled_key_by_its_key()
    {
        var result = await CreateAsync(hostId: null, prefilled: new Dictionary<string, string?> { ["Extra"] = TooLong });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("Extra");
    }
}
