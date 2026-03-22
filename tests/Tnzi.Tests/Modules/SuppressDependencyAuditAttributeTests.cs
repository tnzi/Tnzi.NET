using Tnzi.Modules.Diagnostics;

namespace Tnzi.Tests.Modules;

public class SuppressDependencyAuditAttributeTests
{
    [Fact]
    public void Constructor_ShouldStoreReason()
    {
        var attr = new SuppressDependencyAuditAttribute("cross-module optional dependency");
        Assert.Equal("cross-module optional dependency", attr.Reason);
        Assert.Null(attr.IgnoredServiceType);
    }

    [Fact]
    public void Constructor_WithEmptyReason_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => new SuppressDependencyAuditAttribute(""));
    }

    [Fact]
    public void IgnoredServiceType_ShouldBeSettable()
    {
        var attr = new SuppressDependencyAuditAttribute("optional Redis dependency")
        {
            IgnoredServiceType = typeof(IDisposable)
        };
        Assert.Equal(typeof(IDisposable), attr.IgnoredServiceType);
    }
}
