import { EMPTY_DASH } from '../../utils/placeholders'
import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { formatDateOnly } from '@tnzi/core'
import {
  SalaryComponentType,
  PayFrequency,
  type CreateSalaryComponentDto,
  type UpdateSalaryComponentDto,
  type CreateSalaryStructureDto,
  type UpdateSalaryStructureDto,
  type CreateBracketTableDto,
  type UpdateBracketTableDto,
  type SalaryStructureLineInputDto,
  type BracketRowInputDto,
} from '../../services/bridges/payroll-bridge'

/** lowerCamel the PascalCase enum member for i18n key lookup. */
export function enumKey(value?: string | null): string {
  if (!value) return ''
  return value.charAt(0).toLowerCase() + value.slice(1)
}

// ── Components ────────────────────────────────────────────────────
export interface ComponentRow {
  id?: string
  code?: string
  name?: string
  type?: SalaryComponentType
  formula?: string | null
  condition?: string | null
  defaultAmount?: number | null
  expenseAccountId?: string | null
  liabilityAccountId?: string | null
  isActive?: boolean
  description?: string | null
}

export function buildComponentColumns(t: (key: string) => string): ColumnDef<ComponentRow>[] {
  return [
    { key: 'code', title: 'columns.code', width: 130, primary: true },
    { key: 'name', title: 'columns.name', minWidth: 150 },
    { key: 'type', title: 'columns.type', width: 150, render: (r) => t(`componentType.${enumKey(r.type)}`) },
    { key: 'formula', title: 'columns.formula', minWidth: 180, mobileHidden: true, render: (r) => r.formula ?? t('columns.fixedAmount') },
    {
      key: 'isActive',
      title: 'columns.status',
      width: 100,
      render: (r) => h(TStatusBadge, { value: r.isActive === false ? 0 : 1, type: r.isActive === false ? 'default' : 'success', label: r.isActive === false ? t('status.inactive') : t('status.active') }),
    },
  ]
}

export const componentFormSchema: FormSchemaItem[] = [
  { key: 'code', labelKey: 'form.code', label: 'Code', type: 'text', required: true },
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'text', required: true },
  {
    key: 'type',
    labelKey: 'form.type',
    label: 'Type',
    type: 'select',
    required: true,
    options: [
      { label: 'Earning', value: SalaryComponentType.Earning, labelKey: 'componentType.earning' },
      { label: 'Deduction', value: SalaryComponentType.Deduction, labelKey: 'componentType.deduction' },
      { label: 'Employer Contribution', value: SalaryComponentType.EmployerContribution, labelKey: 'componentType.employerContribution' },
      // 备注项:印在工资条上、可被后续公式引用,但不进任何合计也不产生分录。
      { label: 'Informational', value: SalaryComponentType.Informational, labelKey: 'componentType.informational' },
    ],
  },
  { key: 'formula', labelKey: 'form.formula', label: 'Formula', type: 'textarea' },
  { key: 'condition', labelKey: 'form.condition', label: 'Condition', type: 'text' },
  { key: 'defaultAmount', labelKey: 'form.defaultAmount', label: 'Default Amount', type: 'number' },
  { key: 'expenseAccountId', labelKey: 'form.expenseAccount', label: 'Expense Account', type: 'payroll-account' },
  { key: 'liabilityAccountId', labelKey: 'form.liabilityAccount', label: 'Liability Account', type: 'payroll-account' },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'textarea' },
  { key: 'isActive', labelKey: 'form.isActive', label: 'Active', type: 'switch', visible: (m) => !!m.id },
]

export function toComponentPayload(d: Record<string, unknown>): UpdateSalaryComponentDto & CreateSalaryComponentDto {
  return {
    code: String(d.code ?? '').trim(),
    name: String(d.name ?? '').trim(),
    type: (d.type as SalaryComponentType) ?? SalaryComponentType.Earning,
    formula: (d.formula as string | null) || null,
    condition: (d.condition as string | null) || null,
    defaultAmount: d.defaultAmount == null || d.defaultAmount === '' ? null : Number(d.defaultAmount),
    expenseAccountId: (d.expenseAccountId as string | null) || null,
    liabilityAccountId: (d.liabilityAccountId as string | null) || null,
    description: (d.description as string | null) || null,
    isActive: d.isActive !== false,
  }
}

// ── Structures ────────────────────────────────────────────────────
export interface StructureRow {
  id?: string
  name?: string
  description?: string | null
  frequency?: PayFrequency
  isActive?: boolean
  lines?: SalaryStructureLineInputDto[]
}

export function buildStructureColumns(t: (key: string) => string): ColumnDef<StructureRow>[] {
  return [
    { key: 'name', title: 'columns.name', minWidth: 160, primary: true },
    { key: 'frequency', title: 'columns.frequency', width: 130, render: (r) => t(`frequency.${enumKey(r.frequency)}`) },
    { key: 'description', title: 'columns.description', minWidth: 200, mobileHidden: true, render: (r) => r.description ?? EMPTY_DASH },
    {
      key: 'isActive',
      title: 'columns.status',
      width: 100,
      render: (r) => h(TStatusBadge, { value: r.isActive === false ? 0 : 1, type: r.isActive === false ? 'default' : 'success', label: r.isActive === false ? t('status.inactive') : t('status.active') }),
    },
  ]
}

