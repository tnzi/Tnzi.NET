
using Tnzi.Audit.Options;

namespace Tnzi.Audit.Tests.Services;

/// <summary>
/// Tests for RequestBodyRedactor
/// </summary>
public class RequestBodyRedactorTests
{
    private readonly RequestBodyRedactor _redactor = new();

    private static HashSet<string> DefaultSensitiveFields => new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "token", "secret", "apiKey"
    };

    #region Basic Redaction

    [Fact]
    public void Redact_SimpleObject_RedactsSensitiveFields()
    {
        // Arrange
        var json = """{"username":"admin","password":"secret123"}""";

        // Act
        var result = _redactor.Redact(json, DefaultSensitiveFields);

        // Assert
        result.ShouldContain("\"username\":\"admin\"");
        result.ShouldContain("\"password\":\"***REDACTED***\"");
        result.ShouldNotContain("secret123");
    }

    [Fact]
    public void Redact_NoSensitiveFields_ReturnsOriginal()
    {
        // Arrange
        var json = """{"username":"admin","email":"admin@test.com"}""";

        // Act
        var result = _redactor.Redact(json, DefaultSensitiveFields);

        // Assert
        result.ShouldContain("\"username\":\"admin\"");
        result.ShouldContain("\"email\":\"admin@test.com\"");
    }

    [Fact]
    public void Redact_MultipleSensitiveFields_RedactsAll()
    {
        // Arrange
        var json = """{"password":"pass1","token":"tok123","apiKey":"key456","name":"test"}""";

        // Act
        var result = _redactor.Redact(json, DefaultSensitiveFields);

        // Assert
        result.ShouldContain("\"password\":\"***REDACTED***\"");
        result.ShouldContain("\"token\":\"***REDACTED***\"");
        result.ShouldContain("\"apiKey\":\"***REDACTED***\"");
        result.ShouldContain("\"name\":\"test\"");
    }

    #endregion

    #region Nested Objects

    [Fact]
    public void Redact_NestedObject_RedactsSensitiveFieldsAtAllLevels()
    {
        // Arrange
        var json = """{"user":{"name":"admin","password":"secret"},"token":"abc"}""";

        // Act
        var result = _redactor.Redact(json, DefaultSensitiveFields);

        // Assert
        result.ShouldContain("\"password\":\"***REDACTED***\"");
        result.ShouldContain("\"token\":\"***REDACTED***\"");
        result.ShouldContain("\"name\":\"admin\"");
    }

    [Fact]
    public void Redact_ArrayOfObjects_RedactsSensitiveFields()
    {
        // Arrange
        var json = """[{"password":"p1"},{"password":"p2"},{"name":"ok"}]""";

        // Act
        var result = _redactor.Redact(json, DefaultSensitiveFields);

        // Assert
        result.ShouldNotContain("p1");
        result.ShouldNotContain("p2");
        result.ShouldContain("\"name\":\"ok\"");
    }

    #endregion

    #region Case Insensitivity

    [Fact]
    public void Redact_CaseInsensitiveFields_RedactsRegardlessOfCase()
    {
        // Arrange
        var json = """{"Password":"secret","TOKEN":"abc","ApiKey":"key"}""";

        // Act - the SensitiveFields HashSet uses OrdinalIgnoreCase
        var result = _redactor.Redact(json, DefaultSensitiveFields);

        // Assert
        result.ShouldContain("\"Password\":\"***REDACTED***\"");
        result.ShouldContain("\"TOKEN\":\"***REDACTED***\"");
        result.ShouldContain("\"ApiKey\":\"***REDACTED***\"");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Redact_EmptySensitiveFields_ReturnsOriginal()
    {
        // Arrange
        var json = """{"password":"secret"}""";
        var emptyFields = new HashSet<string>();

        // Act
        var result = _redactor.Redact(json, emptyFields);

        // Assert
        result.ShouldBe(json);
    }

    [Fact]
    public void Redact_InvalidJson_ReturnsOriginal()
    {
        // Arrange
        var notJson = "this is not json";

        // Act
        var result = _redactor.Redact(notJson, DefaultSensitiveFields);

        // Assert
        result.ShouldBe(notJson);
    }

    [Fact]
    public void Redact_EmptyObject_ReturnsEmptyObject()
    {
        // Arrange
        var json = "{}";

        // Act
        var result = _redactor.Redact(json, DefaultSensitiveFields);

        // Assert
        result.ShouldBe("{}");
    }

    [Fact]
    public void Redact_NumberAndBooleanValues_Preserved()
    {
        // Arrange
        var json = """{"count":42,"active":true,"password":"secret"}""";

        // Act
        var result = _redactor.Redact(json, DefaultSensitiveFields);

        // Assert
        result.ShouldContain("\"count\":42");
        result.ShouldContain("\"active\":true");
        result.ShouldContain("\"password\":\"***REDACTED***\"");
    }

    [Fact]
    public void Redact_NullValue_Preserved()
    {
        // Arrange
        var json = """{"name":null,"password":"secret"}""";

        // Act
        var result = _redactor.Redact(json, DefaultSensitiveFields);

        // Assert
        result.ShouldContain("\"name\":null");
        result.ShouldContain("\"password\":\"***REDACTED***\"");
    }

    [Fact]
    public void Redact_SensitiveFieldWithNonStringValue_RedactsToString()
    {
        // Arrange - sensitive field has numeric value
        var json = """{"password":12345}""";

        // Act
        var result = _redactor.Redact(json, DefaultSensitiveFields);

        // Assert - value is replaced with redacted string
        result.ShouldContain("\"password\":\"***REDACTED***\"");
        result.ShouldNotContain("12345");
    }

    #endregion

    #region AuditOptions Defaults

    [Fact]
    public void AuditOptions_DefaultSensitiveFields_ContainsExpectedFields()
    {
        // Arrange
        var options = new AuditOptions();

        // Assert
        options.SensitiveFields.ShouldContain("password");
        options.SensitiveFields.ShouldContain("token");
        options.SensitiveFields.ShouldContain("secret");
        options.SensitiveFields.ShouldContain("apiKey");
        options.SensitiveFields.ShouldContain("refreshToken");
        options.SensitiveFields.ShouldContain("connectionString");
    }

    [Fact]
    public void AuditOptions_SensitiveFields_IsCaseInsensitive()
    {
        // Arrange
        var options = new AuditOptions();

        // Assert - comparer is OrdinalIgnoreCase
        options.SensitiveFields.Contains("PASSWORD").ShouldBeTrue();
        options.SensitiveFields.Contains("Token").ShouldBeTrue();
    }

    [Fact]
    public void AuditOptions_EnableRequestBodyCapture_DefaultFalse()
    {
        // Arrange
        var options = new AuditOptions();

        // Assert
        options.EnableRequestBodyCapture.ShouldBeFalse();
    }

    [Fact]
    public void AuditOptions_MaxRequestBodySize_Default4096()
    {
        // Arrange
        var options = new AuditOptions();

        // Assert
        options.MaxRequestBodySize.ShouldBe(4096);
    }

    #endregion

    #region AuditOptionsValidator

    [Fact]
    public void Validator_MaxRequestBodySize_Zero_ReturnsError()
    {
        // Arrange
        var options = new AuditOptions { MaxRequestBodySize = 0 };
        var validator = new AuditOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("MaxRequestBodySize");
    }

    [Fact]
    public void Validator_MaxRequestBodySize_TooLarge_ReturnsError()
    {
        // Arrange
        var options = new AuditOptions { MaxRequestBodySize = 100000 };
        var validator = new AuditOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("MaxRequestBodySize");
    }

    [Fact]
    public void Validator_MaxRequestBodySize_Valid_Passes()
    {
        // Arrange
        var options = new AuditOptions { MaxRequestBodySize = 8192 };
        var validator = new AuditOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        result.Succeeded.ShouldBeTrue();
    }

    #endregion

    #region Entity and DTO

    [Fact]
    public void AuditOperation_HasRequestBodyProperty()
    {
        // Arrange
        var operation = new AuditOperation();

        // Act
        operation.RequestBody = """{"test":"value"}""";

        // Assert
        operation.RequestBody.ShouldBe("""{"test":"value"}""");
    }

    [Fact]
    public void AuditOperation_RequestBody_DefaultNull()
    {
        // Arrange & Act
        var operation = new AuditOperation();

        // Assert
        operation.RequestBody.ShouldBeNull();
    }

    [Fact]
    public void AuditOperationDto_HasRequestBodyProperty()
    {
        // Arrange
        var dto = new AuditOperationDto();

        // Act
        dto.RequestBody = """{"test":"value"}""";

        // Assert
        dto.RequestBody.ShouldBe("""{"test":"value"}""");
    }

    #endregion
}
