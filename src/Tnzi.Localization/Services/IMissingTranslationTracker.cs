namespace Tnzi.Localization.Services;

/// <summary>
/// 缺失翻译追踪器接口
/// 用于记录和报告缺失的翻译 key
/// </summary>
public interface IMissingTranslationTracker
{
    /// <summary>
    /// 追踪缺失的翻译
    /// </summary>
    /// <param name="culture">文化名称</param>
    /// <param name="key">缺失的翻译 key</param>
    void TrackMissing(string culture, string key);
}
