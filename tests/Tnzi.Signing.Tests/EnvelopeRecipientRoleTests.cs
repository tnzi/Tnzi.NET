using Microsoft.EntityFrameworkCore;
using Tnzi.Results;
using Tnzi.Signing.Dtos;
using Tnzi.Signing.Entities;

namespace Tnzi.Signing.Tests;

/// <summary>
/// 一个角色只能对应一个收件人。
/// </summary>
/// <remarks>
/// <para>
/// 签名图与字段值都是<b>按角色</b>寻址的：密封器按 <c>Role</c> 分组取签名（同角色只取第一张），
/// 字段值按键存（同角色第二人的提交覆盖第一人）。此前 <c>CreateAsync</c> 对收件人只查「至少一个」，
/// 两名同角色的收件人各自签完之后：成品上只有一个人的签名、字段值是后交的那一份，
/// 而完成证书给两个人各写一行 Signed —— 成品与证据链矛盾，且全程零日志。
/// </para>
/// <para>
/// 在支持「同角色多人」（签名与字段值改按收件人寻址，是 schema 变更）之前，
/// 「一角色一人」是显式契约，在发起那一步就拒绝。
/// </para>
/// </remarks>
public class EnvelopeRecipientRoleTests : SigningStorageTestBase
{
    private async Task<Result<EnvelopeDto>> CreateWithAsync(params CreateSignerDto[] recipients)
    {
        var (templateId, _) = await ArrangeTemplateAsync();
        using var create = BeginRequest();
        return await create.Envelopes.CreateAsync(new CreateEnvelopeDto
        {
            TemplateId = templateId,
            Title = "Purchase Agreement",
            Recipients = [.. recipients],
        });
    }

    [Fact]
    public async Task Create_refuses_two_recipients_with_the_same_role()
    {
        var result = await CreateWithAsync(
            new CreateSignerDto { Role = "Buyer", Name = "Alice" },
            new CreateSignerDto { Role = "buyer", Name = "Bob" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("Buyer");

        // 只返回错误却照样落库的实现会骗过上面两条。
        (await DbContext.Set<Envelope>().AsNoTracking().AnyAsync()).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_refuses_a_blank_role(string? role)
    {
        var result = await CreateWithAsync(new CreateSignerDto { Role = role!, Name = "Alice" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        (await DbContext.Set<Envelope>().AsNoTracking().AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Create_accepts_distinct_roles()
    {
        var result = await CreateWithAsync(
            new CreateSignerDto { Role = "Buyer", Name = "Alice" },
            new CreateSignerDto { Role = "Seller", Name = "Bob" });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Recipients.Select(r => r.Role).ShouldBe(["Buyer", "Seller"]);
    }
}
