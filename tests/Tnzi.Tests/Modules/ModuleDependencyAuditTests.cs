namespace Tnzi.Tests.Modules;

public class ModuleDependencyAuditTests
{
    [Fact]
    public void AuditAndReport_WithNullInputs_ShouldReturnEmpty()
    {
        var violations = ModuleDependencyAuditor.AuditAndReport(null!, null!);
        Assert.Empty(violations);
    }

    [Fact]
    public void AuditAndReport_WithEmptyModules_ShouldReturnEmpty()
    {
        var violations = ModuleDependencyAuditor.AuditAndReport(
            new List<IModuleDescriptor>(),
            new Dictionary<Type, List<ServiceDescriptor>>());
        Assert.Empty(violations);
    }

    [Fact]
    public void AuditAndReport_WithNoDependencyViolations_ShouldReturnEmpty()
    {
        // A module whose services only depend on system services
        var modules = new List<IModuleDescriptor>();
        var serviceMap = new Dictionary<Type, List<ServiceDescriptor>>();

        var violations = ModuleDependencyAuditor.AuditAndReport(modules, serviceMap);
        Assert.Empty(violations);
    }
}
