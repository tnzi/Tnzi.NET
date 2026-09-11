using Tnzi.Feature.Services;

namespace Tnzi.Feature.Tests;

/// <summary>
/// A concrete provider for service-level tests. A <c>Mock&lt;IFeatureValueProvider&gt;</c> would
/// return <c>false</c> for the default interface members (Moq does not invoke DIM bodies),
/// which would silently turn every keyed provider into a keyless one in the tests.
/// </summary>
internal sealed class StubFeatureValueProvider : IFeatureValueProvider
{
    private readonly Func<string, string?> _resolve;

    public StubFeatureValueProvider(
        string name,
        int priority,
        bool requiresKey,
        bool isActive = true,
        string? inactiveReason = null,
        Func<string, string?>? resolve = null)
    {
        Name = name;
        Priority = priority;
        RequiresKey = requiresKey;
        IsActive = isActive;
        InactiveReason = isActive ? null : inactiveReason ?? "inactive in this test";
        _resolve = resolve ?? (_ => null);
    }

    public string Name { get; }

    public int Priority { get; }

    public bool RequiresKey { get; }

    public bool IsActive { get; }

    public string? InactiveReason { get; }

    public Task<string?> GetOrNullAsync(string featureName) => Task.FromResult(_resolve(featureName));
}
