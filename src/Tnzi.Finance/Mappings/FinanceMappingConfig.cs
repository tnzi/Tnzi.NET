namespace Tnzi.Finance.Mappings;

/// <summary>
/// 财务模块映射配置
/// </summary>
public class FinanceMappingConfig : IMappingConfig
{
    /// <summary>
    /// 配置映射
    /// </summary>
    public void Configure(IMappingConfigContext context)
    {
        // 凭证交易币合计：头实体只冗余本位币合计（TotalDebit/TotalCredit），且按设计仅在过账时写入，
        // 因此草稿读它恒为 0。交易币金额建草稿时就落在行上，聚合即可得，任何状态都成立。
        // 这里配的是 MapTo 路径（GetAsync / CreateDraft / UpdateDraft / 过账返回 / GetBySource），
        // 列表走的是手写投影、已各自填好；两边都填才不会出现"列表有、详情为 0"的错位。
        // 所有 MapTo 调用点的 Lines 均已加载（Include 或内存构建），聚合不会静默取到 0。
        context.NewConfig<JournalEntry, JournalEntryDto>()
            .Map(dest => dest.TxnTotalDebit, src => src.Lines.Sum(l => l.TxnDebit))
            .Map(dest => dest.TxnTotalCredit, src => src.Lines.Sum(l => l.TxnCredit));
    }
}
