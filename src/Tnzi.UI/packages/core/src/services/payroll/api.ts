/**
 * Payroll Module API - admin endpoints of Tnzi.Finance.Payroll
 * (`/admin/payroll/*` exposed by the module's six `DefaultPayroll*AdminController`s:
 * employees, components, structures, brackets, pay runs, country packs).
 *
 * Note the payslip sub-resources are NESTED under the run
 * (`runs/{id}/payslips/{payslipId}` / `.../inputs`) and the external ingestion
 * endpoint is `runs/external` - matching the backend controller routes.
 */

import type { HttpClient } from '../../http/http'
import type { PagedList } from '../../types/pagination'
import type {
  EmployeeDto,
  CreateEmployeeDto,
  UpdateEmployeeDto,
  EmployeeQueryDto,
  SalaryAssignmentDto,
  CreateSalaryAssignmentDto,
  SalaryComponentDto,
  CreateSalaryComponentDto,
  UpdateSalaryComponentDto,
  SalaryComponentQueryDto,
  SalaryStructureDto,
  SalaryStructureListDto,
  CreateSalaryStructureDto,
  UpdateSalaryStructureDto,
  SalaryStructureQueryDto,
  BracketTableDto,
  BracketTableListDto,
  CreateBracketTableDto,
  UpdateBracketTableDto,
  BracketTableQueryDto,
  PayRunDto,
  PayRunListDto,
  CreatePayRunDto,
  UpdatePayRunDto,
  PayRunQueryDto,
  PayRunPaymentDto,
  PayslipDto,
  PayslipListDto,
  UpdatePayslipInputsDto,
  ExternalPayRunIngestDto,
  CountryPackDto,
  CountryPackSeedResult,
} from './types'

const ADMIN_EMPLOYEE_BASE = '/admin/payroll/employees'
const ADMIN_COMPONENT_BASE = '/admin/payroll/components'
const ADMIN_STRUCTURE_BASE = '/admin/payroll/structures'
const ADMIN_BRACKET_BASE = '/admin/payroll/brackets'
const ADMIN_RUN_BASE = '/admin/payroll/runs'
const ADMIN_COUNTRY_PACK_BASE = '/admin/payroll/country-packs'

/**
 * Admin Payroll API - a single factory returning one group per resource
 * (employees, components, structures, brackets, runs, country packs).
 */
