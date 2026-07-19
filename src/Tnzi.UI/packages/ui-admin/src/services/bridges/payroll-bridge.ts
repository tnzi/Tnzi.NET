/**
 * Payroll bridge - thin adapter over `@tnzi/core`'s admin payroll API
 * (`/admin/payroll/*` exposed by the Tnzi.Finance.Payroll module's six
 * `DefaultPayroll*AdminController`s: employees, components, structures,
 * brackets, pay runs, country packs).
 *
 * Pages import DTO types from this bridge (never from `@tnzi/core/services/*`
 * directly) so integration tests can `vi.mock` the whole module.
 */
import type { HttpClient } from '@tnzi/core/http'
import {
  useAdminPayrollApi,
  type EmployeeDto as CoreEmployeeDto,
  type CreateEmployeeDto as CoreCreateEmployeeDto,
  type UpdateEmployeeDto as CoreUpdateEmployeeDto,
  type SalaryAssignmentDto as CoreSalaryAssignmentDto,
  type CreateSalaryAssignmentDto as CoreCreateSalaryAssignmentDto,
  type SalaryComponentDto as CoreSalaryComponentDto,
  type CreateSalaryComponentDto as CoreCreateSalaryComponentDto,
  type UpdateSalaryComponentDto as CoreUpdateSalaryComponentDto,
  type SalaryStructureDto as CoreSalaryStructureDto,
  type SalaryStructureListDto as CoreSalaryStructureListDto,
  type SalaryStructureLineDto as CoreSalaryStructureLineDto,
  type CreateSalaryStructureDto as CoreCreateSalaryStructureDto,
  type UpdateSalaryStructureDto as CoreUpdateSalaryStructureDto,
  type SalaryStructureLineInputDto as CoreSalaryStructureLineInputDto,
  type BracketTableDto as CoreBracketTableDto,
  type BracketTableListDto as CoreBracketTableListDto,
  type BracketRowDto as CoreBracketRowDto,
  type CreateBracketTableDto as CoreCreateBracketTableDto,
  type UpdateBracketTableDto as CoreUpdateBracketTableDto,
  type BracketRowInputDto as CoreBracketRowInputDto,
  type PayRunDto as CorePayRunDto,
  type PayRunListDto as CorePayRunListDto,
  type CreatePayRunDto as CoreCreatePayRunDto,
  type UpdatePayRunDto as CoreUpdatePayRunDto,
  type PayRunPaymentDto as CorePayRunPaymentDto,
  type PayslipDto as CorePayslipDto,
  type PayslipListDto as CorePayslipListDto,
  type PayslipLineDto as CorePayslipLineDto,
  type UpdatePayslipInputsDto as CoreUpdatePayslipInputsDto,
  type ExternalPayRunIngestDto as CoreExternalPayRunIngestDto,
  type CountryPackDto as CoreCountryPackDto,
  type CountryPackSeedResult as CoreCountryPackSeedResult,
} from '@tnzi/core/services/payroll'
import type { PagedList } from '@tnzi/core'
import { ensureOk, unwrapResult as unwrap, pagedResult, pageArray } from '../_mappers'

// Re-export the DTO types under bridge names consumed by pages/configs.
export type EmployeeDto = CoreEmployeeDto
export type CreateEmployeeDto = CoreCreateEmployeeDto
export type UpdateEmployeeDto = CoreUpdateEmployeeDto
export type SalaryAssignmentDto = CoreSalaryAssignmentDto
export type CreateSalaryAssignmentDto = CoreCreateSalaryAssignmentDto
export type SalaryComponentDto = CoreSalaryComponentDto
export type CreateSalaryComponentDto = CoreCreateSalaryComponentDto
export type UpdateSalaryComponentDto = CoreUpdateSalaryComponentDto
export type SalaryStructureDto = CoreSalaryStructureDto
export type SalaryStructureListDto = CoreSalaryStructureListDto
export type SalaryStructureLineDto = CoreSalaryStructureLineDto
export type CreateSalaryStructureDto = CoreCreateSalaryStructureDto
export type UpdateSalaryStructureDto = CoreUpdateSalaryStructureDto
export type SalaryStructureLineInputDto = CoreSalaryStructureLineInputDto
export type BracketTableDto = CoreBracketTableDto
export type BracketTableListDto = CoreBracketTableListDto
export type BracketRowDto = CoreBracketRowDto
export type CreateBracketTableDto = CoreCreateBracketTableDto
export type UpdateBracketTableDto = CoreUpdateBracketTableDto
export type BracketRowInputDto = CoreBracketRowInputDto
export type PayRunDto = CorePayRunDto
export type PayRunListDto = CorePayRunListDto
export type CreatePayRunDto = CoreCreatePayRunDto
export type UpdatePayRunDto = CoreUpdatePayRunDto
export type PayRunPaymentDto = CorePayRunPaymentDto
export type PayslipDto = CorePayslipDto
export type PayslipListDto = CorePayslipListDto
export type PayslipLineDto = CorePayslipLineDto
export type UpdatePayslipInputsDto = CoreUpdatePayslipInputsDto
export type ExternalPayRunIngestDto = CoreExternalPayRunIngestDto
export type CountryPackDto = CoreCountryPackDto
export type CountryPackSeedResult = CoreCountryPackSeedResult

