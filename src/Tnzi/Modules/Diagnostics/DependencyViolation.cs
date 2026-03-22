namespace Tnzi.Modules.Diagnostics;

/// <summary>
/// Represents an undeclared cross-module dependency violation
/// </summary>
public record DependencyViolation(
    Type ConsumerModule,
    Type ServiceType,
    Type ProviderModule,
    string Message);
