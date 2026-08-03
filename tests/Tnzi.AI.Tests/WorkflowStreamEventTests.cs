using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Tnzi.AI.Tests;

/// <summary>
/// Workflow 流式事件测试
/// </summary>
public class WorkflowStreamEventTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public void WorkflowStreamEvent_SerializesStructuredFields()
    {
        var evt = new WorkflowStreamEventDto
        {
            EventType = "step",
            StepId = "draft",
            Status = "Step 'draft'",
            Output = "hello",
            StepResults =
            [
                new WorkflowStepResultDto
                {
                    StepId = "draft",
                    Output = "hello"
                }
            ]
        };

        var json = JsonSerializer.Serialize(evt, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<WorkflowStreamEventDto>(json, JsonOptions);

        json.ShouldContain("\"eventType\":\"step\"");
        json.ShouldContain("\"stepId\":\"draft\"");
        json.ShouldContain("\"stepResults\"");
        json.ShouldNotContain("\"errorMessage\"");
        deserialized.ShouldNotBeNull();
        deserialized.Status.ShouldBe("Step 'draft'");
        deserialized.StepResults.ShouldNotBeNull();
        deserialized.StepResults.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WriteEventAsync_GenericEvent_WritesNdjsonPayload()
    {
        var httpContext = new DefaultHttpContext();
        var responseStream = new MemoryStream();
        httpContext.Response.Body = responseStream;

        var evt = new WorkflowStreamEventDto
        {
            EventType = "completed",
            Status = "Completed",
            Output = "final",
            IsDone = true
        };

        await StreamingResponseWriter.WriteEventAsync(httpContext.Response, evt, StreamingFormat.NDJSON);

        responseStream.Position = 0;
        var body = await new StreamReader(responseStream).ReadToEndAsync();

        body.ShouldContain("\"eventType\":\"completed\"");
        body.ShouldContain("\"status\":\"Completed\"");
        body.ShouldContain("\"isDone\":true");
    }

    [Fact]
    public async Task WorkflowAdminController_RunStreaming_PreservesStructuredWorkflowEvent()
    {
        var workflowId = Guid.NewGuid();
        var mockService = new Mock<IWorkflowService>();
        mockService
            .Setup(s => s.RunStreamingAsync(workflowId, "input", null, It.IsAny<CancellationToken>()))
            .Returns(CreateResults());

        var controller = new TestWorkflowAdminController(mockService.Object);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Accept = "application/x-ndjson";
        httpContext.Response.Body = new MemoryStream();
        httpContext.RequestServices = new ServiceCollection().BuildServiceProvider();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        await controller.RunStreaming(workflowId, new RunWorkflowRequestDto { Input = "input" });

        httpContext.Response.Body.Position = 0;
        var body = await new StreamReader(httpContext.Response.Body).ReadToEndAsync();

        body.ShouldContain("\"eventType\":\"step\"");
        body.ShouldContain("\"eventType\":\"completed\"");
        body.ShouldContain("\"stepId\":\"draft\"");
        body.ShouldContain("\"stepResults\"");
        body.ShouldContain("\"status\":\"Completed\"");
        body.ShouldNotContain("\"delta\"");
    }

    [Fact]
    public async Task WorkflowAdminController_RunStreaming_AwaitingApproval_IsTerminalEvent()
    {
        var workflowId = Guid.NewGuid();
        var mockService = new Mock<IWorkflowService>();
        mockService
            .Setup(s => s.RunStreamingAsync(workflowId, "input", null, It.IsAny<CancellationToken>()))
            .Returns(CreateApprovalResults());

        var controller = new TestWorkflowAdminController(mockService.Object);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Accept = "application/x-ndjson";
        httpContext.Response.Body = new MemoryStream();
        httpContext.RequestServices = new ServiceCollection().BuildServiceProvider();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        await controller.RunStreaming(workflowId, new RunWorkflowRequestDto { Input = "input" });

        httpContext.Response.Body.Position = 0;
        var body = await new StreamReader(httpContext.Response.Body).ReadToEndAsync();

        body.ShouldContain("\"eventType\":\"completed\"");
        body.ShouldContain("\"status\":\"AwaitingApproval\"");
        body.ShouldContain("\"isDone\":true");
    }

    private static async IAsyncEnumerable<WorkflowExecutionResultDto> CreateResults()
    {
        yield return new WorkflowExecutionResultDto
        {
            Output = "draft output",
            Status = "Step 'draft'",
            StepResults =
            [
                new WorkflowStepResultDto
                {
                    StepId = "draft",
                    Output = "draft output"
                }
            ]
        };

        await Task.Yield();

        yield return new WorkflowExecutionResultDto
        {
            Output = "final output",
            Status = "Completed",
            StepResults =
            [
                new WorkflowStepResultDto
                {
                    StepId = "draft",
                    Output = "draft output"
                },
                new WorkflowStepResultDto
                {
                    StepId = "final",
                    Output = "final output"
                }
            ]
        };
    }

    private static async IAsyncEnumerable<WorkflowExecutionResultDto> CreateApprovalResults()
    {
        yield return new WorkflowExecutionResultDto
        {
            ExecutionId = "wf-awaiting-001",
            Output = "needs approval",
            Status = "AwaitingApproval",
            StepResults =
            [
                new WorkflowStepResultDto
                {
                    StepId = "approval-step",
                    Output = "needs approval"
                }
            ]
        };

        await Task.Yield();
    }

    private sealed class TestWorkflowAdminController : DefaultWorkflowAdminController
    {
        public TestWorkflowAdminController(IWorkflowService workflowService)
            : base(workflowService)
        {
        }
    }
}
