namespace Tnzi.AI.Tests;

/// <summary>
/// ChatHistoryHelper 单元测试 — GroupMessagesByTurns / BuildCallIdToToolNameMap / FormatMessageText
/// </summary>
public class ChatHistoryHelperTests
{
    #region GroupMessagesByTurns

    [Fact]
    public void GroupMessagesByTurns_EmptyHistory_ReturnsEmptyList()
    {
        var turns = ChatHistoryHelper.GroupMessagesByTurns([]);
        turns.ShouldBeEmpty();
    }

    [Fact]
    public void GroupMessagesByTurns_SingleUserMessage_ReturnsSingleTurn()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello")
        };

        var turns = ChatHistoryHelper.GroupMessagesByTurns(messages);

        turns.Count.ShouldBe(1);
        turns[0].Count.ShouldBe(1);
        turns[0][0].Role.ShouldBe(ChatRole.User);
    }

    [Fact]
    public void GroupMessagesByTurns_UserThenAssistant_GroupedInOneTurn()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello"),
            new(ChatRole.Assistant, "Hi there!")
        };

        var turns = ChatHistoryHelper.GroupMessagesByTurns(messages);

        turns.Count.ShouldBe(1);
        turns[0].Count.ShouldBe(2);
        turns[0][0].Role.ShouldBe(ChatRole.User);
        turns[0][1].Role.ShouldBe(ChatRole.Assistant);
    }

    [Fact]
    public void GroupMessagesByTurns_MultipleTurns_EachTurnStartsWithUserMessage()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Question 1"),
            new(ChatRole.Assistant, "Answer 1"),
            new(ChatRole.User, "Question 2"),
            new(ChatRole.Assistant, "Answer 2"),
            new(ChatRole.User, "Question 3")
        };

        var turns = ChatHistoryHelper.GroupMessagesByTurns(messages);

        turns.Count.ShouldBe(3);
        turns[0][0].Text.ShouldBe("Question 1");
        turns[0][1].Text.ShouldBe("Answer 1");
        turns[1][0].Text.ShouldBe("Question 2");
        turns[1][1].Text.ShouldBe("Answer 2");
        turns[2][0].Text.ShouldBe("Question 3");
    }

    [Fact]
    public void GroupMessagesByTurns_AssistantMessageBeforeUser_CreatesOwnTurn()
    {
        // Unusual case: assistant/tool message appears before any user message
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, "Unprompted message"),
            new(ChatRole.User, "First user turn"),
            new(ChatRole.Assistant, "Response")
        };

        var turns = ChatHistoryHelper.GroupMessagesByTurns(messages);

        turns.Count.ShouldBe(2);
        turns[0].Count.ShouldBe(1);
        turns[0][0].Role.ShouldBe(ChatRole.Assistant);
        turns[1].Count.ShouldBe(2);
        turns[1][0].Role.ShouldBe(ChatRole.User);
    }

    [Fact]
    public void GroupMessagesByTurns_TurnWithToolCall_GroupedWithUserTurn()
    {
        var toolCallContent = new FunctionCallContent("call-1", "my_tool");
        var toolResultContent = new FunctionResultContent("call-1", "tool output");

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Use the tool"),
            new(ChatRole.Assistant, [toolCallContent]),
            new(ChatRole.Tool, [toolResultContent]),
            new(ChatRole.Assistant, "Done!")
        };

        var turns = ChatHistoryHelper.GroupMessagesByTurns(messages);

        turns.Count.ShouldBe(1);
        turns[0].Count.ShouldBe(4);
    }

    [Fact]
    public void GroupMessagesByTurns_SystemMessageOnly_SystemBecomesSeparateTurn()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are helpful")
        };

        var turns = ChatHistoryHelper.GroupMessagesByTurns(messages);

        // System messages are not user messages, so they get their own turn
        turns.Count.ShouldBe(1);
        turns[0][0].Role.ShouldBe(ChatRole.System);
    }

    #endregion

    #region BuildCallIdToToolNameMap

    [Fact]
    public void BuildCallIdToToolNameMap_EmptyMessages_ReturnsEmptyMap()
    {
        var map = ChatHistoryHelper.BuildCallIdToToolNameMap([]);
        map.ShouldBeEmpty();
    }

    [Fact]
    public void BuildCallIdToToolNameMap_AssistantWithFunctionCall_BuildsMapping()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Call a tool"),
            new(ChatRole.Assistant, [new FunctionCallContent("call-abc", "my_function")])
        };

        var map = ChatHistoryHelper.BuildCallIdToToolNameMap(messages);

        map.Count.ShouldBe(1);
        map["call-abc"].ShouldBe("my_function");
    }

    [Fact]
    public void BuildCallIdToToolNameMap_MultipleFunctionCalls_BuildsAllMappings()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, [
                new FunctionCallContent("call-1", "tool_a"),
                new FunctionCallContent("call-2", "tool_b")
            ])
        };

        var map = ChatHistoryHelper.BuildCallIdToToolNameMap(messages);

        map.Count.ShouldBe(2);
        map["call-1"].ShouldBe("tool_a");
        map["call-2"].ShouldBe("tool_b");
    }

    [Fact]
    public void BuildCallIdToToolNameMap_NonAssistantMessages_Ignored()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, [new FunctionCallContent("call-user", "user_tool")]),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", "result")])
        };

        var map = ChatHistoryHelper.BuildCallIdToToolNameMap(messages);

        // Only assistant messages are scanned
        map.ShouldBeEmpty();
    }

    [Fact]
    public void BuildCallIdToToolNameMap_OnlyAssistantMessagesScanned_OtherRolesIgnored()
    {
        // FunctionCallContent on a system or user message should not be added to map
        var call = new FunctionCallContent("call-sys", "system_tool");
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, [call])  // system role → should be ignored
        };

        var map = ChatHistoryHelper.BuildCallIdToToolNameMap(messages);

        map.ShouldBeEmpty();
    }

    #endregion

    #region FormatMessageText

    [Fact]
    public void FormatMessageText_PlainTextMessage_ReturnsText()
    {
        var message = new ChatMessage(ChatRole.User, "Hello world");

        var result = ChatHistoryHelper.FormatMessageText(message);

        result.ShouldBe("Hello world");
    }

    [Fact]
    public void FormatMessageText_FunctionCallContent_FormatsTool()
    {
        var call = new FunctionCallContent("call-1", "my_function", new Dictionary<string, object?> { ["param1"] = "value1" });
        var message = new ChatMessage(ChatRole.Assistant, [call]);

        var result = ChatHistoryHelper.FormatMessageText(message);

        result.ShouldContain("[Tool Call: my_function");
        result.ShouldContain("param1");
    }

    [Fact]
    public void FormatMessageText_FunctionCallNoArguments_FormatsWithoutArgs()
    {
        var call = new FunctionCallContent("call-1", "no_arg_tool");
        var message = new ChatMessage(ChatRole.Assistant, [call]);

        var result = ChatHistoryHelper.FormatMessageText(message);

        result.ShouldContain("[Tool Call: no_arg_tool]");
    }

    [Fact]
    public void FormatMessageText_FunctionResultContent_FormatsResult()
    {
        var result = new FunctionResultContent("call-1", "tool output text");
        var message = new ChatMessage(ChatRole.Tool, [result]);

        var formatted = ChatHistoryHelper.FormatMessageText(message);

        formatted.ShouldContain("[Tool Result: tool output text]");
    }

    [Fact]
    public void FormatMessageText_FunctionResultContentEmptyResult_ShowsEmpty()
    {
        var result = new FunctionResultContent("call-1", result: null);
        var message = new ChatMessage(ChatRole.Tool, [result]);

        var formatted = ChatHistoryHelper.FormatMessageText(message);

        formatted.ShouldContain("[Tool Result: (empty)]");
    }

    [Fact]
    public void FormatMessageText_FunctionResultLongText_TruncatesAt500Chars()
    {
        var longText = new string('x', 600);
        var result = new FunctionResultContent("call-1", longText);
        var message = new ChatMessage(ChatRole.Tool, [result]);

        var formatted = ChatHistoryHelper.FormatMessageText(message);

        formatted.ShouldContain("...(truncated)");
        // "[Tool Result: " + 500 chars + "...(truncated)]"
        var prefix = "[Tool Result: ";
        var suffix = "...(truncated)]";
        var innerLength = formatted.Length - prefix.Length - suffix.Length;
        innerLength.ShouldBe(500);
    }

    [Fact]
    public void FormatMessageText_MixedTextAndFunctionCall_IncludesBoth()
    {
        var textContent = new TextContent("Some text before tool call. ");
        var call = new FunctionCallContent("call-2", "another_tool");
        var message = new ChatMessage(ChatRole.Assistant, [textContent, call]);

        var result = ChatHistoryHelper.FormatMessageText(message);

        result.ShouldContain("Some text before tool call.");
        result.ShouldContain("[Tool Call: another_tool]");
    }

    [Fact]
    public void FormatMessageText_EmptyTextMessage_ReturnsNonTextContent()
    {
        // Message with no contents should return [non-text content]
        var message = new ChatMessage(ChatRole.Assistant, []);

        var result = ChatHistoryHelper.FormatMessageText(message);

        result.ShouldBe("[non-text content]");
    }

    #endregion
}
