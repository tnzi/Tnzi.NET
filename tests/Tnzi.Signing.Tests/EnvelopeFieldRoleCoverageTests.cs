using Microsoft.EntityFrameworkCore;
using Tnzi.Results;
using Tnzi.Signing.Dtos;
using Tnzi.Signing.Entities;
using Tnzi.Signing.Metadata;

namespace Tnzi.Signing.Tests;

/// <summary>
/// 模板里指名了角色的字段，那个角色必须真的有人持有。
/// </summary>
/// <remarks>
/// <para>
/// 签名字段没有角色的情况在模板校验处拦下了（<see cref="EnvelopeTemplateValidationTests"/>），
/// 但同一个结果换一条路照样走得通：字段的角色是 <c>Witness</c>（或一个拼错的 <c>Cient</c>），而这份请求的
/// 收件人只有 <c>Buyer</c>。<c>CreateAsync</c> 此前只拿收件人跟收件人自己比（空白 / 重复），从不拿字段的角色
/// 跟收件人的角色比；<c>SubmitAsync</c> 只对本人角色的字段要签名要值；密封器对找不到签名的字段只记一行 Warning。
/// 于是一份签名位空白的成品照样被密封、算哈希、出完成证书、归档 —— 唯一的症状是那一行 Warning。
/// </para>
/// <para>
/// 规则：签名类字段（无论必填与否）与必填字段指名的角色都必须在收件人里；非必填的非签名字段留白是它自己的选择，不拦。
/// 拒绝发生在发起那一步（400），早于任何落库与排版。
/// </para>
/// </remarks>
public class EnvelopeFieldRoleCoverageTests : SigningStorageTestBase
{
    private static Field FieldFor(string role, SigningFieldType type = SigningFieldType.Signature, bool required = true, string key = "Sig") => new()
    {
        Key = key,
        Label = key,
        Type = type,
        RecipientRole = role,
        Required = required,
        PlacementMode = FieldPlacementMode.Absolute,
        Page = 1,
        X = 0.1m,
        Y = 0.1m,
        W = 0.3m,
        H = 0.05m,
    };

    private async Task<Result<EnvelopeDto>> CreateAsync(IReadOnlyList<Field> fields, params CreateSignerDto[] recipients)
    {
        var (templateId, _) = await ArrangeTemplateAsync(fields);
        using var create = BeginRequest();
        return await create.Envelopes.CreateAsync(new CreateEnvelopeDto
        {
            TemplateId = templateId,
            Title = "Purchase Agreement",
            Recipients = [.. recipients],
        });
    }

    private async Task<bool> AnyEnvelopeAsync() => await DbContext.Set<Envelope>().AsNoTracking().AnyAsync();

    [Fact]
    public async Task Create_refuses_a_signature_field_whose_role_no_recipient_holds()
    {
        var result = await CreateAsync([FieldFor("Witness")], new CreateSignerDto { Role = "Buyer", Name = "Alice" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("Sig");
        result.Message!.ShouldContain("Witness");
        (await AnyEnvelopeAsync()).ShouldBeFalse();
    }

    /// <summary>拼错一个字母就是「没人持有」：字段角色 Cient 对收件人 Client。</summary>
    [Fact]
    public async Task Create_refuses_a_misspelled_field_role()
    {
        var result = await CreateAsync([FieldFor("Cient")], SingleClient()[0]);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("Cient");
    }

    [Fact]
    public async Task Create_refuses_a_required_text_field_whose_role_no_recipient_holds()
    {
        var result = await CreateAsync(
            [FieldFor("Guarantor", SigningFieldType.Text, required: true, key: "GuarantorName")],
            SingleClient()[0]);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("GuarantorName");
        result.Message!.ShouldContain("Guarantor");
        (await AnyEnvelopeAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Create_names_every_uncovered_field()
    {
        var result = await CreateAsync(
            [FieldFor("Witness", key: "WitnessSig"), FieldFor("Notary", key: "NotarySig")],
            SingleClient()[0]);

        result.Succeeded.ShouldBeFalse();
        result.Message!.ShouldContain("WitnessSig");
        result.Message!.ShouldContain("NotarySig");
    }

    /// <summary>非必填的非签名字段可以指向一个不在场的角色：留白是它自己的选择，与非必填的发起方字段同一口径。</summary>
    [Fact]
    public async Task Create_allows_an_optional_text_field_for_an_absent_role()
    {
        var result = await CreateAsync(
            [FieldFor("Witness", SigningFieldType.Text, required: false, key: "WitnessNotes")],
            SingleClient()[0]);

        result.Succeeded.ShouldBeTrue(result.Message);
    }

    /// <summary>非必填的签名字段仍然要有人：一个没人签的签名框在成品上就是一块空白。</summary>
    [Fact]
    public async Task Create_refuses_an_optional_signature_field_for_an_absent_role()
    {
        var result = await CreateAsync([FieldFor("Witness", required: false)], SingleClient()[0]);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    /// <summary>角色比对与其它处一致：大小写不敏感、两边都修剪。</summary>
    [Fact]
    public async Task Create_matches_roles_case_insensitively_and_trimmed()
    {
        var result = await CreateAsync(
            [FieldFor("client")],
            new CreateSignerDto { Role = "  Client ", Name = "Alice" });

        result.Succeeded.ShouldBeTrue(result.Message);
    }

    /// <summary>发起方字段（角色为空）不指向任何人，不在本规则之内。</summary>
    [Fact]
    public async Task Create_ignores_fields_with_no_role()
    {
        var result = await CreateAsync(
            [FieldFor(null!, SigningFieldType.Text, required: true, key: "Price")],
            SingleClient()[0]);

        result.Succeeded.ShouldBeTrue(result.Message);
    }

    [Fact]
    public async Task Create_passes_when_every_addressed_role_is_held()
    {
        var result = await CreateAsync(
            [FieldFor("Buyer", key: "BuyerSig"), FieldFor("Seller", key: "SellerSig")],
            new CreateSignerDto { Role = "Buyer", Name = "Alice" },
            new CreateSignerDto { Role = "Seller", Name = "Bob" });

        result.Succeeded.ShouldBeTrue(result.Message);
    }
}
