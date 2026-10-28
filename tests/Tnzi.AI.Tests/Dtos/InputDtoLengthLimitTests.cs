using System.ComponentModel.DataAnnotations;

namespace Tnzi.AI.Tests.Dtos;

/// <summary>
/// 输入 DTO 的长度上限与列一致：比列紧会把合法的长提示词拒成 400，比列松（或没有）会让超长输入在落库时变成 500。
/// </summary>
public class InputDtoLengthLimitTests
{
    [Fact]
    public void AgentInstructions_AcceptTheFullColumnLength()
    {
        // 列是 32000（与 Persona 同），此前 DTO 卡在 4000：管理端粘贴一段常见长度的系统提示词保存即 400，
        // 经 YAML 同步或克隆写进库里的长指令也无法再通过 API 修改
        Validate(new CreateAgentDto { Name = "a", Provider = "OpenAI", Instructions = new string('x', 32000) }).ShouldBeEmpty();
        Validate(new UpdateAgentDto { Instructions = new string('x', 32000) }).ShouldBeEmpty();

        Validate(new CreateAgentDto { Name = "a", Provider = "OpenAI", Instructions = new string('x', 32001) })
            .ShouldContain(r => r.MemberNames.Contains(nameof(CreateAgentDto.Instructions)));
        Validate(new UpdateAgentDto { Instructions = new string('x', 32001) })
            .ShouldContain(r => r.MemberNames.Contains(nameof(UpdateAgentDto.Instructions)));
    }

    [Theory]
    [InlineData(nameof(UpdateUserProfileDto.DisplayName), 200)]
    [InlineData(nameof(UpdateUserProfileDto.Role), 500)]
    [InlineData(nameof(UpdateUserProfileDto.PreferredLanguage), 20)]
    [InlineData(nameof(UpdateUserProfileDto.Content), 16000)]
    public void UserProfile_FieldsAreCappedAtTheirColumnLength(string field, int columnLength)
    {
        Validate(WithField(new UpdateUserProfileDto(), field, columnLength)).ShouldBeEmpty();
        Validate(WithField(new UpdateUserProfileDto(), field, columnLength + 1)).ShouldContain(r => r.MemberNames.Contains(field));
    }

    [Theory]
    [InlineData(nameof(SubAgentTypeInputDto.Name), 100)]
    [InlineData(nameof(SubAgentTypeInputDto.Description), 500)]
    [InlineData(nameof(SubAgentTypeInputDto.Instructions), 4000)]
    [InlineData(nameof(SubAgentTypeInputDto.DefaultModel), 200)]
    public void SubAgentType_FieldsAreCappedAtTheirColumnLength(string field, int columnLength)
    {
        var withinLimit = WithField(new SubAgentTypeInputDto { Name = "n", Description = "d" }, field, columnLength);
        var overLimit = WithField(new SubAgentTypeInputDto { Name = "n", Description = "d" }, field, columnLength + 1);

        Validate(withinLimit).ShouldBeEmpty();
        Validate(overLimit).ShouldContain(r => r.MemberNames.Contains(field));
    }

    private static T WithField<T>(T dto, string field, int length)
    {
        typeof(T).GetProperty(field)!.SetValue(dto, new string('x', length));
        return dto;
    }

    private static List<ValidationResult> Validate(object dto)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);
        return results;
    }
}
