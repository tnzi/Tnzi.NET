namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 停下单个任务（子 agent、后台命令）：请求落在运行上（任一副本），执行它的副本把 <c>stop_task</c> 写给 CLI，每个任务一次；
/// CLI 答不了时留一条警告日志事件。
/// </summary>
public class CliTaskStopTests : CliChatTurnTestBase
{
    private async Task<CliRun> SeedRunningRunAsync()
    {
        var (threadId, userMessageId) = await SeedThreadWithQuestionAsync("start two agents");
        var run = await SeedClaimedRunAsync(threadId, userMessageId);
        await DbContext.Set<CliRun>().IgnoreQueryFilters().Where(r => r.Id == run.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, CliRunStatus.Running));
        return run;
    }

    [Fact]
    public async Task AStopRequest_IsRecordedOnTheRun_AndWrittenToTheCliOnceEach()
    {
        var run = await SeedRunningRunAsync();
        var dispatcher = CreateDispatcher();

        (await dispatcher.StopTaskAsync(run.Id, "a9d5c37c9e58a18c8")).Succeeded.ShouldBeTrue();
        (await dispatcher.StopTaskAsync(run.Id, "b65t6oegl")).Succeeded.ShouldBeTrue();
        (await ReloadRunAsync(run.Id)).StopTaskRequests.ShouldBe("a9d5c37c9e58a18c8\nb65t6oegl\n");

        var control = new RecordingTaskControl();
        var stops = new CliRunTaskStops(run.Id, control, ServiceProvider.GetRequiredService<IServiceScopeFactory>(), new CliRunSignalHub(),
            TimeSpan.FromMilliseconds(50), NullLogger.Instance);

        (await stops.DeliverNewAsync(CancellationToken.None)).ShouldBe(["a9d5c37c9e58a18c8", "b65t6oegl"]);
        (await stops.DeliverNewAsync(CancellationToken.None)).ShouldBeEmpty("each task is asked to stop once");

        (await dispatcher.StopTaskAsync(run.Id, "c1")).Succeeded.ShouldBeTrue();
        (await stops.DeliverNewAsync(CancellationToken.None)).ShouldBe(["c1"]);
        control.Stopped.ShouldBe(["a9d5c37c9e58a18c8", "b65t6oegl", "c1"]);
    }

    [Fact]
    public async Task WhenTheProcessNoLongerReads_TheRequestWaitsForTheNextAttempt()
    {
        var run = await SeedRunningRunAsync();
        await CreateDispatcher().StopTaskAsync(run.Id, "t1");
        var control = new RecordingTaskControl { Accepts = false };
        var stops = new CliRunTaskStops(run.Id, control, ServiceProvider.GetRequiredService<IServiceScopeFactory>(), new CliRunSignalHub(),
            TimeSpan.FromMilliseconds(50), NullLogger.Instance);

        (await stops.DeliverNewAsync(CancellationToken.None)).ShouldBeEmpty();

        control.Accepts = true;
        (await stops.DeliverNewAsync(CancellationToken.None)).ShouldBe(["t1"], "it was not marked as sent");
    }

    [Theory]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("x\"}]")]
    [InlineData("toolong-toolong-toolong-toolong-toolong-toolong-toolong-toolong-toolong-toolong-toolong-toolong-toolong-toolong-toolong-toolong-x")]
    public async Task ATaskIdTheCliCouldNotHaveGiven_IsRefused(string taskId)
    {
        var run = await SeedRunningRunAsync();

        var result = await CreateDispatcher().StopTaskAsync(run.Id, taskId);

        result.Code.ShouldBe(400);
        result.ErrorCode.ShouldBe(ErrorCodes.CliTaskIdInvalid);
        (await ReloadRunAsync(run.Id)).StopTaskRequests.ShouldBeNull();
    }

    [Fact]
    public async Task ARunThatIsNotRunning_HasNoTaskToStop()
    {
        var run = await SeedRunningRunAsync();
        await DbContext.Set<CliRun>().IgnoreQueryFilters().Where(r => r.Id == run.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, CliRunStatus.Completed));

        var result = await CreateDispatcher().StopTaskAsync(run.Id, "t1");

        result.Code.ShouldBe(409);
        (await ReloadRunAsync(run.Id)).StopTaskRequests.ShouldBeNull();
    }

    [Fact]
    public async Task TheAdapter_WritesStopTask_AndReportsWhatTheCliCouldNotStop()
    {
        var adapter = new StreamJsonAdapter(NullLogger<StreamJsonAdapter>.Instance);
        var transport = new FakeCliAgentTransport(
            ["""{"type":"system","subtype":"init","session_id":"s"}"""],
            onWrite: line =>
            {
                if (!line.Contains("\"stop_task\""))
                {
                    return [];
                }

                using var frame = JsonDocument.Parse(line);
                var requestId = frame.RootElement.GetProperty("request_id").GetString();
                return
                [
                    $$$"""{"type":"control_response","response":{"subtype":"error","request_id":"{{{requestId}}}","error":"No task found with id: t-gone"}}""",
                    """{"type":"result","subtype":"success","is_error":false,"result":"done","session_id":"s","num_turns":1}"""
                ];
            });
        var context = new CliAgentLaunchContext
        {
            Provider = CliBuiltInProviders.All["claude"],
            ExecutablePath = "/nonexistent/claude",
            Prompt = "hello",
            WorkingDirectory = "/tmp/does-not-need-to-exist"
        };

        var events = new List<CliAgentEvent>();
        var reading = Task.Run(async () =>
        {
            await foreach (var evt in adapter.RunAsync(transport, context, CancellationToken.None))
            {
                events.Add(evt);
            }
        });
        await WaitUntilAsync(() => events.Count > 0);

        (await adapter.StopTaskAsync("t-gone", CancellationToken.None)).ShouldBeTrue();
        await reading.WaitAsync(TestTimeout);

        using (var written = JsonDocument.Parse(transport.Written.Single(l => l.Contains("\"stop_task\""))))
        {
            written.RootElement.GetProperty("type").GetString().ShouldBe("control_request");
            var request = written.RootElement.GetProperty("request");
            request.GetProperty("subtype").GetString().ShouldBe("stop_task");
            request.GetProperty("task_id").GetString().ShouldBe("t-gone");
        }

        var warning = events.Single(e => e.Type == CliAgentEventType.Log && e.Level == "warning");
        warning.Content.ShouldBe("The CLI could not stop task t-gone: No task found with id: t-gone");
        (await adapter.StopTaskAsync("t-late", CancellationToken.None)).ShouldBeFalse("the process no longer reads after the last result");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        condition().ShouldBeTrue();
    }

    private sealed class RecordingTaskControl : ICliTaskControl
    {
        public List<string> Stopped { get; } = [];

        public bool Accepts { get; set; } = true;

        public Task<bool> StopTaskAsync(string taskId, CancellationToken cancellationToken)
        {
            if (Accepts)
            {
                Stopped.Add(taskId);
            }

            return Task.FromResult(Accepts);
        }
    }
}
