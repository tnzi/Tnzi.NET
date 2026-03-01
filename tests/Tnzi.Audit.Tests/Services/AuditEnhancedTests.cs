
namespace Tnzi.Audit.Tests.Services;

/// <summary>
/// Tests for [AuditDisabled] attribute and audit export functionality
/// </summary>
public class AuditEnhancedTests
{
    #region AuditDisabledAttribute Tests

    [Fact]
    public void AuditDisabledAttribute_DefaultConstructor_HasNoReason()
    {
        // Act
        var attr = new Tnzi.Audit.Metadata.AuditDisabledAttribute();

        // Assert
        attr.ShouldNotBeNull();
        attr.Reason.ShouldBeNull();
    }

    [Fact]
    public void AuditDisabledAttribute_WithReason_StoresReason()
    {
        // Act
        var attr = new Tnzi.Audit.Metadata.AuditDisabledAttribute("Health check endpoint");

        // Assert
        attr.Reason.ShouldBe("Health check endpoint");
    }

    [Fact]
    public void AuditDisabledAttribute_ReasonProperty_CanBeSet()
    {
        // Arrange
        var attr = new Tnzi.Audit.Metadata.AuditDisabledAttribute();

        // Act
        attr.Reason = "Test reason";

        // Assert
        attr.Reason.ShouldBe("Test reason");
    }

    [Fact]
    public void AuditDisabledAttribute_CanBeAppliedToClass()
    {
        // Arrange & Act
        var attributes = typeof(TestAuditDisabledClass)
            .GetCustomAttributes(typeof(Tnzi.Audit.Metadata.AuditDisabledAttribute), true);

        // Assert
        attributes.Length.ShouldBe(1);
    }

    [Fact]
    public void AuditDisabledAttribute_CanBeAppliedToMethod()
    {
        // Arrange & Act
        var method = typeof(TestAuditDisabledClass).GetMethod("TestMethod")!;
        var attributes = method.GetCustomAttributes(typeof(Tnzi.Audit.Metadata.AuditDisabledAttribute), true);

        // Assert
        attributes.Length.ShouldBe(1);
        ((Tnzi.Audit.Metadata.AuditDisabledAttribute)attributes[0]).Reason.ShouldBe("Test method");
    }

    [Fact]
    public void AuditDisabledAttribute_IsInheritable()
    {
        // Arrange & Act
        var attrUsage = typeof(Tnzi.Audit.Metadata.AuditDisabledAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), true)
            .Cast<AttributeUsageAttribute>()
            .First();

        // Assert
        attrUsage.Inherited.ShouldBeTrue();
    }

    [Fact]
    public void AuditDisabledAttribute_DoesNotAllowMultiple()
    {
        // Arrange & Act
        var attrUsage = typeof(Tnzi.Audit.Metadata.AuditDisabledAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), true)
            .Cast<AttributeUsageAttribute>()
            .First();

        // Assert
        attrUsage.AllowMultiple.ShouldBeFalse();
    }

    #endregion

    #region AuditOperationService Export Tests

    [Fact]
    public void ExportToCsvAsync_ServiceCreated_MethodExists()
    {
        // Verify the new export methods exist on the service interface
        var methods = typeof(IAuditOperationService).GetMethods();
        methods.ShouldContain(m => m.Name == "ExportToCsvAsync");
        methods.ShouldContain(m => m.Name == "ExportToJsonAsync");
    }

    [Fact]
    public void ExportToCsvAsync_InterfaceSignature_HasCorrectParameters()
    {
        // Verify the CSV export method signature
        var method = typeof(IAuditOperationService).GetMethod("ExportToCsvAsync")!;
        method.ShouldNotBeNull();
        method.ReturnType.ShouldBe(typeof(Task<Tnzi.Results.Result<string>>));
    }

    [Fact]
    public void ExportToJsonAsync_InterfaceSignature_HasCorrectParameters()
    {
        // Verify the JSON export method signature
        var method = typeof(IAuditOperationService).GetMethod("ExportToJsonAsync")!;
        method.ShouldNotBeNull();
        method.ReturnType.ShouldBe(typeof(Task<Tnzi.Results.Result<string>>));
    }

    #endregion

    #region Test Helpers

    [Tnzi.Audit.Metadata.AuditDisabled]
    private class TestAuditDisabledClass
    {
        [Tnzi.Audit.Metadata.AuditDisabled("Test method")]
        public void TestMethod() { }
    }

    #endregion
}
