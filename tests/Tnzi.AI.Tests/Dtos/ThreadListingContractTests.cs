namespace Tnzi.AI.Tests.Dtos;

/// <summary>
/// Only server code decides whether a thread is kept out of its owner's lists: no request body carries it, and the column
/// an upgrade adds leaves every existing thread listed.
/// </summary>
public class ThreadListingContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("""{"title":"t","listing":"Never"}""")]
    [InlineData("""{"title":"t","listing":2}""")]
    [InlineData("""{"title":"t","Listing":"UntilUserMessage"}""")]
    public void ACreateRequestBody_CannotMakeAThreadUnlisted(string body)
    {
        var dto = JsonSerializer.Deserialize<CreateAgentThreadDto>(body, Web).ShouldNotBeNull();

        dto.Listing.ShouldBe(ThreadListing.Listed);
    }

    [Fact]
    public void AListRequestBody_CannotAskForUnlistedThreads()
    {
        var dto = JsonSerializer.Deserialize<ThreadListQueryDto>("""{"includeUnlisted":true,"IncludeUnlisted":true}""", Web).ShouldNotBeNull();

        dto.IncludeUnlisted.ShouldBeFalse();
    }

    [Fact]
    public void TheChatRequest_HasNoListing()
    {
        // The chat endpoint's body and the queue's: neither may carry a listing (the turn's thread is listed as any user's thread)
        typeof(ChatRequestDto).GetProperties().ShouldNotContain(p => p.Name.Contains("Listing", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Unlisted", StringComparison.OrdinalIgnoreCase));
        typeof(QueueMessageRequestDto).GetProperties().ShouldNotContain(p => p.Name.Contains("Listing", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Unlisted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExistingThreads_MigrateAsListed()
    {
        // The column is a non-null int with no default of its own: a migration adds it with the CLR default 0 for existing rows,
        // and 0 is Listed. A new member must never take 0, and Listed must stay 0.
        ((int)ThreadListing.Listed).ShouldBe(0);
        Enum.GetValues<ThreadListing>().Count(v => (int)v == 0).ShouldBe(1);
        new AgentThread().Listing.ShouldBe(ThreadListing.Listed);
        new CreateAgentThreadDto().Listing.ShouldBe(ThreadListing.Listed);
        new AgentRunRequest().ThreadListing.ShouldBe(ThreadListing.Listed);
        new AiClientOptions().ThreadListing.ShouldBe(ThreadListing.Listed);
    }
}
