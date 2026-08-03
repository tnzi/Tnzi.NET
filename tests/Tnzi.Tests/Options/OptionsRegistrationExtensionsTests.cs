using Microsoft.Extensions.Configuration;
using Tnzi.Options;
using Tnzi.Settings;

public class OptionsRegistrationExtensionsTests
{
    [ConfigSection("Demo")] private sealed class DemoOptions { public string Name { get; set; } = ""; }

    [Fact]
    public void Binds_section_resolved_from_attribute()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Demo:Name"] = "bound" })
            .Build();
        var services = new ServiceCollection();
        services.AddTnziOptions<DemoOptions>(config);
        var sp = services.BuildServiceProvider();
        Assert.Equal("bound", sp.GetRequiredService<IOptions<DemoOptions>>().Value.Name);
    }

    [Fact]
    public void Binds_explicit_section_override()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Custom:Name"] = "x" })
            .Build();
        var services = new ServiceCollection();
        services.AddTnziOptions<DemoOptions>(config, "Custom");
        var sp = services.BuildServiceProvider();
        Assert.Equal("x", sp.GetRequiredService<IOptions<DemoOptions>>().Value.Name);
    }
}
