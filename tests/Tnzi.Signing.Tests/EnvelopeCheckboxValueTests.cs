using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tnzi.Results;
using Tnzi.Security.Claims;
using Tnzi.Signing.Dtos;
using Tnzi.Signing.Entities;
using Tnzi.Signing.Metadata;

namespace Tnzi.Signing.Tests;

/// <summary>
/// 勾选框的取值契约：<c>true</c> / <c>false</c>（也认 <c>1</c> / <c>0</c>），入库前规范化；必填的勾选框必须是勾上的。
/// </summary>
/// <remarks>
/// 此前必填只看「非空白」，而密封只把 <c>true</c> / <c>1</c> 画成勾：<c>false</c> 过得了必填，
/// 成品上那个必勾的框是空的，完成证书照样写 Signed；HTML 复选框的默认值 <c>on</c> 也过得了，却被画成没勾。
/// </remarks>
public class EnvelopeCheckboxValueTests : SigningStorageTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        var anonymous = new Mock<ICurrentUser>();
        anonymous.SetupGet(u => u.IsAuthenticated).Returns(false);
        anonymous.SetupGet(u => u.Id).Returns((Guid?)null);
        services.AddScoped(_ => anonymous.Object);
    }

    private static Field Checkbox(string? role, bool required = true) => new()
    {
        Key = "agree",
        Label = "I agree to the terms",
        Type = SigningFieldType.Checkbox,
        RecipientRole = role,
        Required = required,
        PlacementMode = FieldPlacementMode.Absolute,
        Page = 1,
        X = 0.1m,
        Y = 0.5m,
        W = 0.05m,
        H = 0.03m,
    };

    private async Task<Signer> SignerOfAsync(Guid requestId)
        => await DbContext.Set<Signer>().AsNoTracking().SingleAsync(s => s.RequestId == requestId);

    private async Task<string?> StoredValueAsync(Guid requestId)
        => (await DbContext.Set<FieldValue>().AsNoTracking().SingleAsync(v => v.RequestId == requestId && v.FieldKey == "agree")).Value;

    private async Task<Result<SigningPacketDto>> SubmitAsync(string token, string? value)
    {
        using var submit = BeginRequest();
        return await submit.Envelopes.SubmitAsync(token, new SubmitSigningDto
        {
            Values = new Dictionary<string, string?> { ["agree"] = value },
        });
    }

    [Theory]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData(" FALSE ")]
    public async Task A_required_checkbox_left_unticked_is_refused(string value)
    {
        var (_, requestId, token) = await ArrangeSentEnvelopeAsync([Checkbox("Client")]);

        var result = await SubmitAsync(token, value);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("I agree to the terms");
        (await SignerOfAsync(requestId)).Status.ShouldNotBe(SigningRecipientStatus.Signed);
    }

    /// <summary>认不出的值当场拒绝（按字段指名），绝不照收、再在密封时画成一个空框。</summary>
    [Theory]
    [InlineData("on")]
    [InlineData("yes")]
    [InlineData("checked")]
    public async Task A_checkbox_value_outside_the_contract_is_refused(string value)
    {
        var (_, requestId, token) = await ArrangeSentEnvelopeAsync([Checkbox("Client", required: false)]);

        var result = await SubmitAsync(token, value);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("'I agree to the terms' must be 'true' or 'false'");
        (await SignerOfAsync(requestId)).Status.ShouldNotBe(SigningRecipientStatus.Signed);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("1")]
    public async Task A_ticked_required_checkbox_signs_and_is_stored_normalized(string value)
    {
        var (_, requestId, token) = await ArrangeSentEnvelopeAsync([Checkbox("Client")]);

        var result = await SubmitAsync(token, value);

        result.Succeeded.ShouldBeTrue(result.Message);
        (await SignerOfAsync(requestId)).Status.ShouldBe(SigningRecipientStatus.Signed);
        (await StoredValueAsync(requestId)).ShouldBe("true");
    }

    [Fact]
    public async Task An_optional_checkbox_left_unticked_is_stored_as_false()
    {
        var (_, requestId, token) = await ArrangeSentEnvelopeAsync([Checkbox("Client", required: false)]);

        var result = await SubmitAsync(token, "0");

        result.Succeeded.ShouldBeTrue(result.Message);
        (await StoredValueAsync(requestId)).ShouldBe("false");
    }

    // ── 发起方那侧 ────────────────────────────────────────────────────────

    private async Task<Result<EnvelopeDto>> CreateWithPrefillAsync(Guid templateId, string? value)
    {
        using var create = BeginRequest();
        return await create.Envelopes.CreateAsync(new CreateEnvelopeDto
        {
            TemplateId = templateId,
            Title = "Engagement Letter",
            Recipients = SingleClient(),
            PrefilledValues = new Dictionary<string, string?> { ["agree"] = value },
        });
    }

    /// <summary>发起方负责的必填勾选框预填成 false：发出时与「没有值」一样被拦下，而不是发出一份必勾框空着的文档。</summary>
    [Fact]
    public async Task A_required_sender_checkbox_prefilled_unticked_blocks_sending()
    {
        var (templateId, _) = await ArrangeTemplateAsync([Checkbox(role: null)]);
        var created = await CreateWithPrefillAsync(templateId, "false");
        created.Succeeded.ShouldBeTrue(created.Message);

        using var send = BeginRequest();
        var sent = await send.Envelopes.SendAsync(created.Data!.Id);

        sent.Succeeded.ShouldBeFalse();
        sent.Code.ShouldBe(409);
        sent.Message!.ShouldContain("I agree to the terms");
    }

    [Fact]
    public async Task A_sender_checkbox_value_outside_the_contract_is_refused_at_create()
    {
        var (templateId, _) = await ArrangeTemplateAsync([Checkbox(role: null)]);

        var created = await CreateWithPrefillAsync(templateId, "yes");

        created.Succeeded.ShouldBeFalse();
        created.Code.ShouldBe(400);
        created.Message!.ShouldContain("'I agree to the terms' must be 'true' or 'false'");
        (await DbContext.Set<Envelope>().AsNoTracking().AnyAsync()).ShouldBeFalse();
    }

    /// <summary>合并变量给的布尔值转成字符串是 <c>True</c>：照常认得，存成规范形态，发得出去。</summary>
    [Fact]
    public async Task A_sender_checkbox_prefilled_true_is_normalized_and_sends()
    {
        var (templateId, _) = await ArrangeTemplateAsync([Checkbox(role: null)]);
        var created = await CreateWithPrefillAsync(templateId, "True");
        created.Succeeded.ShouldBeTrue(created.Message);
        (await StoredValueAsync(created.Data!.Id)).ShouldBe("true");

        using var send = BeginRequest();
        (await send.Envelopes.SendAsync(created.Data.Id)).Succeeded.ShouldBeTrue();
    }
}
