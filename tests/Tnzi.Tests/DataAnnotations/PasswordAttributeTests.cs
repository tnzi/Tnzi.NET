using System.ComponentModel.DataAnnotations;
using Tnzi.DataAnnotations;

namespace Tnzi.Tests.DataAnnotations;

/// <summary>
/// MVC 按 (类型, 属性) 缓存 <see cref="ValidationAttribute"/> 实例，一个 <c>[Password]</c> 属性对应唯一实例、
/// 全部并发请求共享。特性因此必须是无状态的：此前 <see cref="PasswordAttribute"/> 把最后一次校验的明文口令
/// 存进实例字段供 <c>FormatErrorMessage</c> 读回，并发请求会拿到描述别人口令的错误信息，且明文口令常驻进程。
/// </summary>
public class PasswordAttributeTests
{
    private static ValidationResult? Validate(PasswordAttribute attribute, object? value)
    {
        var context = new ValidationContext(new object()) { DisplayName = "Password" };
        return attribute.GetValidationResult(value, context);
    }

    [Fact]
    public void FormatErrorMessage_DoesNotDependOnPreviousValidations()
    {
        var attribute = new PasswordAttribute();

        var before = attribute.FormatErrorMessage("Password");
        Assert.False(attribute.IsValid("abcdefgh"));
        var after = attribute.FormatErrorMessage("Password");

        Assert.Equal(before, after);
    }

    [Fact]
    public void GetValidationResult_InterleavedCalls_EachReportsItsOwnRule()
    {
        // A 的口令太短，B 的口令没有数字：A 先校验、B 再校验，A 读到的必须仍是长度错误
        var attribute = new PasswordAttribute();

        var a = Validate(attribute, "ab1");
        var b = Validate(attribute, "abcdefgh");

        Assert.Contains("at least 6 characters", a!.ErrorMessage);
        Assert.Contains("at least one digit", b!.ErrorMessage);
        Assert.DoesNotContain("digit", a.ErrorMessage);
    }

    [Fact]
    public void GetValidationResult_ConcurrentCalls_MessageMatchesOwnInput()
    {
        var attribute = new PasswordAttribute();
        var failures = new ConcurrentBag<string>();

        Parallel.For(0, 2000, i =>
        {
            var (input, expected) = i % 2 == 0
                ? ("ab1", "at least 6 characters")
                : ("abcdefgh", "at least one digit");
            var result = Validate(attribute, input);
            if (result?.ErrorMessage == null || !result.ErrorMessage.Contains(expected))
                failures.Add($"{input} -> {result?.ErrorMessage}");
        });

        Assert.Empty(failures);
    }

    [Theory]
    [InlineData("ab1", "at least 6 characters")]
    [InlineData("abcdefgh", "at least one digit")]
    [InlineData("12345678", "cannot consist solely of digits")]
    [InlineData("ABCDEFG1", "at least one lowercase letter")]
    public void GetValidationResult_ReportsFirstFailingRule(string input, string expectedFragment)
    {
        var result = Validate(new PasswordAttribute(), input);

        Assert.NotNull(result);
        Assert.StartsWith("Password ", result.ErrorMessage);
        Assert.Contains(expectedFragment, result.ErrorMessage);
    }

    [Fact]
    public void GetValidationResult_RequiredUppercase_ReportsUppercaseRule()
    {
        var result = Validate(new PasswordAttribute { RequiredUppercase = true }, "abcdefg1");

        Assert.Contains("at least one uppercase letter", result!.ErrorMessage);
    }

    [Theory]
    [InlineData("abcdef1")]
    [InlineData(null)]
    public void GetValidationResult_ValidOrNull_Succeeds(string? input)
    {
        Assert.Equal(ValidationResult.Success, Validate(new PasswordAttribute(), input));
    }

    [Fact]
    public void GetValidationResult_NonString_Fails()
    {
        var result = Validate(new PasswordAttribute(), 123456);

        Assert.NotNull(result);
        Assert.NotEqual(ValidationResult.Success, result);
    }

    [Fact]
    public void IsValid_BoolOverload_AgreesWithValidationResult()
    {
        var attribute = new PasswordAttribute();

        Assert.True(attribute.IsValid("abcdef1"));
        Assert.False(attribute.IsValid("abcdefgh"));
        Assert.True(attribute.IsValid(null));
        Assert.False(attribute.IsValid(123456));
    }

    [Fact]
    public void Attribute_DoesNotRetainValidatedInput()
    {
        var stringFields = typeof(PasswordAttribute)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(f => f.FieldType == typeof(string))
            .Select(f => f.Name)
            .ToList();

        Assert.Empty(stringFields);
    }
}
