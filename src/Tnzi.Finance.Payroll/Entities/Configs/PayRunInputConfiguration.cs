namespace Tnzi.Finance.Payroll.Entities.Configs;

/// <summary>
/// 一次性输入配置（(批次, 员工, 组件) 唯一；批次删除时由服务在工作单元内级联清理）
/// </summary>
public class PayRunInputConfiguration : EntityTypeConfigurationBase<PayRunInput, Guid>
{
    public override void Configure(EntityTypeBuilder<PayRunInput> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.HasOne<PayRun>()
            .WithMany()
            .HasForeignKey(i => i.PayRunId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(i => i.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SalaryComponent>()
            .WithMany()
            .HasForeignKey(i => i.ComponentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(i => i.Amount).HasMoneyPrecision();
        builder.Property(i => i.Note).HasMaxLength(500);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(i => new { i.TenantId, i.PayRunId, i.EmployeeId, i.ComponentId }).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        }
        else
        {
            builder.HasIndex(i => new { i.PayRunId, i.EmployeeId, i.ComponentId }).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        }

        // 按批次取输入其实走得了上面那条复合唯一索引（PayRunId 是它的最左前缀之一）。
        // 这条仍然留着：上面那条带 IsDeleted 过滤器，是**部分索引** —— FK 强制检查、
        // 以及任何 IgnoreQueryFilters 的运维查询都用不上它。
        builder.HasIndex(i => i.PayRunId);
    }
}