export const structureFormSchema: FormSchemaItem[] = [
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'text', required: true },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'textarea' },
  {
    key: 'frequency',
    labelKey: 'form.frequency',
    label: 'Frequency',
    type: 'select',
    required: true,
    options: [
      { label: 'Monthly', value: PayFrequency.Monthly, labelKey: 'frequency.monthly' },
      { label: 'Semi-monthly', value: PayFrequency.SemiMonthly, labelKey: 'frequency.semiMonthly' },
      { label: 'Bi-weekly', value: PayFrequency.BiWeekly, labelKey: 'frequency.biWeekly' },
      { label: 'Weekly', value: PayFrequency.Weekly, labelKey: 'frequency.weekly' },
    ],
  },
  { key: 'lines', labelKey: 'form.lines', label: 'Lines', type: 'payroll-structure-lines', required: true },
  { key: 'isActive', labelKey: 'form.isActive', label: 'Active', type: 'switch', visible: (m) => !!m.id },
]

export function toStructurePayload(d: Record<string, unknown>): UpdateSalaryStructureDto & CreateSalaryStructureDto {
  const lines = (d.lines as SalaryStructureLineInputDto[] | undefined) ?? []
  return {
    name: String(d.name ?? '').trim(),
    description: (d.description as string | null) || null,
    frequency: (d.frequency as PayFrequency) ?? PayFrequency.Monthly,
    lines: lines.map((l) => ({
      componentId: String(l.componentId ?? ''),
      sequence: Number(l.sequence ?? 0),
      formulaOverride: l.formulaOverride || null,
      amountOverride: l.amountOverride == null ? null : Number(l.amountOverride),
      conditionOverride: l.conditionOverride || null,
    })),
    isActive: d.isActive !== false,
  }
}

// ── Brackets ──────────────────────────────────────────────────────
export interface BracketRow {
  id?: string
  code?: string
  name?: string
  description?: string | null
  effectiveFrom?: string
  isActive?: boolean
  rows?: BracketRowInputDto[]
}

export function buildBracketColumns(t: (key: string) => string): ColumnDef<BracketRow>[] {
  return [
    { key: 'code', title: 'columns.code', width: 150, primary: true },
    { key: 'name', title: 'columns.name', minWidth: 160 },
    { key: 'effectiveFrom', title: 'columns.effectiveFrom', width: 130, render: (r) => formatDateOnly(r.effectiveFrom, { utc: true }) },
    {
      key: 'isActive',
      title: 'columns.status',
      width: 100,
      render: (r) => h(TStatusBadge, { value: r.isActive === false ? 0 : 1, type: r.isActive === false ? 'default' : 'success', label: r.isActive === false ? t('status.inactive') : t('status.active') }),
    },
  ]
}

export const bracketFormSchema: FormSchemaItem[] = [
  { key: 'code', labelKey: 'form.code', label: 'Code', type: 'text', required: true },
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'text', required: true },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'textarea' },
  { key: 'effectiveFrom', labelKey: 'form.effectiveFrom', label: 'Effective From', type: 'date', required: true },
  { key: 'rows', labelKey: 'form.rows', label: 'Rows', type: 'payroll-bracket-rows', required: true },
  { key: 'isActive', labelKey: 'form.isActive', label: 'Active', type: 'switch', visible: (m) => !!m.id },
]

// ── Country packs ─────────────────────────────────────────────────
export interface PackRow {
  code?: string
  displayName?: string
  description?: string | null
}

export function buildPackColumns(t: (key: string) => string): ColumnDef<PackRow>[] {
  return [
    { key: 'code', title: t('packs.columns.code'), width: 100 },
    { key: 'displayName', title: t('packs.columns.name'), minWidth: 160, primary: true },
    { key: 'description', title: t('packs.columns.description'), minWidth: 240 },
  ]
}

export function toBracketPayload(d: Record<string, unknown>, toIso: (v: unknown) => string): UpdateBracketTableDto & CreateBracketTableDto {
  const rows = (d.rows as BracketRowInputDto[] | undefined) ?? []
  return {
    code: String(d.code ?? '').trim(),
    name: String(d.name ?? '').trim(),
    description: (d.description as string | null) || null,
    effectiveFrom: toIso(d.effectiveFrom),
    rows: rows.map((r) => ({
      sequence: Number(r.sequence ?? 0),
      lowerBound: Number(r.lowerBound ?? 0),
      upperBound: r.upperBound == null ? null : Number(r.upperBound),
      rate: Number(r.rate ?? 0),
      quickDeduction: r.quickDeduction == null ? null : Number(r.quickDeduction),
    })),
    isActive: d.isActive !== false,
  }
}
