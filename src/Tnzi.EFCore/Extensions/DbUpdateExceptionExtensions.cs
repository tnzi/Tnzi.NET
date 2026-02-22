
namespace Tnzi.EFCore.Extensions;

/// <summary>
/// DbUpdateException 扩展方法
/// </summary>
public static class DbUpdateExceptionExtensions
{
    /// <summary>
    /// 检查异常是否是数据库唯一约束冲突
    /// </summary>
    /// <param name="exception">数据库更新异常</param>
    /// <returns>如果是唯一约束冲突返回 true，否则返回 false</returns>
    public static bool IsUniqueConstraintViolation(this DbUpdateException exception)
    {
        if (exception?.InnerException == null)
            return false;

        var message = exception.InnerException.Message;
        if (string.IsNullOrEmpty(message))
            return false;

        return message.Contains("UNIQUE constraint", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase);
    }
}