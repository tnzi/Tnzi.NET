namespace Tnzi.AI.Guardrails;

/// <summary>
/// Guardrail 执行器 — 统一运行所有注册的输入/输出 Guardrails，支持顺序和并行执行模式
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
    /// 顺序执行输入 Guardrails — 遇到第一个拒绝即停止
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
    /// 顺序执行输出 Guardrails — 遇到第一个拒绝即停止
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
    /// 并行执行输入 Guardrails — 所有 guardrail 同时执行，支持 tripwire 立即中止
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
                // Tripwire 已触发，其他 guardrail 被取消 — 正常退出
            }
        });

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) when (tripwireResult != null)
        {
            // Tripwire 导致整体取消 — 已捕获
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

        // 按注册顺序依次应用修改（并行模式下修改基于原始输入，顺序应用）
        var currentText = input;
        foreach (var (_, result) in orderedResults)
        {
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
    /// 并行执行输出 Guardrails — 所有 guardrail 同时执行，支持 tripwire 立即中止
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
                // Tripwire 已触发，其他 guardrail 被取消 — 正常退出
            }
        });

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) when (tripwireResult != null)
        {
            // Tripwire 导致整体取消 — 已捕获
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

        // 按注册顺序依次应用修改
        var currentText = output;
        foreach (var (_, result) in orderedResults)
        {
            if (result.ModifiedContent != null)
            {
                _logger.LogDebug("Output modified by {GuardrailName}: {Reason}",
                    result.GuardrailName, result.Reason);
                currentText = result.ModifiedContent;
            }
        }

        return (currentText, null);
    }
}
