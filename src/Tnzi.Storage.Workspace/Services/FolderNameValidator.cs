namespace Tnzi.Storage.Workspace.Services;

/// <summary>
/// 目录名的形态规则：<see cref="FileFolder.Path"/> 是把名字用 <c>/</c> 拼起来的，
/// 名字里的分隔符会让一条 <c>ParentId</c> 为空的行长在别人的路径前缀下面。
/// </summary>
/// <remarks>
/// <para>
/// 拒绝的只有三类：路径分隔符（<c>/</c> 与 <c>\</c>）、控制字符、以及只由点组成的名字
/// （<c>.</c> / <c>..</c>，在任何路径语义里都表示别的目录）。带点、带空格的普通名字照常接受。
/// </para>
/// <para>
/// 放行不等于路径上没有碰撞：同一位置重名仍由服务层按 409 回答。本类只回答「这个名字
/// 能不能安全地参与路径拼装」。
/// </para>
/// </remarks>
public static class FolderNameValidator
{
    /// <summary>面向调用方的拒绝理由（英文，随 400 返回）。</summary>
    public const string InvalidNameMessage =
        "Folder name contains invalid characters: path separators, control characters and dot-only names are not allowed";

    /// <summary>名字（已 Trim）能否参与路径拼装。</summary>
    public static bool IsValid(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (name.All(c => c == '.'))
            return false;

        return !name.Any(c => c is '/' or '\\' || char.IsControl(c));
    }
}
