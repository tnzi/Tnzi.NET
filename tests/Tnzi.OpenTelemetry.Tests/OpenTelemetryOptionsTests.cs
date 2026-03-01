using Tnzi.OpenTelemetry;
using Tnzi.OpenTelemetry.Options;

namespace Tnzi.OpenTelemetry.Tests;

/// <summary>
/// Tests for OpenTelemetryOptions defaults and OpenTelemetryModule source collection logic
/// </summary>
public class OpenTelemetryOptionsTests
{
    [Fact]
    public void Options_DefaultValues_ShouldBeCorrect()
    {
        var options = new OpenTelemetryOptions();

        options.EnableTracing.ShouldBeTrue();
        options.EnableMetrics.ShouldBeTrue();
        options.EnableLogging.ShouldBeFalse();
        options.InstrumentAspNetCore.ShouldBeTrue();
        options.InstrumentHttpClient.ShouldBeTrue();
        options.InstrumentEntityFramework.ShouldBeTrue();
        options.InstrumentFramework.ShouldBeTrue();
        options.SamplingRatio.ShouldBe(1.0);
        options.UseGrpc.ShouldBeTrue();
        options.ExportToConsole.ShouldBeFalse();
        options.OtlpEndpoint.ShouldBeNull();
        options.ServiceName.ShouldBeNull();
        options.ServiceVersion.ShouldBeNull();
        options.ServiceInstanceId.ShouldBeNull();
        options.AdditionalActivitySources.ShouldBeEmpty();
        options.AdditionalMeters.ShouldBeEmpty();
    }

    [Fact]
    public void CollectActivitySourceNames_WhenInstrumentFrameworkTrue_ShouldIncludeFrameworkSources()
    {
        var options = new OpenTelemetryOptions { InstrumentFramework = true };

        var sources = OpenTelemetryModule.CollectActivitySourceNames(options);

        sources.ShouldContain("Tnzi.AI");
        sources.ShouldContain("Tnzi.AI.Rag");
        sources.ShouldContain("Tnzi.Framework");
        sources.Length.ShouldBe(3);
    }

    [Fact]
    public void CollectActivitySourceNames_WhenInstrumentFrameworkFalse_ShouldNotIncludeFrameworkSources()
    {
        var options = new OpenTelemetryOptions { InstrumentFramework = false };

        var sources = OpenTelemetryModule.CollectActivitySourceNames(options);

        sources.ShouldBeEmpty();
    }

    [Fact]
    public void CollectActivitySourceNames_WithAdditionalSources_ShouldIncludeBoth()
    {
        var options = new OpenTelemetryOptions
        {
            InstrumentFramework = true,
            AdditionalActivitySources = new List<string> { "MyApp.Custom", "MyApp.Other" }
        };

        var sources = OpenTelemetryModule.CollectActivitySourceNames(options);

        sources.ShouldContain("Tnzi.AI");
        sources.ShouldContain("Tnzi.AI.Rag");
        sources.ShouldContain("Tnzi.Framework");
        sources.ShouldContain("MyApp.Custom");
        sources.ShouldContain("MyApp.Other");
        sources.Length.ShouldBe(5);
    }

    [Fact]
    public void CollectActivitySourceNames_WithOnlyAdditionalSources_ShouldIncludeOnlyCustom()
    {
        var options = new OpenTelemetryOptions
        {
            InstrumentFramework = false,
            AdditionalActivitySources = new List<string> { "MyApp.Custom" }
        };

        var sources = OpenTelemetryModule.CollectActivitySourceNames(options);

        sources.Length.ShouldBe(1);
        sources.ShouldContain("MyApp.Custom");
    }

    [Fact]
    public void CollectActivitySourceNames_WithEmptyAndWhitespaceSources_ShouldFilterThem()
    {
        var options = new OpenTelemetryOptions
        {
            InstrumentFramework = false,
            AdditionalActivitySources = new List<string> { "MyApp.Valid", "", "  ", "MyApp.Other" }
        };

        var sources = OpenTelemetryModule.CollectActivitySourceNames(options);

        sources.Length.ShouldBe(2);
        sources.ShouldContain("MyApp.Valid");
        sources.ShouldContain("MyApp.Other");
    }

    [Fact]
    public void CollectActivitySourceNames_WithDuplicates_ShouldDedup()
    {
        var options = new OpenTelemetryOptions
        {
            InstrumentFramework = true,
            AdditionalActivitySources = new List<string> { "Tnzi.AI", "MyApp.Custom" }
        };

        var sources = OpenTelemetryModule.CollectActivitySourceNames(options);

        // Tnzi.AI should appear only once (deduped)
        sources.Count(s => s == "Tnzi.AI").ShouldBe(1);
        sources.ShouldContain("MyApp.Custom");
        sources.Length.ShouldBe(4); // 3 framework + 1 custom (Tnzi.AI deduped)
    }

