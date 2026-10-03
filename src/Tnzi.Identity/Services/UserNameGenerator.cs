
namespace Tnzi.Identity.Services;

/// <summary>
/// 用户名生成工具类：给没有人工起名的账号（验证码自动注册、第三方登录）定一个唯一用户名。
/// </summary>
internal static class UserNameGenerator
{
    /// <summary>
    /// 生成唯一用户名（如果已存在则添加数字后缀）。
    /// </summary>
    /// <param name="baseUserName">基础用户名（邮箱、手机号、显示名等）</param>
    /// <param name="checkUserNameExistsAsync">检查用户名是否存在的异步委托</param>
    /// <param name="ownEmail">新账号自己的邮箱。只有基底<b>就是</b>它时，结果才允许带 <c>@</c>。</param>
    /// <returns>唯一的用户名</returns>
    /// <remarks>
    /// ★ 带 <c>@</c> 的用户名只能是账号自己的邮箱（见 <see cref="UserNamePolicy"/>）。因此：
    /// <list type="bullet">
    /// <item>基底是自己的邮箱时<b>原样</b>使用，不做字符清洗。清洗会把 <c>a+b@x.com</c> 变成
    /// <c>ab@x.com</c>，一个长得像另一个人邮箱的用户名。</item>
    /// <item>邮箱已被占用作用户名（存量数据）时，不在它后面加后缀（<c>a@x.com_1</c> 仍是邮箱形态），
    /// 而是退到 <c>@</c> 之前的部分再编号。这样的账号用户名与邮箱不再绑定。</item>
    /// <item>其余基底（显示名等）里的 <c>@</c> 一律去掉。</item>
    /// </list>
    /// </remarks>
    public static async Task<string> GenerateUniqueAsync(string baseUserName, Func<string, Task<bool>> checkUserNameExistsAsync, string? ownEmail = null)
    {
        Check.NotNull(checkUserNameExistsAsync);

        var isOwnEmail = !string.IsNullOrEmpty(ownEmail)
            && string.Equals(baseUserName, ownEmail, StringComparison.OrdinalIgnoreCase);
        if (isOwnEmail)
        {
            if (!await checkUserNameExistsAsync(baseUserName))
            {
                return baseUserName;
            }

            var at = baseUserName.IndexOf('@');
            if (at > 0)
            {
                baseUserName = baseUserName[..at];
            }
        }

        // 允许字母、数字、下划线、连字符和点号
        var cleanUserName = new string(baseUserName.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.').ToArray());
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
