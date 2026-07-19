/**
 * Payroll Module Metadata - enums aligned with the Tnzi.Finance.Payroll backend.
 *
 * The framework serializes every enum wire value as its PascalCase member name
 * (global `JsonStringEnumConverter`, see Tnzi.AspNetCore), so these enums use
 * string values that match the C# member names. Responses carry the string
 * (e.g. `status: "Draft"`); requests still accept both the string and the
 * legacy integer (converter `allowIntegerValues` default), so sending a member
 * value on create/update is safe.
 *
 * The `*_LABELS` maps are the canonical English fallbacks (the admin pages
 * localize through i18n; consumers without an i18n layer read these).
 */

/** Salary component type (drives the posting direction and the net-pay effect). */
export enum SalaryComponentType {
  /** Earning: Dr expense account; adds to gross and net. */
  Earning = 'Earning',
  /** Deduction: Cr liability account; subtracts from net. */
  Deduction = 'Deduction',
  /** Employer contribution: Dr expense + Cr liability (both sides); does not touch net. */
  EmployerContribution = 'EmployerContribution',
}

export const SALARY_COMPONENT_TYPE_LABELS: Record<SalaryComponentType, string> = {
  [SalaryComponentType.Earning]: 'Earning',
  [SalaryComponentType.Deduction]: 'Deduction',
  [SalaryComponentType.EmployerContribution]: 'Employer Contribution',
}

/** Pay frequency (periods per year: 12 / 24 / 26 / 52). */
export enum PayFrequency {
  Monthly = 'Monthly',
  SemiMonthly = 'SemiMonthly',
  BiWeekly = 'BiWeekly',
  Weekly = 'Weekly',
}

export const PAY_FREQUENCY_LABELS: Record<PayFrequency, string> = {
  [PayFrequency.Monthly]: 'Monthly',
  [PayFrequency.SemiMonthly]: 'Semi-monthly',
  [PayFrequency.BiWeekly]: 'Bi-weekly',
  [PayFrequency.Weekly]: 'Weekly',
}

/** Pay-run lifecycle status. */
export enum PayRunStatus {
  Draft = 'Draft',
  Calculated = 'Calculated',
  Posted = 'Posted',
  PartiallyPaid = 'PartiallyPaid',
  Paid = 'Paid',
  Voided = 'Voided',
}

export const PAY_RUN_STATUS_LABELS: Record<PayRunStatus, string> = {
  [PayRunStatus.Draft]: 'Draft',
  [PayRunStatus.Calculated]: 'Calculated',
  [PayRunStatus.Posted]: 'Posted',
  [PayRunStatus.PartiallyPaid]: 'Partially Paid',
  [PayRunStatus.Paid]: 'Paid',
  [PayRunStatus.Voided]: 'Voided',
}

/** Pay-run source. */
export enum PayRunSource {
  /** Calculated in-house by this module's formula engine. */
  Internal = 'Internal',
  /** Ingested from an external embedded payroll provider. */
  External = 'External',
  /** Opening balance (mid-year go-live YTD seed; never posts, never hits the GL, only feeds Ytd()). */
  OpeningBalance = 'OpeningBalance',
}

export const PAY_RUN_SOURCE_LABELS: Record<PayRunSource, string> = {
  [PayRunSource.Internal]: 'Internal',
  [PayRunSource.External]: 'External',
  [PayRunSource.OpeningBalance]: 'Opening Balance',
}

/** Single payslip payment status. */
export enum PayslipPaymentStatus {
  Unpaid = 'Unpaid',
  Paid = 'Paid',
}

export const PAYSLIP_PAYMENT_STATUS_LABELS: Record<PayslipPaymentStatus, string> = {
  [PayslipPaymentStatus.Unpaid]: 'Unpaid',
  [PayslipPaymentStatus.Paid]: 'Paid',
}

/** Ytd() year-to-date aggregation basis. */
export enum YtdBasis {
  CalendarYear = 'CalendarYear',
  FiscalYear = 'FiscalYear',
}

export const YTD_BASIS_LABELS: Record<YtdBasis, string> = {
  [YtdBasis.CalendarYear]: 'Calendar Year',
  [YtdBasis.FiscalYear]: 'Fiscal Year',
}
