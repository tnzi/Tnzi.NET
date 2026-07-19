/**
 * Payroll Module Types - employees, salary components/structures, bracket
 * tables, pay runs and payslips.
 * Aligned with the Tnzi.Finance.Payroll backend (`Tnzi.Finance.Payroll/Dtos`).
 *
 * Date-only fields (hireDate / terminationDate / effectiveFrom / periodStart /
 * periodEnd / payDate) are UTC-midnight ISO strings; render with
 * `formatDateOnly(v, { utc: true })` and round-trip through calendar-day
 * helpers (never `new Date(iso).getTime()`).
 */

import type { PagedQueryDto } from '../../types/pagination'
import {
  SalaryComponentType,
  PayFrequency,
  PayRunStatus,
  PayRunSource,
  PayslipPaymentStatus,
} from './metadata'

export {
  SalaryComponentType,
  PayFrequency,
  PayRunStatus,
  PayRunSource,
  PayslipPaymentStatus,
  YtdBasis,
} from './metadata'

// ============================================
// Employee + salary assignment
// ============================================

export interface EmployeeDto {
  id: string
  code: string
  name: string
  email?: string | null
  phone?: string | null
  hireDate?: string | null
  terminationDate?: string | null
  vendorId?: string | null
  userId?: string | null
  /** Scalar JSON object feeding the Attr()/AttrText() formula functions. */
  attributesJson?: string | null
  isActive: boolean
  notes?: string | null
  creationTime: string
}

export interface CreateEmployeeDto {
  code: string
  name: string
  email?: string | null
  phone?: string | null
  hireDate?: string | null
  terminationDate?: string | null
  userId?: string | null
  attributesJson?: string | null
  notes?: string | null
}

export interface UpdateEmployeeDto extends CreateEmployeeDto {
  isActive: boolean
}

export interface EmployeeQueryDto extends PagedQueryDto {
  keyword?: string | null
  isActive?: boolean | null
}

export interface SalaryAssignmentDto {
  id: string
  employeeId: string
  structureId: string
  /** Structure name (list display). */
  structureName: string
  effectiveFrom: string
  baseAmount: number
  notes?: string | null
  creationTime: string
}

/** Create a salary assignment (correction = delete + recreate; no update endpoint). */
export interface CreateSalaryAssignmentDto {
  structureId: string
  effectiveFrom: string
  baseAmount: number
  notes?: string | null
}

// ============================================
// Salary component
// ============================================

export interface SalaryComponentDto {
  id: string
  code: string
  name: string
  type: SalaryComponentType
  formula?: string | null
  condition?: string | null
  defaultAmount?: number | null
  expenseAccountId?: string | null
  liabilityAccountId?: string | null
  isActive: boolean
  description?: string | null
  creationTime: string
}

export interface CreateSalaryComponentDto {
  code: string
  name: string
  type: SalaryComponentType
  formula?: string | null
  condition?: string | null
  defaultAmount?: number | null
  expenseAccountId?: string | null
  liabilityAccountId?: string | null
  description?: string | null
}

export interface UpdateSalaryComponentDto extends CreateSalaryComponentDto {
  isActive: boolean
}

export interface SalaryComponentQueryDto extends PagedQueryDto {
  keyword?: string | null
  type?: SalaryComponentType | null
  isActive?: boolean | null
}

// ============================================
// Salary structure (+ lines)
// ============================================

export interface SalaryStructureLineDto {
  id: string
  componentId: string
  componentCode: string
  componentName: string
  componentType: SalaryComponentType
  sequence: number
  formulaOverride?: string | null
  amountOverride?: number | null
  conditionOverride?: string | null
}

export interface SalaryStructureDto {
  id: string
  name: string
  description?: string | null
  frequency: PayFrequency
  isActive: boolean
  creationTime: string
  lines: SalaryStructureLineDto[]
}

/** Paged list projection (no lines). */
export interface SalaryStructureListDto {
  id: string
  name: string
  description?: string | null
  frequency: PayFrequency
  isActive: boolean
  creationTime: string
}

export interface SalaryStructureLineInputDto {
  componentId: string
  sequence: number
  formulaOverride?: string | null
  amountOverride?: number | null
  conditionOverride?: string | null
}

export interface CreateSalaryStructureDto {
  name: string
  description?: string | null
  frequency: PayFrequency
  lines: SalaryStructureLineInputDto[]
}

export interface UpdateSalaryStructureDto extends CreateSalaryStructureDto {
  isActive: boolean
}

export interface SalaryStructureQueryDto extends PagedQueryDto {
  keyword?: string | null
  frequency?: PayFrequency | null
  isActive?: boolean | null
}

// ============================================
// Bracket table (+ rows)
// ============================================

export interface BracketRowDto {
  id: string
  sequence: number
  lowerBound: number
  upperBound?: number | null
  rate: number
  quickDeduction?: number | null
}

export interface BracketTableDto {
  id: string
  code: string
  name: string
  description?: string | null
  effectiveFrom: string
  isActive: boolean
  creationTime: string
  rows: BracketRowDto[]
}

/** Paged list projection (no rows). */
export interface BracketTableListDto {
  id: string
  code: string
  name: string
  description?: string | null
  effectiveFrom: string
  isActive: boolean
  creationTime: string
}

