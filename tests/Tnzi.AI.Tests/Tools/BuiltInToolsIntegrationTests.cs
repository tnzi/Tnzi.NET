using System.Reflection;
using Tnzi.AI.Tools.Attributes;
using Tnzi.AI.WebSearch;
using BuiltInMemoryTools = Tnzi.AI.Tools.BuiltIn.MemoryTools;

namespace Tnzi.AI.Tests.Tools;

/// <summary>
/// 内置工具集成测试 — 验证所有 IAIToolProvider 工具可实例化且 AIFunction 定义有效
/// </summary>
public class BuiltInToolsIntegrationTests
{
    #region AIToolGroup 属性验证

    [Theory]
    [InlineData(typeof(DateTimeTools), "datetime")]
    [InlineData(typeof(TextTools), "text")]
    [InlineData(typeof(WebSearchTools), "websearch")]
    [InlineData(typeof(BuiltInMemoryTools), "memory")]
    [InlineData(typeof(A2ATools), "a2a")]
    public void BuiltInToolProvider_HasCorrectToolGroupAttribute(Type toolType, string expectedGroup)
    {
        var attr = toolType.GetCustomAttribute<AIToolGroupAttribute>();

        attr.ShouldNotBeNull($"{toolType.Name} should have [AIToolGroup] attribute");
        attr.GroupName.ShouldBe(expectedGroup);
    }

    #endregion

    #region AIFunction 方法验证

    [Theory]
    [InlineData(typeof(DateTimeTools), new[] { "get_current_datetime", "calculate_date_difference" })]
    [InlineData(typeof(TextTools), new[] { "text_statistics", "convert_case" })]
    [InlineData(typeof(WebSearchTools), new[] { "web_search" })]
    public void ToolProvider_HasExpectedAIFunctions(Type toolType, string[] expectedFunctions)
    {
        var methods = toolType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<AIFunctionAttribute>() != null)
            .Select(m => m.GetCustomAttribute<AIFunctionAttribute>()!.Name)
            .ToList();

        foreach (var expected in expectedFunctions)
        {
            methods.ShouldContain(expected, $"{toolType.Name} should have AIFunction '{expected}'");
        }
    }

    [Fact]
    public void AllAIFunctions_HaveDescriptions()
    {
        var toolTypes = new[]
        {
            typeof(DateTimeTools), typeof(TextTools), typeof(WebSearchTools),
            typeof(BuiltInMemoryTools), typeof(A2ATools)
        };

        foreach (var toolType in toolTypes)
        {
            var methods = toolType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<AIFunctionAttribute>() != null);

            foreach (var method in methods)
            {
                var attr = method.GetCustomAttribute<AIFunctionAttribute>()!;
                attr.Name.ShouldNotBeNullOrWhiteSpace($"{toolType.Name}.{method.Name} AIFunction name should not be empty");
            }
        }
    }

    #endregion

    #region 工具实例化

    [Fact]
    public void DateTimeTools_CanBeInstantiated()
    {
        var tools = new DateTimeTools(NullLogger<DateTimeTools>.Instance);
        tools.ShouldNotBeNull();
    }

    [Fact]
    public void TextTools_CanBeInstantiated()
    {
        var tools = new TextTools(NullLogger<TextTools>.Instance);
        tools.ShouldNotBeNull();
    }

    [Fact]
    public void WebSearchTools_CanBeInstantiated_WithoutProvider()
    {
        var tools = new WebSearchTools(NullLogger<WebSearchTools>.Instance);
        tools.ShouldNotBeNull();
    }

    [Fact]
    public void WebSearchTools_CanBeInstantiated_WithProvider()
    {
        var tools = new WebSearchTools(NullLogger<WebSearchTools>.Instance, Mock.Of<IWebSearchProvider>());
        tools.ShouldNotBeNull();
    }

    [Fact]
    public void MemoryTools_CanBeInstantiated()
    {
        var mockStore = new Mock<IMemoryStore>();
        var options = new StaticOptionsMonitor<AIOptions>(new AIOptions());
        var tools = new BuiltInMemoryTools(mockStore.Object, NullLogger<BuiltInMemoryTools>.Instance, options);
        tools.ShouldNotBeNull();
    }

    [Fact]
    public void A2ATools_CanBeInstantiated_WithoutClient()
    {
        var tools = new A2ATools(NullLogger<A2ATools>.Instance);
        tools.ShouldNotBeNull();
    }

    [Fact]
    public void ClarificationTools_CanBeInstantiated()
    {
        var tools = new ClarificationTools(new AgentExecutionContextAccessor());
        tools.ShouldNotBeNull();
    }

    [Fact]
    public void TodoTools_CanBeInstantiated()
    {
        var tools = new TodoTools(new AgentExecutionContextAccessor());
        tools.ShouldNotBeNull();
    }

    #endregion

    #region 工具构造函数空值验证

    [Fact]
    public void DateTimeTools_NullLogger_Throws()
    {
        Should.Throw<ArgumentNullException>(() => new DateTimeTools(null!));
    }

    [Fact]
    public void TextTools_NullLogger_Throws()
    {
        Should.Throw<ArgumentNullException>(() => new TextTools(null!));
    }

    [Fact]
    public void WebSearchTools_NullLogger_Throws()
    {
        Should.Throw<ArgumentNullException>(() => new WebSearchTools(null!));
    }

    #endregion
}
