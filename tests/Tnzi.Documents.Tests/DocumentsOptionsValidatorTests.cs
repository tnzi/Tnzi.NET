namespace Tnzi.Documents.Tests;

/// <summary>
/// <see cref="DocumentsOptionsValidator"/> 的启动期校验。
/// </summary>
public class DocumentsOptionsValidatorTests
{
    private readonly DocumentsOptionsValidator _validator = new();

    [Fact]
    public void Defaults_AreValid()
    {
        Validate(new DocumentsOptions()).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(4)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3601)]
    public void ConversionTimeout_OutOfRange_Fails(int seconds)
    {
        var result = Validate(new DocumentsOptions { ConversionTimeoutSeconds = seconds });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain(nameof(DocumentsOptions.ConversionTimeoutSeconds));
    }

    [Fact]
    public void LibreOfficePath_PointingNowhere_FailsAtStartupInsteadOfAtFirstUpload()
    {
        var missing = Path.Combine(Path.GetTempPath(), "tnzi-no-such-libreoffice", "soffice.exe");

        var result = Validate(new DocumentsOptions { LibreOfficePath = missing });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain(missing);
    }

    [Fact]
    public void LibreOfficePath_PointingAtAnExistingDirectory_IsAccepted()
    {
        var result = Validate(new DocumentsOptions { LibreOfficePath = Path.GetTempPath() });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void LibreOfficePath_Empty_IsAccepted_BecauseAutoDetectionTakesOver()
    {
        Validate(new DocumentsOptions { LibreOfficePath = null }).Succeeded.ShouldBeTrue();
        Validate(new DocumentsOptions { LibreOfficePath = "  " }).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void ProfileDirectory_MustBeAbsolute_BecauseLibreOfficeTakesItAsAFileUrl()
    {
        var result = Validate(new DocumentsOptions { ProfileDirectory = "relative/profile" });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain(nameof(DocumentsOptions.ProfileDirectory));
    }

    [Fact]
    public void ProfileDirectory_Absolute_IsAccepted()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "tnzi-profile");

        Validate(new DocumentsOptions { ProfileDirectory = absolute }).Succeeded.ShouldBeTrue();
    }

    private ValidateOptionsResult Validate(DocumentsOptions options)
        => _validator.Validate(Microsoft.Extensions.Options.Options.DefaultName, options);
}
