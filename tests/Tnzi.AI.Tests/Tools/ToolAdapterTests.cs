using Tnzi.AI.Tools.Attributes;

namespace Tnzi.AI.Tests.Tools;

/// <summary>
/// <see cref="ToolAdapter"/> 必须把 <c>ToolDefinition</c> 上的安全元数据带到 <c>AIFunction.AdditionalProperties</c>，
/// 否则权限评估器看不到 <c>[AIFunction(IsDestructive = true)]</c>。
/// </summary>
public class ToolAdapterTests
{
    [Fact]
    public void ConvertToAITools_CopiesIsDestructiveAndReadOnlyIntoAdditionalProperties()
    {
        var scanner = new ToolScanner(NullLogger<ToolScanner>.Instance);
        var definitions = scanner.ScanAssembly(typeof(MetadataTools).Assembly)
            .Where(t => t.ProviderType == typeof(MetadataTools))
            .ToList();
        var services = new ServiceCollection().AddSingleton<MetadataTools>().BuildServiceProvider();

        var functions = ToolAdapter.ConvertToAITools(definitions, services).Cast<AIFunction>().ToList();

        var destructive = functions.Single(f => f.Name == "wipe");
        destructive.AdditionalProperties[ToolMetadataKeys.Destructive].ShouldBe(true);
        destructive.AdditionalProperties[ToolMetadataKeys.ReadOnly].ShouldBe(false);

        var safe = functions.Single(f => f.Name == "peek");
        safe.AdditionalProperties[ToolMetadataKeys.Destructive].ShouldBe(false);
        safe.AdditionalProperties[ToolMetadataKeys.ReadOnly].ShouldBe(true);
    }

    [AIToolGroup("adapter-metadata", "Adapter Metadata Tools", "Tools for testing adapter metadata")]
    private class MetadataTools : IAIToolProvider
    {
        [AIFunction("wipe", "Wipes things", IsDestructive = true)]
        public Task<string> WipeAsync() => Task.FromResult("gone");

        [AIFunction("peek", "Reads things", IsReadOnly = true)]
        public Task<string> PeekAsync() => Task.FromResult("seen");
    }
}
