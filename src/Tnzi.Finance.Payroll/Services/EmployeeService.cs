namespace Tnzi.Finance.Payroll.Services;

/// <summary>
/// 员工服务（含薪资分配子资源）
/// </summary>
public class EmployeeService : ApplicationService, IEmployeeService
{
    private readonly IRepository<Employee, Guid> _employeeRepository;
    private readonly IRepository<SalaryAssignment, Guid> _assignmentRepository;
    private readonly IRepository<SalaryStructure, Guid> _structureRepository;
    private readonly IRepository<Vendor, Guid> _vendorRepository;
    private readonly IVendorService _vendorService;

    public EmployeeService(
        IServiceProvider serviceProvider,
        IRepository<Employee, Guid> employeeRepository,
        IRepository<SalaryAssignment, Guid> assignmentRepository,
        IRepository<SalaryStructure, Guid> structureRepository,
        IRepository<Vendor, Guid> vendorRepository,
        IVendorService vendorService) : base(serviceProvider)
    {
        _employeeRepository = Check.NotNull(employeeRepository);
        _assignmentRepository = Check.NotNull(assignmentRepository);
        _structureRepository = Check.NotNull(structureRepository);
        _vendorRepository = Check.NotNull(vendorRepository);
        _vendorService = Check.NotNull(vendorService);
    }

    public async Task<Result<IPagedList<EmployeeDto>>> GetPagedAsync(EmployeeQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var pagedList = await _employeeRepository.AsNoTracking()
            .Filter(query)
            .OrderBy(e => e.Code)
            .ProjectTo<Employee, EmployeeDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        return Ok(pagedList);
    }

    public async Task<Result<EmployeeDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var employee = await _employeeRepository.GetAsync(id, cancellationToken);
        if (employee == null)
            return Fail<EmployeeDto>("Employee not found.", 404);

