using Microsoft.Extensions.Options;

namespace Tnzi.EFCore.Tests.Encryption;

/// <summary>
/// 可变的 <see cref="IOptionsMonitor{TOptions}"/> 替身:<see cref="Update"/> 像真实的配置重载一样换上一份新的选项对象。
/// </summary>
/// <remarks>
/// 密钥环按选项对象的引用判断是否需要重建,所以测试不能就地改同一个选项实例,
/// 否则测到的是「改了没人知道」而不是真实的重载行为。
/// </remarks>
internal sealed class MutableFieldEncryptionOptionsMonitor : IOptionsMonitor<FieldEncryptionOptions>
{
    private FieldEncryptionOptions _current;

    public MutableFieldEncryptionOptionsMonitor(FieldEncryptionOptions initial) => _current = initial;

    public FieldEncryptionOptions CurrentValue => _current;

    public FieldEncryptionOptions Get(string? name) => _current;

    public IDisposable? OnChange(Action<FieldEncryptionOptions, string?> listener) => null;

    /// <summary>复制当前选项、应用修改,作为新的选项对象换上。</summary>
    public void Update(Action<FieldEncryptionOptions> change)
    {
        var next = new FieldEncryptionOptions
        {
            Enabled = _current.Enabled,
            ActiveKeyId = _current.ActiveKeyId,
            Keys = new Dictionary<string, string>(_current.Keys),
            KeyFile = _current.KeyFile,
            Mode = _current.Mode
        };
        change(next);
        _current = next;
    }
}
