
namespace Tnzi.EFCore.Data;

/// <summary>
/// 数据过滤器管理器实现
/// </summary>
public class DataFilterManager : IDataFilterManager
{
    private readonly AsyncLocal<Dictionary<Type, Stack<bool>>> _filterStates = new();
    private readonly ILogger<DataFilterManager>? _logger;

    public DataFilterManager(ILogger<DataFilterManager>? logger = null)
    {
        _logger = logger;
    }

    private Dictionary<Type, Stack<bool>> FilterStates
    {
        get
        {
            if (_filterStates.Value == null)
            {
                _filterStates.Value = new Dictionary<Type, Stack<bool>>();
            }
            return _filterStates.Value;
        }
    }

    public IDisposable Disable<TFilter>() where TFilter : class, IDataFilter
    {
        // 审计日志：跨租户过滤器操作
        if (typeof(TFilter) == typeof(IMultiTenantFilter))
        {
            _logger?.LogWarning(
                "Multi-tenant filter disabled. Caller: {Caller}. Queries will bypass tenant isolation until scope is disposed.",
                GetCallerInfo());
        }

        return SetState<TFilter>(false);
    }

    public IDisposable Enable<TFilter>() where TFilter : class, IDataFilter
    {
        return SetState<TFilter>(true);
    }

    public bool IsEnabled<TFilter>() where TFilter : class, IDataFilter
    {
        var filterType = typeof(TFilter);
        if (!FilterStates.TryGetValue(filterType, out var stack) || stack.Count == 0)
        {
            // 默认启用
            return true;
        }
        return stack.Peek();
    }

    private IDisposable SetState<TFilter>(bool isEnabled) where TFilter : class, IDataFilter
    {
        var filterType = typeof(TFilter);
        if (!FilterStates.TryGetValue(filterType, out var stack))
        {
            stack = new Stack<bool>();
            FilterStates[filterType] = stack;
        }

        stack.Push(isEnabled);
        return new FilterStateScope(() =>
        {
            if (stack.Count > 0)
            {
                stack.Pop();
            }
        });
    }

    /// <summary>
    /// 获取调用者信息（用于审计日志）
    /// </summary>
    private static string GetCallerInfo()
    {
        var stackTrace = new StackTrace(skipFrames: 2, fNeedFileInfo: false);
        var frame = stackTrace.GetFrame(0);
        if (frame?.GetMethod() is { } method)
        {
            return $"{method.DeclaringType?.Name}.{method.Name}";
        }
        return "Unknown";
    }

    /// <summary>
    /// 过滤器状态范围
    /// </summary>
    private class FilterStateScope : IDisposable
    {
        private readonly Action _disposeAction;

        public FilterStateScope(Action disposeAction)
        {
            _disposeAction = disposeAction;
        }

        public void Dispose()
        {
            _disposeAction();
        }
    }
}

