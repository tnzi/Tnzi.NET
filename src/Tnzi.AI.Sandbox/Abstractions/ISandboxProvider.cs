namespace Tnzi.AI.Sandbox.Abstractions;

public interface ISandboxProvider
{
    string Name { get; }
    Task<ISandbox> CreateAsync(SandboxCreateOptions options, CancellationToken ct = default);
}