        return Ok(employee.MapTo<EmployeeDto>());
    }

    public async Task<Result<EmployeeDto>> CreateAsync(CreateEmployeeDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var validation = await ValidateAsync(input, excludeId: null, cancellationToken);
        if (!validation.Succeeded)
            return Fail<EmployeeDto>(validation.Message ?? "Invalid employee.", validation.Code ?? 400);

        var employee = new Employee();
        Apply(employee, input, isActive: true);

        try
        {
            await _employeeRepository.InsertAsync(employee, cancellationToken);
            await _employeeRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<EmployeeDto>($"Employee code '{employee.Code}' already exists.", 409);
        }

        return Ok(employee.MapTo<EmployeeDto>());
    }

    public async Task<Result<EmployeeDto>> UpdateAsync(Guid id, UpdateEmployeeDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var employee = await _employeeRepository.GetAsync(id, cancellationToken);
        if (employee == null)
            return Fail<EmployeeDto>("Employee not found.", 404);

        var validation = await ValidateAsync(input, excludeId: id, cancellationToken);
        if (!validation.Succeeded)
            return Fail<EmployeeDto>(validation.Message ?? "Invalid employee.", validation.Code ?? 400);

        Apply(employee, input, input.IsActive);

        try
        {
            // 员工更新 + 影子供应商名称同步是两次写入，须原子提交
            await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                await _employeeRepository.UpdateAsync(employee, ct);
                await _employeeRepository.SaveChangesAsync(ct);
                await SyncVendorNameAsync(employee, ct);
                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<EmployeeDto>($"Employee code '{employee.Code}' already exists.", 409);
        }

        return Ok(employee.MapTo<EmployeeDto>());
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var employee = await _employeeRepository.GetAsync(id, cancellationToken);
        if (employee == null)
            return Fail("Employee not found.", 404);

        if (await _assignmentRepository.AnyAsync(a => a.EmployeeId == id, cancellationToken))
            return Fail("The employee has salary assignments and cannot be deleted. Set a termination date instead.", 409);

        await _employeeRepository.DeleteAsync(employee, cancellationToken);
        return Ok();
    }

    public async Task<Result<EmployeeDto>> EnsurePayeeVendorAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var employee = await _employeeRepository.GetAsync(id, cancellationToken);
        if (employee == null)
            return Fail<EmployeeDto>("Employee not found.", 404);

        if (employee.VendorId.HasValue &&
            await _vendorRepository.AnyAsync(v => v.Id == employee.VendorId.Value, cancellationToken))
        {
            // 已链接且供应商仍在——幂等直接返回
            return Ok(employee.MapTo<EmployeeDto>());
        }

        // 创建 + 回填是两次写入，须原子提交（供应商创建失败在任何写入前以 Result 返回，
        // 之后的失败只可能是异常——环境事务整体回滚，不留孤儿供应商）
        return await ExecuteInUnitOfWorkAsync<Result<EmployeeDto>>(async ct =>
        {
            var created = await _vendorService.CreateAsync(new CreateVendorDto
            {
                Name = employee.Name,
                Notes = $"Payroll payee vendor for employee '{employee.Code}'."
            }, ct);

            if (!created.Succeeded)
                return Fail<EmployeeDto>(created.Message ?? "Failed to create the payee vendor.", created.Code ?? 500);

            employee.VendorId = created.Data!.Id;
            await _employeeRepository.UpdateAsync(employee, ct);
            await _employeeRepository.SaveChangesAsync(ct);

            return Ok(employee.MapTo<EmployeeDto>());
        }, cancellationToken);
    }

    public async Task<Employee?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(code);
        var normalized = code.Trim();
        return await _employeeRepository.FindAsync(e => e.Code == normalized, cancellationToken);
    }

    public async Task<Result<List<SalaryAssignmentDto>>> GetAssignmentsAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (!await _employeeRepository.AnyAsync(e => e.Id == employeeId, cancellationToken))
            return Fail<List<SalaryAssignmentDto>>("Employee not found.", 404);

        var assignments = await _assignmentRepository.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId)
            .OrderByDescending(a => a.EffectiveFrom)
            .ToListAsync(cancellationToken);

        return Ok(await ToAssignmentDtosAsync(assignments, cancellationToken));
    }

    public async Task<Result<SalaryAssignmentDto>> CreateAssignmentAsync(Guid employeeId, CreateSalaryAssignmentDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        if (!await _employeeRepository.AnyAsync(e => e.Id == employeeId, cancellationToken))
            return Fail<SalaryAssignmentDto>("Employee not found.", 404);

        var structure = await _structureRepository.GetAsync(input.StructureId, cancellationToken);
        if (structure == null)
            return Fail<SalaryAssignmentDto>("Salary structure not found.", 404);
        if (!structure.IsActive)
            return Fail<SalaryAssignmentDto>($"Salary structure '{structure.Name}' is inactive.", 400);

        if (input.BaseAmount < 0)
            return Fail<SalaryAssignmentDto>("BaseAmount cannot be negative.", 400);

        var effectiveFrom = input.EffectiveFrom.ToUtcDate();
        if (await _assignmentRepository.AnyAsync(a => a.EmployeeId == employeeId && a.EffectiveFrom == effectiveFrom, cancellationToken))
            return Fail<SalaryAssignmentDto>("An assignment with the same effective date already exists for this employee.", 409);

        var assignment = new SalaryAssignment
        {
            EmployeeId = employeeId,
            StructureId = structure.Id,
            EffectiveFrom = effectiveFrom,
            BaseAmount = input.BaseAmount,
            Notes = input.Notes
        };

        try
        {
            await _assignmentRepository.InsertAsync(assignment, cancellationToken);
            await _assignmentRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<SalaryAssignmentDto>("An assignment with the same effective date already exists for this employee.", 409);
        }

        var dto = assignment.MapTo<SalaryAssignmentDto>();
        dto.StructureName = structure.Name;
        return Ok(dto);
    }

    public async Task<Result> DeleteAssignmentAsync(Guid employeeId, Guid assignmentId, CancellationToken cancellationToken = default)
    {
        var assignment = await _assignmentRepository.FindAsync(a => a.Id == assignmentId && a.EmployeeId == employeeId, cancellationToken);
        if (assignment == null)
            return Fail("Salary assignment not found.", 404);

        await _assignmentRepository.DeleteAsync(assignment, cancellationToken);
        return Ok();
    }

    public async Task<SalaryAssignment?> ResolveAssignmentAsync(Guid employeeId, DateTime asOf, CancellationToken cancellationToken = default)
    {
        var cutoff = asOf.ToUtcDate();
        return await _assignmentRepository.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId && a.EffectiveFrom <= cutoff)
            .OrderByDescending(a => a.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Result> ValidateAsync(CreateEmployeeDto input, Guid? excludeId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Code))
            return Fail("Employee code is required.");
        if (string.IsNullOrWhiteSpace(input.Name))
            return Fail("Employee name is required.");

        if (input.HireDate.HasValue && input.TerminationDate.HasValue &&
            input.TerminationDate.Value.ToUtcDate() < input.HireDate.Value.ToUtcDate())
        {
            return Fail("TerminationDate cannot be earlier than HireDate.");
        }

        var attributesCheck = PayrollAttributeHelper.Validate(input.AttributesJson);
        if (!attributesCheck.Succeeded)
            return attributesCheck;

        var code = input.Code.Trim();
        if (await _employeeRepository.AnyAsync(e => e.Code == code && e.Id != excludeId, cancellationToken))
            return Fail($"Employee code '{code}' already exists.", 409);

        return Ok();
    }

    private static void Apply(Employee employee, CreateEmployeeDto input, bool isActive)
    {
        employee.Code = input.Code.Trim();
        employee.Name = input.Name.Trim();
        employee.Email = input.Email?.Trim();
        employee.Phone = input.Phone?.Trim();
        employee.HireDate = input.HireDate?.ToUtcDate();
        employee.TerminationDate = input.TerminationDate?.ToUtcDate();
        employee.UserId = input.UserId;
        employee.AttributesJson = string.IsNullOrWhiteSpace(input.AttributesJson) ? null : input.AttributesJson.Trim();
        employee.Notes = input.Notes;
        employee.IsActive = isActive;
    }

    /// <summary>
    /// 员工姓名 → 影子供应商名称的单向同步（供应商已被管理员删除时静默跳过）
    /// </summary>
    private async Task SyncVendorNameAsync(Employee employee, CancellationToken cancellationToken)
    {
        if (!employee.VendorId.HasValue)
            return;

        var vendor = await _vendorRepository.GetAsync(employee.VendorId.Value, cancellationToken);
        if (vendor == null || vendor.Name == employee.Name)
            return;

        vendor.Name = employee.Name;
        await _vendorRepository.UpdateAsync(vendor, cancellationToken);
        await _vendorRepository.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<SalaryAssignmentDto>> ToAssignmentDtosAsync(List<SalaryAssignment> assignments, CancellationToken cancellationToken)
    {
        if (assignments.Count == 0)
            return [];

        var structureIds = assignments.Select(a => a.StructureId).Distinct().ToList();
        var names = await _structureRepository.AsNoTracking()
            .Where(s => structureIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);

        return assignments.Select(a =>
        {
            var dto = a.MapTo<SalaryAssignmentDto>();
            dto.StructureName = names.GetValueOrDefault(a.StructureId, string.Empty);
            return dto;
        }).ToList();
    }
}