export function useAdminPayrollApi(client: HttpClient) {
  return {
    /** Employees (+ shadow-vendor + salary-assignment sub-resource). */
    employees: {
      getList: (params?: EmployeeQueryDto) =>
        client.get<PagedList<EmployeeDto>>(ADMIN_EMPLOYEE_BASE, { params }),
      get: (id: string) => client.get<EmployeeDto>(`${ADMIN_EMPLOYEE_BASE}/${id}`),
      create: (data: CreateEmployeeDto) => client.post<EmployeeDto>(ADMIN_EMPLOYEE_BASE, data),
      update: (id: string, data: UpdateEmployeeDto) =>
        client.put<EmployeeDto>(`${ADMIN_EMPLOYEE_BASE}/${id}`, data),
      delete: (id: string) => client.delete<void>(`${ADMIN_EMPLOYEE_BASE}/${id}`),
      /** Idempotently ensure the employee has a shadow vendor (payee for real A/P flows). */
      ensureVendor: (id: string) =>
        client.post<EmployeeDto>(`${ADMIN_EMPLOYEE_BASE}/${id}/ensure-vendor`),
      getAssignments: (id: string) =>
        client.get<SalaryAssignmentDto[]>(`${ADMIN_EMPLOYEE_BASE}/${id}/assignments`),
      createAssignment: (id: string, data: CreateSalaryAssignmentDto) =>
        client.post<SalaryAssignmentDto>(`${ADMIN_EMPLOYEE_BASE}/${id}/assignments`, data),
      deleteAssignment: (id: string, assignmentId: string) =>
        client.delete<void>(`${ADMIN_EMPLOYEE_BASE}/${id}/assignments/${assignmentId}`),
    },

    /** Salary components. */
    components: {
      getList: (params?: SalaryComponentQueryDto) =>
        client.get<PagedList<SalaryComponentDto>>(ADMIN_COMPONENT_BASE, { params }),
      get: (id: string) => client.get<SalaryComponentDto>(`${ADMIN_COMPONENT_BASE}/${id}`),
      create: (data: CreateSalaryComponentDto) =>
        client.post<SalaryComponentDto>(ADMIN_COMPONENT_BASE, data),
      update: (id: string, data: UpdateSalaryComponentDto) =>
        client.put<SalaryComponentDto>(`${ADMIN_COMPONENT_BASE}/${id}`, data),
      delete: (id: string) => client.delete<void>(`${ADMIN_COMPONENT_BASE}/${id}`),
    },

    /** Salary structures (list projection has no lines; get returns the full record). */
    structures: {
      getList: (params?: SalaryStructureQueryDto) =>
        client.get<PagedList<SalaryStructureListDto>>(ADMIN_STRUCTURE_BASE, { params }),
      get: (id: string) => client.get<SalaryStructureDto>(`${ADMIN_STRUCTURE_BASE}/${id}`),
      create: (data: CreateSalaryStructureDto) =>
        client.post<SalaryStructureDto>(ADMIN_STRUCTURE_BASE, data),
      update: (id: string, data: UpdateSalaryStructureDto) =>
        client.put<SalaryStructureDto>(`${ADMIN_STRUCTURE_BASE}/${id}`, data),
      delete: (id: string) => client.delete<void>(`${ADMIN_STRUCTURE_BASE}/${id}`),
    },

    /** Bracket tables (list projection has no rows; get / resolve return the full record). */
    brackets: {
      getList: (params?: BracketTableQueryDto) =>
        client.get<PagedList<BracketTableListDto>>(ADMIN_BRACKET_BASE, { params }),
      /** Resolve the version effective on a date (rows included). */
      resolve: (code: string, asOf: string) =>
        client.get<BracketTableDto>(`${ADMIN_BRACKET_BASE}/resolve`, { params: { code, asOf } }),
      get: (id: string) => client.get<BracketTableDto>(`${ADMIN_BRACKET_BASE}/${id}`),
      create: (data: CreateBracketTableDto) =>
        client.post<BracketTableDto>(ADMIN_BRACKET_BASE, data),
      update: (id: string, data: UpdateBracketTableDto) =>
        client.put<BracketTableDto>(`${ADMIN_BRACKET_BASE}/${id}`, data),
      delete: (id: string) => client.delete<void>(`${ADMIN_BRACKET_BASE}/${id}`),
    },

    /** Pay runs (draft CRUD + calculate / post / pay / void + payslip sub-resource + external ingestion). */
    runs: {
      getList: (params?: PayRunQueryDto) =>
        client.get<PagedList<PayRunListDto>>(ADMIN_RUN_BASE, { params }),
      get: (id: string) => client.get<PayRunDto>(`${ADMIN_RUN_BASE}/${id}`),
      createDraft: (data: CreatePayRunDto) => client.post<PayRunDto>(ADMIN_RUN_BASE, data),
      updateDraft: (id: string, data: UpdatePayRunDto) =>
        client.put<PayRunDto>(`${ADMIN_RUN_BASE}/${id}`, data),
      deleteDraft: (id: string) => client.delete<void>(`${ADMIN_RUN_BASE}/${id}`),
      /** Calculate / recalculate the run (Draft | Calculated → Calculated). */
      calculate: (id: string) => client.post<PayRunDto>(`${ADMIN_RUN_BASE}/${id}/calculate`),
      /** Post the run to the general ledger (Calculated → Posted; blocked when errorCount > 0). */
      post: (id: string) => client.post<PayRunDto>(`${ADMIN_RUN_BASE}/${id}/post`),
      /** Pay (Posted | PartiallyPaid → PartiallyPaid | Paid); may be called repeatedly. */
      pay: (id: string, data: PayRunPaymentDto) =>
        client.post<PayRunDto>(`${ADMIN_RUN_BASE}/${id}/pay`, data),
      /** Void (Posted and later → Voided; reverses every voucher). */
      void: (id: string) => client.post<PayRunDto>(`${ADMIN_RUN_BASE}/${id}/void`),
      getPayslips: (id: string) =>
        client.get<PayslipListDto[]>(`${ADMIN_RUN_BASE}/${id}/payslips`),
      getPayslip: (id: string, payslipId: string) =>
        client.get<PayslipDto>(`${ADMIN_RUN_BASE}/${id}/payslips/${payslipId}`),
      updatePayslipInputs: (id: string, payslipId: string, data: UpdatePayslipInputsDto) =>
        client.put<PayslipDto>(`${ADMIN_RUN_BASE}/${id}/payslips/${payslipId}/inputs`, data),
      /** Idempotent external ingestion (External / OpeningBalance). */
      createFromExternal: (data: ExternalPayRunIngestDto) =>
        client.post<PayRunDto>(`${ADMIN_RUN_BASE}/external`, data),
    },

    /** Country packs (list registered packs + trigger idempotent seeding by code). */
    countryPacks: {
      getRegistered: () => client.get<CountryPackDto[]>(ADMIN_COUNTRY_PACK_BASE),
      seed: (code: string) =>
        client.post<CountryPackSeedResult>(`${ADMIN_COUNTRY_PACK_BASE}/${code}/seed`),
    },
  }
}
