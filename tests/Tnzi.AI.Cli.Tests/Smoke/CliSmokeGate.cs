namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// Opt-in gate for tests that launch a real, user-installed agent CLI.
/// </summary>
/// <remarks>
/// <b>Hard rule for this project: default test runs never resolve or execute any
/// agent CLI.</b> A CI machine may genuinely have <c>claude</c> installed, and a
/// careless test would then spend the owner's account quota. So these tests are
/// double-gated: an explicit environment opt-in <i>and</i> the CLI actually being
/// present. Either one missing turns the test into a skip whose reason says which,
/// rather than a failure that reads like a real defect.
/// <para>
/// What they buy that fixtures cannot: fixtures are recordings, so they keep passing
/// after the real CLI changes its output. These are the only tests that notice
/// protocol drift.
/// </para>
/// </remarks>
public static class CliSmokeGate
{
    /// <summary>Environment variable that enables the smoke tests.</summary>
    public const string EnvVar = "TNZI_RUN_CLI_AGENT_SMOKE";

    /// <summary>Whether the operator explicitly opted in.</summary>
    public static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable(EnvVar), "1", StringComparison.Ordinal);

    /// <summary>Resolve a provider's executable, or null when it is not installed.</summary>
    public static string? ResolveExecutable(string providerKey)
    {
        if (!CliBuiltInProviders.All.TryGetValue(providerKey, out var provider)) return null;

        var resolver = new CliExecutableResolver(NullLogger<CliExecutableResolver>.Instance);
        return resolver.Resolve(provider);
    }

    /// <summary>
    /// The first installed provider speaking a given protocol, or null.
    /// </summary>
    /// <remarks>
    /// Used by the ACP smoke test so it exercises whichever ACP CLI the machine
    /// happens to have. Pinning it to one product would leave the test permanently
    /// skipped on every machine that installed a different one, which for a protocol
    /// covering seven CLIs is most machines.
    /// </remarks>
    public static (CliProviderDescriptor Provider, string ExecutablePath)? FirstInstalled(
        CliAgentProtocol protocol)
    {
        foreach (var provider in CliBuiltInProviders.All.Values.Where(p => p.Protocol == protocol))
        {
            var path = ResolveExecutable(provider.Key);
            if (path is not null) return (provider, path);
        }

        return null;
    }

    /// <summary>Skip reason for a provider-specific smoke test, or null to run it.</summary>
    public static string? SkipReason(string providerKey)
    {
        if (!Enabled) return $"Set {EnvVar}=1 to run smoke tests against real agent CLIs.";
        return ResolveExecutable(providerKey) is null
            ? $"The '{providerKey}' CLI is not installed on this machine."
            : null;
    }

    /// <summary>Skip reason for a protocol-level smoke test, or null to run it.</summary>
    public static string? SkipReasonForProtocol(CliAgentProtocol protocol)
    {
        if (!Enabled) return $"Set {EnvVar}=1 to run smoke tests against real agent CLIs.";
        return FirstInstalled(protocol) is null
            ? $"No CLI speaking {protocol} is installed on this machine."
            : null;
    }
}

/// <summary>
/// A <see cref="FactAttribute"/> that skips unless the smoke gate is open for a provider.
/// </summary>
/// <remarks>
/// The decision is made at discovery time by setting <see cref="FactAttribute.Skip"/>,
/// because xUnit 2.x has no runtime skip. That is the right time anyway: both inputs
/// (an environment variable and what is on PATH) are fixed for the run, and a skipped
/// test that never starts cannot accidentally spawn a process.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SmokeFactAttribute : FactAttribute
{
    /// <summary>Gate on a specific provider being installed.</summary>
    public SmokeFactAttribute(string providerKey)
        => Skip = CliSmokeGate.SkipReason(providerKey);
}

/// <summary>
/// A <see cref="FactAttribute"/> that skips unless some CLI speaking a protocol is installed.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SmokeProtocolFactAttribute : FactAttribute
{
    /// <summary>Gate on any provider of the given protocol being installed.</summary>
    public SmokeProtocolFactAttribute(CliAgentProtocol protocol)
        => Skip = CliSmokeGate.SkipReasonForProtocol(protocol);
}
