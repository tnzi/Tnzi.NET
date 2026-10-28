namespace Tnzi.AI.Guardrails;

/// <summary>
/// Guardrail 执行器 - 统一运行所有注册的输入/输出 Guardrails，支持顺序和并行执行模式
/// </summary>
public class GuardrailRunner
{
    private readonly IEnumerable<IInputGuardrail> _inputGuardrails;
    private readonly IEnumerable<IOutputGuardrail> _outputGuardrails;
    private readonly IOptionsMonitor<AIOptions> _options;
    private readonly ILogger<GuardrailRunner> _logger;

    public GuardrailRunner(
        IEnumerable<IInputGuardrail> inputGuardrails,
        IEnumerable<IOutputGuardrail> outputGuardrails,
        IOptionsMonitor<AIOptions> options,
        ILogger<GuardrailRunner> logger)
    {
        _inputGuardrails = Check.NotNull(inputGuardrails);
        _outputGuardrails = Check.NotNull(outputGuardrails);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 运行所有输入 Guardrails
    /// </summary>
    /// <param name="input">用户输入文本</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>最终结果：通过时返回（可能已修改的）文本，拒绝时返回 GuardrailResult</returns>
    public async Task<(string text, GuardrailResult? rejection)> RunInputGuardrailsAsync(string input, CancellationToken ct = default)
    {
        if (!_options.CurrentValue.Guardrails.Enabled)
        {
            return (input, null);
        }

        if (_options.CurrentValue.Guardrails.ExecutionMode == GuardrailExecutionMode.Parallel)
        {
            return await RunInputGuardrailsParallelAsync(input, ct);
        }

        return await RunInputGuardrailsSequentialAsync(input, ct);
    }

    /// <summary>
    /// 运行所有输出 Guardrails
    /// </summary>
    /// <param name="output">AI 响应文本</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>最终结果：通过时返回（可能已修改的）文本，拒绝时返回 GuardrailResult</returns>
    public async Task<(string text, GuardrailResult? rejection)> RunOutputGuardrailsAsync(string output, CancellationToken ct = default)
    {
        if (!_options.CurrentValue.Guardrails.Enabled)
        {
            return (output, null);
        }

        if (_options.CurrentValue.Guardrails.ExecutionMode == GuardrailExecutionMode.Parallel)
        {
            return await RunOutputGuardrailsParallelAsync(output, ct);
        }

        return await RunOutputGuardrailsSequentialAsync(output, ct);
    }

    /// <summary>
    /// 顺序执行输入 Guardrails - 遇到第一个拒绝即停止
    /// </summary>
    private async Task<(string text, GuardrailResult? rejection)> RunInputGuardrailsSequentialAsync(string input, CancellationToken ct)
    {
        var currentText = input;

        foreach (var guardrail in _inputGuardrails)
        {
            var result = await guardrail.ValidateAsync(currentText, ct);

            if (!result.IsAllowed)
            {
                _logger.LogWarning("Input blocked by {GuardrailName}: {Reason}",
                    result.GuardrailName, result.Reason);
                return (currentText, result);
            }

            if (result.ModifiedContent != null)
            {
                _logger.LogDebug("Input modified by {GuardrailName}: {Reason}",
                    result.GuardrailName, result.Reason);
                currentText = result.ModifiedContent;
            }
        }

        return (currentText, null);
    }

    /// <summary>
    /// 顺序执行输出 Guardrails - 遇到第一个拒绝即停止
    /// </summary>
    private async Task<(string text, GuardrailResult? rejection)> RunOutputGuardrailsSequentialAsync(string output, CancellationToken ct)
    {
        var currentText = output;

        foreach (var guardrail in _outputGuardrails)
        {
            var result = await guardrail.ValidateAsync(currentText, ct);

            if (!result.IsAllowed)
            {
                _logger.LogWarning("Output blocked by {GuardrailName}: {Reason}",
                    result.GuardrailName, result.Reason);
                return (currentText, result);
            }

            if (result.ModifiedContent != null)
            {
                _logger.LogDebug("Output modified by {GuardrailName}: {Reason}",
                    result.GuardrailName, result.Reason);
                currentText = result.ModifiedContent;
            }
        }

        return (currentText, null);
    }

    /// <summary>
    /// 并行执行输入 Guardrails - 所有 guardrail 同时执行，支持 tripwire 立即中止
    /// </summary>
    private async Task<(string text, GuardrailResult? rejection)> RunInputGuardrailsParallelAsync(string input, CancellationToken ct)
    {
        var guardrailList = _inputGuardrails.ToList();
        if (guardrailList.Count == 0)
        {
            return (input, null);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var results = new ConcurrentBag<(int index, GuardrailResult result)>();
        GuardrailResult? tripwireResult = null;

        var tasks = guardrailList.Select(async (guardrail, index) =>
        {
            try
            {
                var result = await guardrail.ValidateAsync(input, cts.Token);
                results.Add((index, result));
            }
            catch (TripwireGuardrailException ex)
            {
                _logger.LogWarning("Input tripwire triggered by {GuardrailName}: {Reason}",
                    ex.GuardrailName, ex.Message);
                tripwireResult = GuardrailResult.Rejected(ex.GuardrailName, ex.Message);
                await cts.CancelAsync();
            }
            catch (OperationCanceledException) when (tripwireResult != null)
            {
                // Tripwire 已触发，其他 guardrail 被取消 - 正常退出
            }
        });

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) when (tripwireResult != null)
        {
            // Tripwire 导致整体取消 - 已捕获
        }

        // Tripwire 优先级最高
        if (tripwireResult != null)
        {
            return (input, tripwireResult);
        }

        // 检查是否有拒绝结果（按注册顺序返回第一个拒绝）
        var orderedResults = results.OrderBy(r => r.index).ToList();
        var rejection = orderedResults.FirstOrDefault(r => !r.result.IsAllowed);
        if (rejection.result != null && !rejection.result.IsAllowed)
        {
            _logger.LogWarning("Input blocked by {GuardrailName}: {Reason}",
                rejection.result.GuardrailName, rejection.result.Reason);
            return (input, rejection.result);
        }

        return await ChainParallelModificationsAsync(
            input, orderedResults, (index, text, token) => guardrailList[index].ValidateAsync(text, token), "Input", ct);
    }

    /// <summary>
    /// 并行执行输出 Guardrails - 所有 guardrail 同时执行，支持 tripwire 立即中止
    /// </summary>
    private async Task<(string text, GuardrailResult? rejection)> RunOutputGuardrailsParallelAsync(string output, CancellationToken ct)
    {
        var guardrailList = _outputGuardrails.ToList();
        if (guardrailList.Count == 0)
        {
            return (output, null);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var results = new ConcurrentBag<(int index, GuardrailResult result)>();
        GuardrailResult? tripwireResult = null;

        var tasks = guardrailList.Select(async (guardrail, index) =>
        {
            try
            {
                var result = await guardrail.ValidateAsync(output, cts.Token);
                results.Add((index, result));
            }
            catch (TripwireGuardrailException ex)
            {
                _logger.LogWarning("Output tripwire triggered by {GuardrailName}: {Reason}",
                    ex.GuardrailName, ex.Message);
                tripwireResult = GuardrailResult.Rejected(ex.GuardrailName, ex.Message);
                await cts.CancelAsync();
            }
            catch (OperationCanceledException) when (tripwireResult != null)
            {
                // Tripwire 已触发，其他 guardrail 被取消 - 正常退出
            }
        });

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) when (tripwireResult != null)
        {
            // Tripwire 导致整体取消 - 已捕获
        }

        // Tripwire 优先级最高
        if (tripwireResult != null)
        {
            return (output, tripwireResult);
        }

        // 检查是否有拒绝结果（按注册顺序返回第一个拒绝）
        var orderedResults = results.OrderBy(r => r.index).ToList();
        var rejection = orderedResults.FirstOrDefault(r => !r.result.IsAllowed);
        if (rejection.result != null && !rejection.result.IsAllowed)
        {
            _logger.LogWarning("Output blocked by {GuardrailName}: {Reason}",
                rejection.result.GuardrailName, rejection.result.Reason);
            return (output, rejection.result);
        }

        return await ChainParallelModificationsAsync(
            output, orderedResults, (index, text, token) => guardrailList[index].ValidateAsync(text, token), "Output", ct);
    }

    /// <summary>
    /// 并行模式下合并多个守卫的改写。
    /// </summary>
    /// <remarks>
    /// 并行时每个改写都是对<b>原始文本</b>算出来的，逐个取 <c>ModifiedContent</c> 覆盖等于只留最后一个：
    /// 脱敏守卫在前、规范化守卫在后，脱敏结果就被规范化守卫基于原文的输出整个盖掉，原文照样进模型。
    /// 这里按注册顺序把改写串起来，与顺序模式同一语义：第一个改写者的结果作起点，其余改写者在当前文本上重跑一次。
    /// 只重跑改写过的守卫；重跑被拒即按拒绝处理（失败关闭）。
    /// </remarks>
    private async Task<(string text, GuardrailResult? rejection)> ChainParallelModificationsAsync(
        string original,
        IReadOnlyList<(int index, GuardrailResult result)> orderedResults,
        Func<int, string, CancellationToken, Task<GuardrailResult>> validate,
        string direction,
        CancellationToken ct)
    {
        var modifiers = orderedResults.Where(r => r.result.ModifiedContent != null).ToList();
        if (modifiers.Count == 0)
        {
            return (original, null);
        }

        var (_, first) = modifiers[0];
        _logger.LogDebug("{Direction} modified by {GuardrailName}: {Reason}", direction, first.GuardrailName, first.Reason);
        var currentText = first.ModifiedContent!;

        foreach (var (index, _) in modifiers.Skip(1))
        {
            var rerun = await validate(index, currentText, ct);
            if (!rerun.IsAllowed)
            {
                _logger.LogWarning("{Direction} blocked by {GuardrailName}: {Reason}", direction, rerun.GuardrailName, rerun.Reason);
                return (currentText, rerun);
            }

            if (rerun.ModifiedContent != null)
            {
                _logger.LogDebug("{Direction} modified by {GuardrailName}: {Reason}", direction, rerun.GuardrailName, rerun.Reason);
                currentText = rerun.ModifiedContent;
            }
        }

        return (currentText, null);
    }
}
