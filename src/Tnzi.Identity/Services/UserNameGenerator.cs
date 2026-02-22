
namespace Tnzi.Identity;

/// <summary>
/// 用户名生成工具类
/// 提供唯一用户名生成逻辑，支持邮箱等特殊字符
/// </summary>
internal static class UserNameGenerator
{
    /// <summary>
    /// 生成唯一用户名（如果已存在则添加数字后缀）
    /// </summary>
    /// <param name="baseUserName">基础用户名</param>
    /// <param name="checkUserNameExistsAsync">检查用户名是否存在的异步委托</param>
    /// <returns>唯一的用户名</returns>
    public static async Task<string> GenerateUniqueAsync(string baseUserName, Func<string, Task<bool>> checkUserNameExistsAsync)
    {
        Check.NotNull(checkUserNameExistsAsync);

        // 清理用户名，移除不允许的字符（允许字母、数字、下划线、连字符、@和点号，以支持邮箱作为用户名）
        var cleanUserName = new string(baseUserName.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '@' || c == '.').ToArray());
        if (string.IsNullOrEmpty(cleanUserName))
        {
            cleanUserName = "user";
        }

        var userName = cleanUserName;
        var suffix = 1;

        // 循环检查直到找到唯一用户名
        while (await checkUserNameExistsAsync(userName))
        {
            userName = $"{cleanUserName}_{suffix}";
            suffix++;

            // 防止无限循环（理论上不应该发生）
            if (suffix > 10000)
            {
                userName = $"{cleanUserName}_{Guid.NewGuid():N}";
                break;
            }
        }

        return userName;
    }
}
