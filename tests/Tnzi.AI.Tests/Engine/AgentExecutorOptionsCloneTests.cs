using System.Reflection;
using Tnzi.AI.Tools.Models;

namespace Tnzi.AI.Tests.Engine;

/// <summary>
/// 守卫：<see cref="AgentExecutorOptions.Clone"/> 必须复制全部公共可写属性。
/// 历史 bug：WithAdditionalTools/WithFilteredTools 手抄字段清单克隆选项，漏掉
/// <c>Logger</c> 与 <c>StripTextFromToolCallMessages</c> —— 工具合并后的执行器静默丢失
/// DeepSeek 分词破碎修复与工具失败日志。现改为 MemberwiseClone；本测试以反射枚举
/// 全部可写属性，新增属性若未被测试赋非默认值会先失败，逼迫维护者确认克隆语义。
/// </summary>
public class AgentExecutorOptionsCloneTests
{
    [Fact]
    public void Clone_CopiesEveryWritableProperty()
    {
        var options = new AgentExecutorOptions
        {
            Name = "CloneGuard",
            Instructions = "test instructions",
            Tools = new List<AITool>(),
            Temperature = 0.7f,
            MaxOutputTokens = 123,
            MaxToolIterations = 7,
            ToolTimeoutSeconds = 42,
            HistoryReducer = new Mock<IHistoryReducer>().Object,
            Middlewares = new[] { new Mock<IToolExecutionMiddleware>().Object },
            Logger = new Mock<ILogger>().Object,
            ToolDefinitions = new Dictionary<string, ToolDefinition>(),
            StripTextFromToolCallMessages = true
        };

        var writableProps = typeof(AgentExecutorOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .ToList();

        // 自检：每个可写属性都必须被上面赋成非默认值 - 新增属性会让这里先红，
        // 强制把它纳入克隆守卫（MemberwiseClone 本身不会漏，但守卫要跟上）。
        var defaults = new AgentExecutorOptions();
        foreach (var prop in writableProps)
        {
            Equals(prop.GetValue(options), prop.GetValue(defaults)).ShouldBeFalse(
                $"Test setup must assign a non-default value to {prop.Name} so the clone guard covers it");
        }

        var clone = options.Clone();

        clone.ShouldNotBeSameAs(options);
        foreach (var prop in writableProps)
        {
            prop.GetValue(clone).ShouldBe(prop.GetValue(options),
                $"Clone must copy {prop.Name} - a dropped member silently changes executor behavior after tool merge");
        }
    }
}
