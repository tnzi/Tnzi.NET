using Microsoft.EntityFrameworkCore;
using Tnzi.Results;
using Tnzi.Signing.Dtos;
using Tnzi.Signing.Entities;
using Tnzi.Signing.Metadata;

namespace Tnzi.Signing.Tests;

/// <summary>
/// 发起一份签署请求时的两条边界：有效期从发出那一刻起算；发起方写入的文本按列宽校验、请求整份提交。
/// </summary>
public class EnvelopeCreateAndSendWindowTests : SigningStorageTestBase
{
    private async Task<Result<EnvelopeDto>> CreateAsync(Action<CreateEnvelopeDto>? configure = null)
    {
        var (templateId, _) = await ArrangeTemplateAsync();
        var input = new CreateEnvelopeDto
        {
            TemplateId = templateId,
            Title = "Engagement Letter",
            Recipients = SingleClient(),
        };
        configure?.Invoke(input);

        using var create = BeginRequest();
        return await create.Envelopes.CreateAsync(input);
    }

    private async Task<bool> AnyEnvelopeAsync() => await DbContext.Set<Envelope>().AsNoTracking().AnyAsync();

    // ── 有效期从发出那一刻起算 ──────────────────────────────────────────────

    /// <summary>
    /// 截止时间此前在建草稿时就定死，发出时不看也不改：一份有效期 3 天的草稿一周后才发，
    /// 发出的链接一打开就是已过期（只给状态、提交 410），列表里刚发出就显示过期。
    /// </summary>
    [Fact]
    public async Task Sending_an_old_draft_restarts_the_validity_window()
    {
        var created = await CreateAsync(i => i.ExpiresInDays = 3);
        created.Succeeded.ShouldBeTrue(created.Message);
        var requestId = created.Data!.Id;

        // 这份草稿五天前建、两天前就「过期」了。
        var createdAt = DateTime.UtcNow.AddDays(-5);
        await DbContext.Set<Envelope>().Where(e => e.Id == requestId).ExecuteUpdateAsync(s => s
            .SetProperty(e => e.CreationTime, createdAt)
            .SetProperty(e => e.ExpiresAt, createdAt.AddDays(3)));

        using (var send = BeginRequest())
        {
            (await send.Envelopes.SendAsync(requestId)).Succeeded.ShouldBeTrue();
        }

        using var read = BeginRequest();
        var envelope = (await read.Envelopes.GetAsync(requestId)).Data!;
        envelope.Status.ShouldBe(EnvelopeStatus.Sent);
        envelope.ExpiresAt.ShouldBeInRange(DateTime.UtcNow.AddDays(3).AddMinutes(-5), DateTime.UtcNow.AddDays(3).AddMinutes(5));
    }

    [Fact]
    public async Task Sending_right_away_keeps_the_requested_number_of_days()
    {
        var created = await CreateAsync(i => i.ExpiresInDays = 3);
        var requestId = created.Data!.Id;

        using (var send = BeginRequest())
        {
            (await send.Envelopes.SendAsync(requestId)).Succeeded.ShouldBeTrue();
        }

        var stored = await DbContext.Set<Envelope>().AsNoTracking().SingleAsync(e => e.Id == requestId);
        stored.ExpiresAt.ShouldBeInRange(DateTime.UtcNow.AddDays(3).AddMinutes(-5), DateTime.UtcNow.AddDays(3).AddMinutes(5));
    }

    // ── 发起方写入的文本按列宽校验，越界 400 且什么都不落库 ───────────────────────

    public static TheoryData<string, Action<CreateEnvelopeDto>> OverlongInputs() => new()
    {
        { "title", i => i.Title = new string('t', SigningLimits.MaxTitleLength + 1) },
        { "host entity type", i => i.HostEntityType = new string('h', SigningLimits.MaxHostEntityTypeLength + 1) },
        { "email", i => i.Recipients = [new CreateSignerDto { Role = "Client", Name = "Alice", Email = new string('e', SigningLimits.MaxEmailLength + 1) }] },
        { "name", i => i.Recipients = [new CreateSignerDto { Role = "Client", Name = new string('n', SigningLimits.MaxSignerNameLength + 1) }] },
        { "blank name", i => i.Recipients = [new CreateSignerDto { Role = "Client", Name = "   " }] },
        { "prefilled key", i => i.PrefilledValues = new Dictionary<string, string?> { [new string('k', SigningLimits.MaxFieldKeyLength + 1)] = "v" } },
        { "validity", i => i.ExpiresInDays = 100_000 },
    };

    /// <summary>
    /// 此前请求行先单独落库，越界的收件人或预填键在 SQL Server / PostgreSQL 上写入失败（500），
    /// 留下一份没有收件人的草稿；很大的有效期让计算截止时间抛异常。SQLite 不强制列宽，所以这里断言的是
    /// 服务在写库之前就按字段指名拒绝。
    /// </summary>
    [Theory]
    [MemberData(nameof(OverlongInputs))]
    public async Task Create_refuses_input_that_does_not_fit_before_writing_anything(string what, Action<CreateEnvelopeDto> configure)
    {
        var result = await CreateAsync(configure);

        result.Succeeded.ShouldBeFalse(what);
        result.Code.ShouldBe(400);
        (await AnyEnvelopeAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Create_accepts_values_at_the_limits()
    {
        var result = await CreateAsync(i =>
        {
            i.Title = new string('t', SigningLimits.MaxTitleLength);
            i.ExpiresInDays = SigningLimits.MaxExpiresInDays;
            i.Recipients = [new CreateSignerDto { Role = "Client", Name = new string('n', SigningLimits.MaxSignerNameLength), Email = "a@example.com" }];
        });

        result.Succeeded.ShouldBeTrue(result.Message);
    }
}
