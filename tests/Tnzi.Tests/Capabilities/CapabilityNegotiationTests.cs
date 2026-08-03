using Tnzi.Capabilities;

namespace Tnzi.Tests.Capabilities;

public class CapabilityNegotiationTests
{
    [Theory]
    [InlineData("chat-draft-restore-v1")]
    [InlineData("rpc-v1")]
    [InlineData("streaming-frames-v12")]
    public void IsValidName_AcceptsKebabCaseWithVersionSuffix(string name)
    {
        Assert.True(TnziCapabilities.IsValidName(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-version-suffix")]          // the suffix is the whole reason names never change meaning
    [InlineData("Chat-Draft-Restore-v1")]      // a casing variant would silently be a different capability
    [InlineData("chat_draft_restore_v1")]
    [InlineData("chat-draft-restore-v0")]      // versions start at 1; v0 reads as "unversioned"
    [InlineData("-leading-dash-v1")]
    [InlineData("trailing-dash--v1")]
    public void IsValidName_RejectsMalformedNames(string name)
    {
        Assert.False(TnziCapabilities.IsValidName(name));
    }

    [Fact]
    public void Declare_RejectsMalformedName()
    {
        var catalog = new CapabilityCatalog();

        // Failing at startup beats shipping a capability nobody can ever match: at runtime a typo
        // is indistinguishable from "no client supports it yet".
        Assert.Throws<ArgumentException>(() => catalog.Declare("NotAValidName"));
    }

    [Fact]
    public void Declare_IsIdempotentAndSorted()
    {
        var catalog = new CapabilityCatalog();
        catalog.Declare("zebra-v1");
        catalog.Declare("alpha-v1");
        catalog.Declare("zebra-v1");

        Assert.Equal(["alpha-v1", "zebra-v1"], catalog.ServerCapabilities);
    }

    [Fact]
    public void ClientCapabilities_ParsesCommaSeparatedHeader()
    {
        var client = new ClientCapabilities("alpha-v1, beta-v2,gamma-v1");

        Assert.True(client.Supports("alpha-v1"));
        Assert.True(client.Supports("beta-v2"));
        Assert.True(client.Supports("gamma-v1"));
    }

    [Fact]
    public void ClientCapabilities_DropsMalformedEntriesButKeepsTheRest()
    {
        var client = new ClientCapabilities("alpha-v1, NOT VALID, beta-v1");

        // One bad name should cost that one capability, not fail the whole request - and since an
        // unrecognised capability already degrades to the older path, dropping is the safe way.
        Assert.True(client.Supports("alpha-v1"));
        Assert.True(client.Supports("beta-v1"));
        Assert.Equal(2, client.Declared.Count);
    }

    [Fact]
    public void ClientCapabilities_TreatsSilenceAsNo()
    {
        Assert.False(ClientCapabilities.None.Supports("alpha-v1"));
        Assert.False(new ClientCapabilities(null).Supports("alpha-v1"));
        Assert.Empty(new ClientCapabilities("").Declared);
    }

    [Fact]
    public void DeclaredCapability_IsReadableFromTheBuiltProvider()
    {
        var services = new ServiceCollection();

        // Mirrors real startup: the framework registers the catalog, a module declares into it
        // afterwards, and only later does a provider exist to resolve it from. If registration
        // were by type instead of by instance, the declaration would land on a throwaway object
        // and the endpoint would report an empty list - with nothing failing anywhere.
        services.AddTnziCapabilities();
        services.DeclareCapability("alpha-v1");

        var resolved = services.BuildServiceProvider().GetRequiredService<ICapabilityCatalog>();

        Assert.Equal(["alpha-v1"], resolved.ServerCapabilities);
    }

    [Fact]
    public void DeclaringWithoutPriorRegistration_StillReachesTheProvider()
    {
        var services = new ServiceCollection();

        // Module load order must not matter: a module that declares before the framework
        // registered anything has to end up in the same catalog.
        services.DeclareCapability("alpha-v1");
        services.AddTnziCapabilities();
        services.DeclareCapability("beta-v1");

        var resolved = services.BuildServiceProvider().GetRequiredService<ICapabilityCatalog>();

        Assert.Equal(["alpha-v1", "beta-v1"], resolved.ServerCapabilities);
    }

    [Fact]
    public void AddTnziCapabilities_IsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddTnziCapabilities();
        services.AddTnziCapabilities();

        Assert.Single(services, d => d.ServiceType == typeof(ICapabilityCatalog));
    }

    [Fact]
    public void ServerDeclaration_DoesNotImplyClientSupport()
    {
        var catalog = new CapabilityCatalog();
        catalog.Declare("alpha-v1");

        var client = new ClientCapabilities(headerValue: null);

        // The exact failure this mechanism exists to prevent: the server is upgraded first,
        // concludes "I support it, so we can use it", and breaks the clients that lag behind.
        Assert.True(catalog.IsDeclared("alpha-v1"));
        Assert.False(client.Supports("alpha-v1"));
    }
}
