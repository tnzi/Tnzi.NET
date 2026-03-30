namespace Tnzi.AI.Tests;

public class TodoToolsTests
{
    [Fact]
    public void WriteTodos_StoresInAccessorProperties()
    {
        var accessor = new AgentExecutionContextAccessor();
        var tools = new TodoTools(accessor);

        var items = new List<TodoItemDto>
        {
            new() { Content = "Write tests", Status = TodoStatus.Pending, Order = 1 },
            new() { Content = "Implement", Status = TodoStatus.Pending, Order = 2 }
        };

        var result = tools.WriteTodos(items);

        Assert.Contains("2 todo", result, StringComparison.OrdinalIgnoreCase);
        Assert.True(accessor.Properties.ContainsKey("Todos"));
        var stored = accessor.Properties["Todos"] as List<TodoItemDto>;
        Assert.NotNull(stored);
        Assert.Equal(2, stored.Count);
    }

    [Fact]
    public void WriteTodos_EmptyList_ReturnsMessage()
    {
        var accessor = new AgentExecutionContextAccessor();
        var tools = new TodoTools(accessor);

        var result = tools.WriteTodos([]);

        Assert.Contains("cleared", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WriteTodos_WithCompletedItems_ShowsProgress()
    {
        var accessor = new AgentExecutionContextAccessor();
        var tools = new TodoTools(accessor);

        var items = new List<TodoItemDto>
        {
            new() { Content = "Task A", Status = TodoStatus.Completed, Order = 1 },
            new() { Content = "Task B", Status = TodoStatus.InProgress, Order = 2 },
            new() { Content = "Task C", Status = TodoStatus.Pending, Order = 3 }
        };

        var result = tools.WriteTodos(items);

        Assert.Contains("1/3 completed", result);
    }

    [Fact]
    public void WriteTodos_NullList_ClearsTodos()
    {
        var accessor = new AgentExecutionContextAccessor();
        var tools = new TodoTools(accessor);

        var result = tools.WriteTodos(null!);

        Assert.Contains("cleared", result, StringComparison.OrdinalIgnoreCase);
    }
}
