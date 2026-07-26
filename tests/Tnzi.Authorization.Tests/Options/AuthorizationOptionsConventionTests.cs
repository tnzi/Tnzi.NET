using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Tnzi.Authorization.Options;
using Tnzi.Options;
using AuthOptions = Tnzi.Authorization.Options.AuthorizationOptions;

namespace Tnzi.Authorization.Tests.Options;

/// <summary>
/// Out-of-the-box super-admin convention tests: the PostConfigure default
/// (<see cref="AuthOptions.ApplyConventionDefaults"/>), the binder
/// list-APPEND regression it exists to dodge, and the validator's
/// contradiction checks around <c>DisableSuperAdminBypass</c>.
/// </summary>
public class AuthorizationOptionsConventionTests
{
    [Fact]
    public void Convention_default_fills_SuperAdmin_when_nothing_configured()
    {
        var options = new AuthOptions();

        AuthOptions.ApplyConventionDefaults(options);

        options.SuperAdminRoles.ShouldBe([AuthOptions.DefaultSuperAdminRoleName]);
    }

    [Fact]
    public void Convention_default_respects_configured_roles()
    {
        var options = new AuthOptions { SuperAdminRoles = ["Root"] };

        AuthOptions.ApplyConventionDefaults(options);

        options.SuperAdminRoles.ShouldBe(["Root"]);
    }

    [Fact]
    public void Convention_default_is_suppressed_by_DisableSuperAdminBypass()
    {
        var options = new AuthOptions { DisableSuperAdminBypass = true };

        AuthOptions.ApplyConventionDefaults(options);

        options.SuperAdminRoles.ShouldBeEmpty();
    }

    [Fact]
    public void SeedBuiltInAdminRoles_defaults_to_true()
    {
        new AuthOptions().SeedBuiltInAdminRoles.ShouldBeTrue();
    }

    /// <summary>
    /// Regression lock for the binder append trap: the configuration binder
    /// ADDS items to a pre-populated List instead of replacing it. The
    /// convention default therefore must NOT be a property initializer -
    /// a deployment configuring ["SuperAdmin"] must end up with exactly one
    /// entry through the module's real registration path (Bind +
    /// PostConfigure), or the validator's duplicate check kills startup.
    /// </summary>
    [Fact]
    public void Configured_role_list_binds_without_duplicating_the_convention_default()
    {
        var value = BuildOptions(new Dictionary<string, string?>
        {
            ["Authorization:SuperAdminRoles:0"] = "SuperAdmin",
        });

        value.SuperAdminRoles.ShouldBe(["SuperAdmin"]);
    }

    [Fact]
    public void Absent_configuration_resolves_to_the_convention_default_via_registration_path()
    {
        var value = BuildOptions([]);

        value.SuperAdminRoles.ShouldBe([AuthOptions.DefaultSuperAdminRoleName]);
    }

    [Fact]
    public void DisableSuperAdminBypass_via_configuration_yields_empty_roles()
    {
        var value = BuildOptions(new Dictionary<string, string?>
        {
            ["Authorization:DisableSuperAdminBypass"] = "true",
        });

        value.SuperAdminRoles.ShouldBeEmpty();
    }

    [Fact]
    public void Validator_rejects_disable_flag_combined_with_configured_roles()
    {
        var result = Validate(new AuthOptions
        {
            DisableSuperAdminBypass = true,
            SuperAdminRoles = ["SuperAdmin"],
        });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("DisableSuperAdminBypass");
    }

    [Fact]
    public void Validator_rejects_disable_flag_combined_with_bootstrap_users()
    {
        var result = Validate(new AuthOptions
        {
            DisableSuperAdminBypass = true,
            BootstrapSuperAdminUsers = ["admin"],
        });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("BootstrapSuperAdminUsers");
    }

    [Fact]
    public void Validator_rejects_whitespace_bootstrap_user_entries()
    {
        var result = Validate(new AuthOptions
        {
            SuperAdminRoles = ["SuperAdmin"],
            BootstrapSuperAdminUsers = ["admin", "  "],
        });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("BootstrapSuperAdminUsers");
    }

    [Fact]
    public void Validator_accepts_the_conventional_shape()
    {
        var options = new AuthOptions { BootstrapSuperAdminUsers = ["admin"] };
        AuthOptions.ApplyConventionDefaults(options);

        Validate(options).Succeeded.ShouldBeTrue();
    }

    /// <summary>Mirrors the module's real registration: Bind + validator + convention PostConfigure.</summary>
    private static AuthOptions BuildOptions(Dictionary<string, string?> configuration)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(configuration).Build();
        var services = new ServiceCollection();
        services.AddTnziOptions<AuthOptions, AuthorizationOptionsValidator>(config)
            .PostConfigure(AuthOptions.ApplyConventionDefaults);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<AuthOptions>>().Value;
    }

    private static ValidateOptionsResult Validate(AuthOptions options)
        => new AuthorizationOptionsValidator().Validate(null, options);
}