    [Fact]
    public void CollectMeterNames_WhenInstrumentFrameworkTrue_ShouldIncludeFrameworkMeters()
    {
        var options = new OpenTelemetryOptions { InstrumentFramework = true };

        var meters = OpenTelemetryModule.CollectMeterNames(options);

        meters.ShouldContain("Tnzi.AI");
        meters.ShouldContain("Tnzi.AI.Rag");
        meters.ShouldContain("Tnzi.Framework");
        meters.Length.ShouldBe(3);
    }

    [Fact]
    public void CollectMeterNames_WhenInstrumentFrameworkFalse_ShouldNotIncludeFrameworkMeters()
    {
        var options = new OpenTelemetryOptions { InstrumentFramework = false };

        var meters = OpenTelemetryModule.CollectMeterNames(options);

        meters.ShouldBeEmpty();
    }

    [Fact]
    public void CollectMeterNames_WithAdditionalMeters_ShouldIncludeBoth()
    {
        var options = new OpenTelemetryOptions
        {
            InstrumentFramework = true,
            AdditionalMeters = new List<string> { "MyApp.Metrics" }
        };

        var meters = OpenTelemetryModule.CollectMeterNames(options);

        meters.ShouldContain("Tnzi.AI");
        meters.ShouldContain("Tnzi.Framework");
        meters.ShouldContain("MyApp.Metrics");
        meters.Length.ShouldBe(4);
    }

    [Fact]
    public void CollectMeterNames_WithDuplicates_ShouldDedup()
    {
        var options = new OpenTelemetryOptions
        {
            InstrumentFramework = true,
            AdditionalMeters = new List<string> { "Tnzi.Framework", "MyApp.Custom" }
        };

        var meters = OpenTelemetryModule.CollectMeterNames(options);

        meters.Count(m => m == "Tnzi.Framework").ShouldBe(1);
        meters.ShouldContain("MyApp.Custom");
        meters.Length.ShouldBe(4); // 3 framework + 1 custom (Tnzi.Framework deduped)
    }

    [Fact]
    public void CollectMeterNames_WithEmptyAndWhitespaceMeters_ShouldFilterThem()
    {
        var options = new OpenTelemetryOptions
        {
            InstrumentFramework = false,
            AdditionalMeters = new List<string> { "MyApp.Valid", "", "  " }
        };

        var meters = OpenTelemetryModule.CollectMeterNames(options);

        meters.Length.ShouldBe(1);
        meters.ShouldContain("MyApp.Valid");
    }
}

/// <summary>
/// Tests for OpenTelemetryOptionsValidator
/// </summary>
public class OpenTelemetryOptionsValidatorTests
{
    private readonly OpenTelemetryOptionsValidator _validator = new();

    [Fact]
    public void Validate_DefaultOptions_ShouldPass()
    {
        var options = new OpenTelemetryOptions();

        var result = _validator.Validate(null, options);

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_InvalidSamplingRatio_Negative_ShouldFail()
    {
        var options = new OpenTelemetryOptions { SamplingRatio = -0.1 };

        var result = _validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("SamplingRatio");
    }

    [Fact]
    public void Validate_InvalidSamplingRatio_OverOne_ShouldFail()
    {
        var options = new OpenTelemetryOptions { SamplingRatio = 1.5 };

        var result = _validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("SamplingRatio");
    }

    [Fact]
    public void Validate_ValidOtlpEndpoint_ShouldPass()
    {
        var options = new OpenTelemetryOptions { OtlpEndpoint = "http://localhost:4317" };

        var result = _validator.Validate(null, options);

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_InvalidOtlpEndpoint_NoScheme_ShouldFail()
    {
        var options = new OpenTelemetryOptions { OtlpEndpoint = "localhost:4317" };

        var result = _validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("OtlpEndpoint");
    }

    [Fact]
    public void Validate_OptionsWithFrameworkInstrumentation_ShouldPass()
    {
        var options = new OpenTelemetryOptions
        {
            InstrumentFramework = true,
            AdditionalActivitySources = new List<string> { "MyApp.Custom" },
            AdditionalMeters = new List<string> { "MyApp.Metrics" }
        };

        var result = _validator.Validate(null, options);

        result.Succeeded.ShouldBeTrue();
    }
}
