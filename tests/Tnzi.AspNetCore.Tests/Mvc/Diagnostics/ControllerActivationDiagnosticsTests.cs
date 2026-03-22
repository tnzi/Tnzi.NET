namespace Tnzi.AspNetCore.Tests.Mvc.Diagnostics;

public class ControllerActivationDiagnosticsTests
{
    [Fact]
    public void RecordActivation_ShouldStoreRecord()
    {
        var diag = new ControllerActivationDiagnostics();

        diag.RecordActivation("TestController", "TestModule", "auth", isDefault: true);

        var records = diag.GetRecords();
        Assert.Single(records);
        Assert.Equal("TestController", records[0].ControllerType);
        Assert.True(records[0].IsDefault);
        Assert.True(records[0].IsActive);
    }

    [Fact]
    public void RecordSuppression_ShouldStoreWithReason()
    {
        var diag = new ControllerActivationDiagnostics();

        diag.RecordSuppression("TestController", "Missing IMyService", SuppressionReason.MissingDependency);

        var records = diag.GetRecords();
        Assert.Single(records);
        Assert.False(records[0].IsActive);
        Assert.Equal(SuppressionReason.MissingDependency, records[0].SuppressionReason);
        Assert.Equal("Missing IMyService", records[0].SuppressionDetail);
    }

    [Fact]
    public void RecordReplacement_ShouldStoreReplacementInfo()
    {
        var diag = new ControllerActivationDiagnostics();

        diag.RecordReplacement("DefaultAuthController", "AuthController", "auth");

        var records = diag.GetRecords();
        Assert.Single(records);
        Assert.Equal("AuthController", records[0].ReplacedBy);
        Assert.Equal(SuppressionReason.ReplacedByUser, records[0].SuppressionReason);
        Assert.False(records[0].IsActive);
    }

    [Fact]
    public void GetRecords_ShouldReturnAllRecords()
    {
        var diag = new ControllerActivationDiagnostics();

        diag.RecordActivation("C1", null, "r1", false);
        diag.RecordSuppression("C2", "reason", SuppressionReason.MarkerAbsent);
        diag.RecordReplacement("C3", "C4", "r3");

        Assert.Equal(3, diag.GetRecords().Count);
    }
}