export {
  SalaryComponentType,
  PayFrequency,
  PayRunStatus,
  PayRunSource,
  PayslipPaymentStatus,
  YtdBasis,
  SALARY_COMPONENT_TYPE_LABELS,
  PAY_FREQUENCY_LABELS,
  PAY_RUN_STATUS_LABELS,
  PAY_RUN_SOURCE_LABELS,
  PAYSLIP_PAYMENT_STATUS_LABELS,
  YTD_BASIS_LABELS,
} from '@tnzi/core/services/payroll'

/** Page-facing paged query (CrudPageQuery-compatible subset). */
export interface PayrollPagedQuery {
  pageIndex: number
  pageSize: number
  searchText?: string
  filters?: Record<string, unknown>
}

export type PayrollPagedResult<T> = PagedList<T>

export interface PayrollBridgeDeps {
  client?: HttpClient
}

export interface PayrollBridge {
  employees: {
    fetch(query: PayrollPagedQuery): Promise<PayrollPagedResult<EmployeeDto>>
    get(id: string): Promise<EmployeeDto | null>
    create(data: CoreCreateEmployeeDto): Promise<EmployeeDto>
    update(id: string, data: CoreUpdateEmployeeDto): Promise<EmployeeDto>
    delete(ids: string[]): Promise<void>
    ensureVendor(id: string): Promise<EmployeeDto>
    assignments(id: string): Promise<SalaryAssignmentDto[]>
    createAssignment(id: string, data: CoreCreateSalaryAssignmentDto): Promise<SalaryAssignmentDto>
    deleteAssignment(id: string, assignmentId: string): Promise<void>
  }
  components: {
    fetch(query: PayrollPagedQuery): Promise<PayrollPagedResult<SalaryComponentDto>>
    create(data: CoreCreateSalaryComponentDto): Promise<SalaryComponentDto>
    update(id: string, data: CoreUpdateSalaryComponentDto): Promise<SalaryComponentDto>
    delete(ids: string[]): Promise<void>
  }
  structures: {
    fetch(query: PayrollPagedQuery): Promise<PayrollPagedResult<SalaryStructureListDto>>
    getById(id: string): Promise<SalaryStructureDto | null>
    create(data: CoreCreateSalaryStructureDto): Promise<SalaryStructureDto>
    update(id: string, data: CoreUpdateSalaryStructureDto): Promise<SalaryStructureDto>
    delete(ids: string[]): Promise<void>
  }
  brackets: {
    fetch(query: PayrollPagedQuery): Promise<PayrollPagedResult<BracketTableListDto>>
    getById(id: string): Promise<BracketTableDto | null>
    resolve(code: string, asOf: string): Promise<BracketTableDto>
    create(data: CoreCreateBracketTableDto): Promise<BracketTableDto>
    update(id: string, data: CoreUpdateBracketTableDto): Promise<BracketTableDto>
    delete(ids: string[]): Promise<void>
  }
  runs: {
    fetch(query: PayrollPagedQuery): Promise<PayrollPagedResult<PayRunListDto>>
    getById(id: string): Promise<PayRunDto | null>
    createDraft(data: CoreCreatePayRunDto): Promise<PayRunDto>
    updateDraft(id: string, data: CoreUpdatePayRunDto): Promise<PayRunDto>
    deleteDraft(id: string): Promise<void>
    calculate(id: string): Promise<PayRunDto>
    post(id: string): Promise<PayRunDto>
    pay(id: string, data: CorePayRunPaymentDto): Promise<PayRunDto>
    voidRun(id: string): Promise<PayRunDto>
    payslips(id: string): Promise<PayslipListDto[]>
    payslip(id: string, payslipId: string): Promise<PayslipDto | null>
    updatePayslipInputs(id: string, payslipId: string, data: CoreUpdatePayslipInputsDto): Promise<PayslipDto>
    createFromExternal(data: CoreExternalPayRunIngestDto): Promise<PayRunDto>
  }
  countryPacks: {
    /** 后端无分页端点(注册表是内存集合),客户端经 pageArray 分页以对接 useCrudPage。 */
    fetch(query: PayrollPagedQuery): Promise<PayrollPagedResult<CountryPackDto>>
    seed(code: string): Promise<CountryPackSeedResult>
  }
}

