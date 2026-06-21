namespace Tnzi.Settings;

public class ConfigSectionResolverTests
{
    [ConfigSection("System:Encryption")] private sealed class WithAttr { }
    private sealed class FooOptions { }
    private sealed class Bar { }

    [Fact] public void Uses_attribute_when_present() => Assert.Equal("System:Encryption", ConfigSectionResolver.Resolve(typeof(WithAttr)));
    [Fact] public void Strips_Options_suffix() => Assert.Equal("Foo", ConfigSectionResolver.Resolve(typeof(FooOptions)));
    [Fact] public void Falls_back_to_type_name() => Assert.Equal("Bar", ConfigSectionResolver.Resolve(typeof(Bar)));
}