export interface BracketRowInputDto {
  sequence: number
  lowerBound: number
  upperBound?: number | null
  rate: number
  quickDeduction?: number | null
}

export interface CreateBracketTableDto {
  code: string
  name: string
  description?: string | null
  effectiveFrom: string
  rows: BracketRowInputDto[]
}

export interface UpdateBracketTableDto extends CreateBracketTableDto {
  isActive: boolean
}

export interface BracketTableQueryDto extends PagedQueryDto {
  keyword?: string | null
  /** Exact code (list every version of one code). */
  code?: string | null
  isActive?: boolean | null
}

// ============================================
// Pay run (+ payslips)
// ============================================

export interface PayRunDto {
  id: string
  /** Assigned at posting; null while draft. */
  number?: string | null
  status: PayRunStatus
  periodStart: string
  periodEnd: string
  payDate: string
  frequency: PayFrequency
  structureId?: string | null
  structureName?: string | null
  memo?: string | null
  source: PayRunSource
  providerRunId?: string | null
  employeeCount: number
  /** Payslips that failed to calculate (> 0 blocks posting). */
  errorCount: number
  grossTotal: number
  deductionTotal: number
  employerCostTotal: number
  netTotal: number
  creationTime: string
}

export interface PayRunListDto {
  id: string
  number?: string | null
  status: PayRunStatus
  periodStart: string
  periodEnd: string
  payDate: string
  frequency: PayFrequency
  source: PayRunSource
  employeeCount: number
  grossTotal: number
  netTotal: number
  creationTime: string
}

export interface CreatePayRunDto {
  periodStart: string
  periodEnd: string
  payDate: string
  frequency: PayFrequency
  /** Salary-structure filter (null = every employee with an assignment). */
  structureId?: string | null
  memo?: string | null
}

/** Update a draft pay run (Draft state only). */
export type UpdatePayRunDto = CreatePayRunDto

export interface PayRunQueryDto extends PagedQueryDto {
  status?: PayRunStatus | null
  source?: PayRunSource | null
  dateFrom?: string | null
  dateTo?: string | null
  keyword?: string | null
}

/** Payment request (Posted / PartiallyPaid state; may be called repeatedly to settle in full). */
export interface PayRunPaymentDto {
  /** Employees to pay (null = every unpaid payslip in the run). */
  employeeIds?: string[] | null
  /** Funds account (must be a postable CashEquivalent leaf). */
  paymentAccountId: string
  paymentDate: string
  /** Payment method (free-form, e.g. BankTransfer / Check). */
  paymentMethod?: string | null
  reference?: string | null
}

export interface PayslipLineDto {
  id: string
  sequence: number
  componentId: string
  componentCode: string
  componentName: string
  componentType: SalaryComponentType
  amount: number
  /** Year-to-date accumulated amount for this component (incl. this period). */
  ytdAmount: number
  formulaSnapshot?: string | null
  expenseAccountId?: string | null
  liabilityAccountId?: string | null
}

export interface PayslipDto {
  id: string
  payRunId: string
  employeeId: string
  employeeCode: string
  employeeName: string
  structureId: string
  baseAmount: number
  periodDays: number
  workedDays: number
  grossPay: number
  totalDeductions: number
  employerCost: number
  netPay: number
  calculationError?: string | null
  journalEntryId?: string | null
  paymentJournalEntryId?: string | null
  paymentStatus: PayslipPaymentStatus
  paymentMethod?: string | null
  lines: PayslipLineDto[]
}

export interface PayslipListDto {
  id: string
  employeeId: string
  employeeCode: string
  employeeName: string
  grossPay: number
  totalDeductions: number
  employerCost: number
  netPay: number
  calculationError?: string | null
  paymentStatus: PayslipPaymentStatus
}

/** Modify one payslip's inputs and recalculate it (Calculated state only). */
export interface UpdatePayslipInputsDto {
  /** Actual days worked (formula variable WORKED_DAYS). */
  workedDays: number
}

// ============================================
// Country pack + external ingestion
// ============================================

export interface CountryPackDto {
  /** Country / region code, e.g. "US" / "CA" / "CN". */
  code: string
  displayName: string
  description?: string | null
}

export interface CountryPackSeedResult {
  componentsSeeded: number
  bracketTablesSeeded: number
}

export interface ExternalPayslipLineDto {
  /** Locally-registered component code (drives the posting direction + accounts). */
  componentCode: string
  /** Amount (base currency, already computed externally). */
  amount: number
}

export interface ExternalPayslipDto {
  /** Locally-existing employee code. */
  employeeCode: string
  /** Actual days worked (null = period total). */
  workedDays?: number | null
  lines: ExternalPayslipLineDto[]
}

/**
 * External pay-run ingestion request (embedded provider or historical
 * migration). Idempotent by `providerRunId`; each line carries a locally
 * registered component code. `source` = OpeningBalance seeds YTD only (never
 * posts, never hits the GL).
 */
export interface ExternalPayRunIngestDto {
  /** External run identifier (idempotency key, required). */
  providerRunId: string
  /** Source (External or OpeningBalance). */
  source: PayRunSource
  periodStart: string
  periodEnd: string
  payDate: string
  frequency: PayFrequency
  memo?: string | null
  payslips: ExternalPayslipDto[]
}