function toPaged<T>(
  result: { items?: T[]; totalCount?: number; pageIndex?: number; pageSize?: number },
  query: PayrollPagedQuery,
): PayrollPagedResult<T> {
  return pagedResult({
    items: result.items ?? [],
    totalCount: result.totalCount ?? 0,
    pageIndex: result.pageIndex ?? query.pageIndex,
    pageSize: result.pageSize ?? query.pageSize,
  })
}

export function createPayrollBridge(deps: PayrollBridgeDeps = {}): PayrollBridge {
  const { client } = deps

  if (!client) {
    const noOp = () => Promise.reject(new Error('createPayrollBridge: no HttpClient provided'))
    const section = new Proxy({}, { get: () => noOp })
    return {
      employees: section as never,
      components: section as never,
      structures: section as never,
      brackets: section as never,
      runs: section as never,
      countryPacks: section as never,
    }
  }

  const api = useAdminPayrollApi(client)

  return {
    employees: {
      fetch: async (query) => {
        const filters = query.filters ?? {}
        const result = unwrap<PayrollPagedResult<EmployeeDto>>(
          (await api.employees.getList({
            pageIndex: query.pageIndex,
            pageSize: query.pageSize,
            keyword: query.searchText || undefined,
            isActive: (filters.isActive as boolean | undefined) ?? undefined,
          } as never)) as never,
        )
        return toPaged(result, query)
      },
      get: async (id) => unwrap<EmployeeDto | null>(await api.employees.get(id)),
      create: async (data) => unwrap<EmployeeDto>(await api.employees.create(data)),
      update: async (id, data) => unwrap<EmployeeDto>(await api.employees.update(id, data)),
      delete: async (ids) => {
        for (const id of ids) ensureOk(await api.employees.delete(id))
      },
      ensureVendor: async (id) => unwrap<EmployeeDto>(await api.employees.ensureVendor(id)),
      assignments: async (id) => unwrap<SalaryAssignmentDto[]>(await api.employees.getAssignments(id)) ?? [],
      createAssignment: async (id, data) =>
        unwrap<SalaryAssignmentDto>(await api.employees.createAssignment(id, data)),
      deleteAssignment: async (id, assignmentId) => {
        ensureOk(await api.employees.deleteAssignment(id, assignmentId))
      },
    },

    components: {
      fetch: async (query) => {
        const filters = query.filters ?? {}
        const result = unwrap<PayrollPagedResult<SalaryComponentDto>>(
          (await api.components.getList({
            pageIndex: query.pageIndex,
            pageSize: query.pageSize,
            keyword: query.searchText || undefined,
            type: (filters.type as string | undefined) ?? undefined,
            isActive: (filters.isActive as boolean | undefined) ?? undefined,
          } as never)) as never,
        )
        return toPaged(result, query)
      },
      create: async (data) => unwrap<SalaryComponentDto>(await api.components.create(data)),
      update: async (id, data) => unwrap<SalaryComponentDto>(await api.components.update(id, data)),
      delete: async (ids) => {
        for (const id of ids) ensureOk(await api.components.delete(id))
      },
    },

    structures: {
      fetch: async (query) => {
        const filters = query.filters ?? {}
        const result = unwrap<PayrollPagedResult<SalaryStructureListDto>>(
          (await api.structures.getList({
            pageIndex: query.pageIndex,
            pageSize: query.pageSize,
            keyword: query.searchText || undefined,
            frequency: (filters.frequency as string | undefined) ?? undefined,
            isActive: (filters.isActive as boolean | undefined) ?? undefined,
          } as never)) as never,
        )
        return toPaged(result, query)
      },
      getById: async (id) => unwrap<SalaryStructureDto | null>(await api.structures.get(id)),
      create: async (data) => unwrap<SalaryStructureDto>(await api.structures.create(data)),
      update: async (id, data) => unwrap<SalaryStructureDto>(await api.structures.update(id, data)),
      delete: async (ids) => {
        for (const id of ids) ensureOk(await api.structures.delete(id))
      },
    },

    brackets: {
      fetch: async (query) => {
        const filters = query.filters ?? {}
        const result = unwrap<PayrollPagedResult<BracketTableListDto>>(
          (await api.brackets.getList({
            pageIndex: query.pageIndex,
            pageSize: query.pageSize,
            keyword: query.searchText || undefined,
            code: (filters.code as string | undefined) ?? undefined,
            isActive: (filters.isActive as boolean | undefined) ?? undefined,
          } as never)) as never,
        )
        return toPaged(result, query)
      },
      getById: async (id) => unwrap<BracketTableDto | null>(await api.brackets.get(id)),
      resolve: async (code, asOf) => unwrap<BracketTableDto>(await api.brackets.resolve(code, asOf)),
      create: async (data) => unwrap<BracketTableDto>(await api.brackets.create(data)),
      update: async (id, data) => unwrap<BracketTableDto>(await api.brackets.update(id, data)),
      delete: async (ids) => {
        for (const id of ids) ensureOk(await api.brackets.delete(id))
      },
    },

    runs: {
      fetch: async (query) => {
        const filters = query.filters ?? {}
        const result = unwrap<PayrollPagedResult<PayRunListDto>>(
          (await api.runs.getList({
            pageIndex: query.pageIndex,
            pageSize: query.pageSize,
            keyword: query.searchText || undefined,
            status: (filters.status as string | undefined) ?? undefined,
            source: (filters.source as string | undefined) ?? undefined,
            dateFrom: (filters.dateFrom as string | undefined) ?? undefined,
            dateTo: (filters.dateTo as string | undefined) ?? undefined,
          } as never)) as never,
        )
        return toPaged(result, query)
      },
      getById: async (id) => unwrap<PayRunDto | null>(await api.runs.get(id)),
      createDraft: async (data) => unwrap<PayRunDto>(await api.runs.createDraft(data)),
      updateDraft: async (id, data) => unwrap<PayRunDto>(await api.runs.updateDraft(id, data)),
      deleteDraft: async (id) => {
        ensureOk(await api.runs.deleteDraft(id))
      },
      calculate: async (id) => unwrap<PayRunDto>(await api.runs.calculate(id)),
      post: async (id) => unwrap<PayRunDto>(await api.runs.post(id)),
      pay: async (id, data) => unwrap<PayRunDto>(await api.runs.pay(id, data)),
      voidRun: async (id) => unwrap<PayRunDto>(await api.runs.void(id)),
      payslips: async (id) => unwrap<PayslipListDto[]>(await api.runs.getPayslips(id)) ?? [],
      payslip: async (id, payslipId) => unwrap<PayslipDto | null>(await api.runs.getPayslip(id, payslipId)),
      updatePayslipInputs: async (id, payslipId, data) =>
        unwrap<PayslipDto>(await api.runs.updatePayslipInputs(id, payslipId, data)),
      createFromExternal: async (data) => unwrap<PayRunDto>(await api.runs.createFromExternal(data)),
    },

    countryPacks: {
      fetch: async (query) =>
        pageArray(
          unwrap<CountryPackDto[]>(await api.countryPacks.getRegistered()) ?? [],
          { searchText: '', filters: {}, ...query },
        ),
      seed: async (code) => unwrap<CountryPackSeedResult>(await api.countryPacks.seed(code)),
    },
  }
}
