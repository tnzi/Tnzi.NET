using Tnzi.AI.Agents.Definitions;

namespace Tnzi.AI.Tests.Agents;

public class YamlAgentDefinitionProviderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "tnzi-yaml-defs-" + Guid.NewGuid().ToString("N"));

    public YamlAgentDefinitionProviderTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private YamlAgentDefinitionProvider Build(bool watch = false) => new(
        new StaticOptionsMonitor<AIOptions>(new AIOptions
        {
            AgentDefinitions = new AgentDefinitionOptions { Enabled = true, DirectoryPath = _directory, WatchForChanges = watch }
        }),
        NullLogger<YamlAgentDefinitionProvider>.Instance);

    [Fact]
    public async Task LoadDefinitionsAsync_ParsesFields_AndStampsHash()
    {
        File.WriteAllText(Path.Combine(_directory, "a.yaml"), """
            name: researcher
            description: Finds things
            instructions: Search the web
            provider: OpenAI
            model: gpt-4o
            toolGroups: [web-search, file]
            temperature: 0.2
            maxTokens: 2048
            executionMode: 0
            domains: [research]
            qualityTier: 4
            isEnabled: false
            """);
        File.WriteAllText(Path.Combine(_directory, "notes.txt"), "ignored");
        using var provider = Build();

        var definitions = await provider.LoadDefinitionsAsync();

        var d = definitions.ShouldHaveSingleItem();
        d.Name.ShouldBe("researcher");
        d.Description.ShouldBe("Finds things");
        d.Instructions.ShouldBe("Search the web");
        d.Provider.ShouldBe("OpenAI");
        d.Model.ShouldBe("gpt-4o");
        d.ToolGroups.ShouldBe(["web-search", "file"]);
        d.Temperature.ShouldBe(0.2);
        d.MaxTokens.ShouldBe(2048);
        d.Domains.ShouldBe(["research"]);
        d.QualityTier.ShouldBe(4);
        d.IsEnabled.ShouldBeFalse();
        d.DefinitionHash.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task LoadDefinitionsAsync_InvalidOrNamelessFile_IsSkipped()
    {
        File.WriteAllText(Path.Combine(_directory, "bad.yaml"), "name: [unterminated");
        File.WriteAllText(Path.Combine(_directory, "nameless.yml"), "instructions: no name here");
        File.WriteAllText(Path.Combine(_directory, "good.yml"), "name: ok");
        using var provider = Build();

        var definitions = await provider.LoadDefinitionsAsync();

        definitions.Select(d => d.Name).ShouldBe(["ok"]);
    }

    [Fact]
    public async Task OnDefinitionsChanged_FiresWhenAWatchedFileChanges()
    {
        File.WriteAllText(Path.Combine(_directory, "a.yaml"), "name: a");
        using var provider = Build(watch: true);
        var fired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = provider.OnDefinitionsChanged(() => fired.TrySetResult());

        File.WriteAllText(Path.Combine(_directory, "a.yaml"), "name: a\ninstructions: changed");

        await fired.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task OnDefinitionsChanged_FiresWhenAnAtomicSaveRenamesOverTheWatchedFile()
    {
        // vim 默认与多数 IDE 的 safe-write：写临时文件再改名盖过原文件。临时名不是 .yaml（Created 被过滤），
        // 真正的文件只收到 Renamed —— 不订阅它，「热重载」就取决于用哪个编辑器，而且没有任何日志。
        File.WriteAllText(Path.Combine(_directory, "a.yaml"), "name: a");
        using var provider = Build(watch: true);
        var fired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = provider.OnDefinitionsChanged(() => fired.TrySetResult());

        // 目标名此前不存在：Windows 上盖过已有文件还会连带一个 Deleted，会把「有没有订阅 Renamed」遮住
        var temp = Path.Combine(_directory, "b.yaml.tmp~");
        File.WriteAllText(temp, "name: b\ninstructions: atomically saved");
        File.Move(temp, Path.Combine(_directory, "b.yaml"));

        await fired.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
