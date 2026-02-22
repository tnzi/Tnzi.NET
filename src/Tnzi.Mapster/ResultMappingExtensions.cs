
namespace Tnzi.Mapster;

/// <summary>
/// Result 映射扩展方法
/// 提供 Result 类型的异步映射功能
/// </summary>
public static class ResultMappingExtensions
{
    /// <summary>
    /// 将 Result&lt;TSource&gt; 映射为 Result&lt;TTarget&gt;
    /// 如果源结果成功，则映射数据；如果失败，则返回失败结果
    /// </summary>
    /// <typeparam name="TSource">源数据类型</typeparam>
    /// <typeparam name="TTarget">目标数据类型</typeparam>
    /// <param name="result">源结果</param>
    /// <returns>映射后的结果</returns>
    public static Result<TTarget> Map<TSource, TTarget>(this Result<TSource> result)
    {
        Check.NotNull(result);

        if (!result.Succeeded)
        {
            return Result<TTarget>.Failure(
                result.Message ?? "Operation failed",
                result.Code ?? 400,
                result.ErrorCode,
                result.ErrorDetails);
        }

        if (result.Data == null)
        {
            return Result<TTarget>.Failure("Data is null, cannot map.", 400, "MAPPING_ERROR");
        }

        try
        {
            var mappedData = result.Data.MapTo<TTarget>();
            return Result<TTarget>.Success(mappedData, result.Message, result.Code);
        }
        catch (Exception ex)
        {
#if DEBUG
            throw;
#else
            return Result<TTarget>.Failure(
                $"Mapping failed: {ex.Message}",
                500,
                "MAPPING_ERROR",
                new { ExceptionType = ex.GetType().Name, OriginalMessage = ex.Message });
#endif
        }
    }

    /// <summary>
    /// 异步将 Result&lt;TSource&gt; 映射为 Result&lt;TTarget&gt;
    /// 如果源结果成功，则异步映射数据；如果失败，则返回失败结果
    /// </summary>
    /// <typeparam name="TSource">源数据类型</typeparam>
    /// <typeparam name="TTarget">目标数据类型</typeparam>
    /// <param name="resultTask">源结果任务</param>
    /// <param name="mapper">异步映射函数</param>
    /// <returns>映射后的结果任务</returns>
    public static async Task<Result<TTarget>> MapAsync<TSource, TTarget>(
        this Task<Result<TSource>> resultTask,
        Func<TSource, Task<TTarget>> mapper)
    {
        Check.NotNull(resultTask);
        Check.NotNull(mapper);

        var result = await resultTask;
        return await result.MapAsync(mapper);
    }

    /// <summary>
    /// 异步将 Result&lt;TSource&gt; 映射为 Result&lt;TTarget&gt;
    /// 如果源结果成功，则映射数据；如果失败，则返回失败结果
    /// </summary>
    /// <typeparam name="TSource">源数据类型</typeparam>
    /// <typeparam name="TTarget">目标数据类型</typeparam>
    /// <param name="result">源结果</param>
    /// <param name="mapper">异步映射函数</param>
    /// <returns>映射后的结果任务</returns>
    public static async Task<Result<TTarget>> MapAsync<TSource, TTarget>(
        this Result<TSource> result,
        Func<TSource, Task<TTarget>> mapper)
    {
        Check.NotNull(result);
        Check.NotNull(mapper);

        if (!result.Succeeded)
        {
            return Result<TTarget>.Failure(
                result.Message ?? "Operation failed",
                result.Code ?? 400,
                result.ErrorCode,
                result.ErrorDetails);
        }

        if (result.Data == null)
        {
            return Result<TTarget>.Failure("Data is null, cannot map.", 400, "MAPPING_ERROR");
        }

        try
        {
            var mappedData = await mapper(result.Data);
            return Result<TTarget>.Success(mappedData, result.Message, result.Code);
        }
        catch (Exception ex)
        {
#if DEBUG
            throw;
#else
            return Result<TTarget>.Failure(
                $"Mapping failed: {ex.Message}",
                500,
                "MAPPING_ERROR",
                new { ExceptionType = ex.GetType().Name, OriginalMessage = ex.Message });
#endif
        }
    }
}
