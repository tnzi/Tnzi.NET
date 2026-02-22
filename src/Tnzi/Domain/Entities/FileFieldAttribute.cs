
namespace Tnzi.Domain.Entities;

/// <summary>
/// 标记属性为文件字段，用于自动跟踪文件引用
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class FileFieldAttribute : Attribute
{
    /// <summary>
    /// 是否多文件字段（存储为 JSON 数组）
    /// </summary>
    public bool Multiple { get; set; } = false;
}