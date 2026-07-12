/**
 * Finance Module Types - Chart of accounts, general ledger, and reports
 * Aligned with Tnzi.NET backend Finance module (Tnzi.Finance/Dtos)
 */

import type { PagedQueryDto } from '../../types/pagination';
import {
  AccountRootType,
  AccountSystemRole,
  CashFlowActivity,
  JournalEntryStatus,
  FinanceDocumentStatus,
  PaymentDirection,
  FinancePartyType,
  SettlementDocType,
  ItemType,
  ReconciliationStatus,
} from './metadata';

export { AccountRootType, AccountSystemRole, CashFlowActivity, JournalEntryStatus };

// ============================================
// Chart of Accounts
// ============================================

export interface AccountDto {
  id: string;
  code: string;
  name: string;
  description?: string | null;
  rootType: AccountRootType;
  subType?: string | null;
  parentId?: string | null;
  isGroup: boolean;
  currency?: string | null;
  systemRole?: AccountSystemRole | null;
  cashFlowActivity?: CashFlowActivity | null;
  isActive: boolean;
  creationTime?: string;
}

export interface AccountTreeDto extends AccountDto {
  children: AccountTreeDto[];
}

export interface CreateAccountDto {
  code: string;
  name: string;
  description?: string | null;
  rootType: AccountRootType;
  subType?: string | null;
  parentId?: string | null;
  isGroup?: boolean;
  currency?: string | null;
  systemRole?: AccountSystemRole | null;
  cashFlowActivity?: CashFlowActivity | null;
}

/** Full update; rootType is immutable after creation */
export interface UpdateAccountDto {
  code: string;
  name: string;
  description?: string | null;
  subType?: string | null;
  parentId?: string | null;
  currency?: string | null;
  systemRole?: AccountSystemRole | null;
  cashFlowActivity?: CashFlowActivity | null;
  isActive?: boolean;
}

export interface AccountQueryDto extends PagedQueryDto {
  keyword?: string | null;
  rootType?: AccountRootType | null;
  isActive?: boolean | null;
}

// ============================================
// Journal Entries (General Ledger)
// ============================================

export interface JournalLineDto {
  id: string;
  lineNumber: number;
  accountId: string;
  accountCode?: string | null;
  accountName?: string | null;
  /** Base-currency debit amount */
  debit: number;
  /** Base-currency credit amount */
  credit: number;
  /** Transaction-currency debit amount */
  txnDebit: number;
  /** Transaction-currency credit amount */
  txnCredit: number;
  currency: string;
  memo?: string | null;
  partyType?: string | null;
  partyId?: string | null;
  dimensions?: string | null;
  /** Structured tax dimension (tax lines only; drives the tax summary report) */
  taxRateId?: string | null;
}

export interface JournalEntryDto {
  id: string;
  /** Assigned at posting; null while draft */
  number?: string | null;
  status: JournalEntryStatus;
  postingDate: string;
  memo?: string | null;
  currency: string;
  exchangeRate: number;
  sourceType?: string | null;
  sourceId?: string | null;
  totalDebit: number;
  totalCredit: number;
  postedTime?: string | null;
  postedById?: string | null;
  reversalOfEntryId?: string | null;
  reversedByEntryId?: string | null;
  creationTime?: string;
  lines: JournalLineDto[];
}

export interface CreateJournalLineDto {
  accountId: string;
  /** Transaction-currency debit (exactly one of debit/credit > 0 at posting) */
  debit?: number;
  /** Transaction-currency credit */
  credit?: number;
  memo?: string | null;
  partyType?: string | null;
  partyId?: string | null;
  dimensions?: string | null;
}

export interface CreateJournalEntryDto {
  postingDate: string;
  memo?: string | null;
  /** Transaction currency; null = base currency */
  currency?: string | null;
  /** Null = resolve from the exchange-rate table at posting */
  exchangeRate?: number | null;
  lines: CreateJournalLineDto[];
}

export interface ReverseJournalEntryDto {
  /** Null = same date as the original entry */
  postingDate?: string | null;
  memo?: string | null;
}

export interface JournalEntryQueryDto extends PagedQueryDto {
  status?: JournalEntryStatus | null;
  dateFrom?: string | null;
  dateTo?: string | null;
  sourceType?: string | null;
  sourceId?: string | null;
  keyword?: string | null;
}

// ============================================
// Exchange Rates
// ============================================

export interface ExchangeRateDto {
  id: string;
  fromCurrency: string;
  toCurrency: string;
  rate: number;
  rateDate: string;
  source?: string | null;
  creationTime?: string;
}

/** Idempotent upsert keyed by (currency pair, date) */
export interface UpsertExchangeRateDto {
  fromCurrency: string;
  toCurrency: string;
  rate: number;
  rateDate: string;
  source?: string | null;
}

export interface ExchangeRateQueryDto extends PagedQueryDto {
  fromCurrency?: string | null;
  toCurrency?: string | null;
  dateFrom?: string | null;
  dateTo?: string | null;
}

// ============================================
// Fiscal Years
// ============================================

export interface FiscalYearDto {
  id: string;
  name: string;
  startDate: string;
  endDate: string;
  isClosed: boolean;
  closedTime?: string | null;
  closedById?: string | null;
}

export interface CreateFiscalYearDto {
  name: string;
  startDate: string;
  endDate: string;
}

// ============================================
// Financial Reports
// ============================================

/** Signed balances: debit positive, credit negative */
export interface TrialBalanceRowDto {
  accountId: string;
  code: string;
  name: string;
  rootType: AccountRootType;
  openingBalance: number;
  periodDebit: number;
  periodCredit: number;
  closingBalance: number;
}

export interface TrialBalanceReportDto {
  from: string;
  to: string;
  baseCurrency: string;
  rows: TrialBalanceRowDto[];
  totalOpeningBalance: number;
  totalPeriodDebit: number;
  totalPeriodCredit: number;
  totalClosingBalance: number;
}

/** Balance is positive in the account's natural direction */
export interface ReportAccountRowDto {
  accountId: string;
  code: string;
  name: string;
  rootType: AccountRootType;
  subType?: string | null;
  balance: number;
}

export interface BalanceSheetReportDto {
  asOf: string;
  baseCurrency: string;
  assets: ReportAccountRowDto[];
  liabilities: ReportAccountRowDto[];
  equity: ReportAccountRowDto[];
  /** Computed current earnings line (no year-end close required) */
  currentEarnings: number;
  totalAssets: number;
  totalLiabilities: number;
  /** Includes currentEarnings */
  totalEquity: number;
  /** Should be 0 */
  balanceCheck: number;
}

export interface ProfitAndLossReportDto {
  from: string;
  to: string;
  baseCurrency: string;
  income: ReportAccountRowDto[];
  expenses: ReportAccountRowDto[];
  totalIncome: number;
  totalExpenses: number;
  netProfit: number;
}

export interface GeneralLedgerLineDto {
  journalEntryId: string;
  entryNumber?: string | null;
  postingDate: string;
  memo?: string | null;
  debit: number;
  credit: number;
  partyType?: string | null;
  partyId?: string | null;
  /** Source document type from the entry header (register back-link) */
  sourceType?: string | null;
  /** Source document id from the entry header */
  sourceId?: string | null;
  /** Signed running balance (debit positive); continuous across pages */
  runningBalance: number;
}

export interface GeneralLedgerReportDto {
  accountId: string;
  code: string;
  name: string;
  from: string;
  to: string;
  baseCurrency: string;
  openingBalance: number;
  closingBalance: number;
  lines: {
    items: GeneralLedgerLineDto[];
    totalCount: number;
    pageIndex: number;
    pageSize: number;
  };
}

/** Indirect-method cash flow statement (net profit + balance-sheet movements by CashFlowActivity) */
export interface CashFlowReportDto {
  from: string;
  to: string;
  baseCurrency: string;
  /** Starting point of the indirect method */
  netProfit: number;
  operating: ReportAccountRowDto[];
  investing: ReportAccountRowDto[];
  financing: ReportAccountRowDto[];
  /** Accounts with no CashFlowActivity classification (fix them on the chart of accounts) */
  unclassified: ReportAccountRowDto[];
  /** = netProfit + operating adjustment rows */
  totalOperating: number;
  totalInvesting: number;
  totalFinancing: number;
  totalUnclassified: number;
  netCashFlow: number;
  openingCash: number;
  closingCash: number;
  /** GL movement of CashEquivalent accounts (closingCash - openingCash) */
  cashMovement: number;
  /** Identity check row: netCashFlow - cashMovement, expected 0 */
  checkDifference: number;
}

/** Per-rate tax filing row (output = TaxPayable credits, input = TaxReceivable debits) */
export interface TaxSummaryRowDto {
  taxRateId: string;
  /** Null when the rate has been deleted */
  rateName?: string | null;
  rate?: number | null;
  agencyId?: string | null;
  agencyName?: string | null;
  outputTax: number;
  inputTax: number;
  /** outputTax - inputTax; positive = payable */
  netTax: number;
}

/** Tax filing summary (pure GL aggregation over lines carrying taxRateId) */
export interface TaxSummaryReportDto {
  from: string;
  to: string;
  baseCurrency: string;
  rows: TaxSummaryRowDto[];
  totalOutputTax: number;
  totalInputTax: number;
  totalNetTax: number;
}

// ── P2: parties, catalog, tax model ─────────────────────────────

export interface CustomerDto {
  id: string;
  code?: string | null;
  name: string;
  email?: string | null;
  phone?: string | null;
  billingAddress?: string | null;
  shippingAddress?: string | null;
  currency?: string | null;
  paymentTermsDays?: number | null;
  defaultTaxCodeId?: string | null;
  isActive: boolean;
  notes?: string | null;
  creationTime: string;
}

export interface CreateCustomerDto {
  code?: string | null;
  name: string;
  email?: string | null;
  phone?: string | null;
  billingAddress?: string | null;
  shippingAddress?: string | null;
  currency?: string | null;
  paymentTermsDays?: number | null;
  defaultTaxCodeId?: string | null;
  notes?: string | null;
}

export interface UpdateCustomerDto extends CreateCustomerDto {
  isActive: boolean;
}

export interface CustomerQueryDto extends PagedQueryDto {
  keyword?: string;
  isActive?: boolean;
}

export interface VendorDto {
  id: string;
  code?: string | null;
  name: string;
  email?: string | null;
  phone?: string | null;
  address?: string | null;
  currency?: string | null;
  paymentTermsDays?: number | null;
  defaultTaxCodeId?: string | null;
  isActive: boolean;
  notes?: string | null;
  creationTime: string;
}

export interface CreateVendorDto {
  code?: string | null;
  name: string;
  email?: string | null;
  phone?: string | null;
  address?: string | null;
  currency?: string | null;
  paymentTermsDays?: number | null;
  defaultTaxCodeId?: string | null;
  notes?: string | null;
}

export interface UpdateVendorDto extends CreateVendorDto {
  isActive: boolean;
}

export type VendorQueryDto = CustomerQueryDto;

export interface ItemDto {
  id: string;
  code?: string | null;
  name: string;
  type: ItemType;
  description?: string | null;
  salesPrice?: number | null;
  purchasePrice?: number | null;
  incomeAccountId?: string | null;
  expenseAccountId?: string | null;
  defaultTaxCodeId?: string | null;
  isActive: boolean;
  creationTime: string;
}

export interface CreateItemDto {
  code?: string | null;
  name: string;
  type?: ItemType;
  description?: string | null;
  salesPrice?: number | null;
  purchasePrice?: number | null;
  incomeAccountId?: string | null;
  expenseAccountId?: string | null;
  defaultTaxCodeId?: string | null;
}

export interface UpdateItemDto extends CreateItemDto {
  isActive: boolean;
}

export interface ItemQueryDto extends PagedQueryDto {
  keyword?: string;
  type?: ItemType;
  isActive?: boolean;
}

export interface TaxAgencyDto {
  id: string;
  name: string;
  description?: string | null;
  isActive: boolean;
}

export interface UpsertTaxAgencyDto {
  name: string;
  description?: string | null;
  isActive?: boolean;
}

export interface TaxRateDto {
  id: string;
  agencyId: string;
  agencyName?: string | null;
  name: string;
  rate: number;
  isActive: boolean;
}

export interface UpsertTaxRateDto {
  agencyId: string;
  name: string;
  rate: number;
  isActive?: boolean;
}

export interface TaxCodeComponentDto {
  taxRateId: string;
  rateName?: string | null;
  rate: number;
  order: number;
  isCompound: boolean;
}

export interface TaxCodeDto {
  id: string;
  name: string;
  description?: string | null;
  isActive: boolean;
  components: TaxCodeComponentDto[];
}

export interface UpsertTaxCodeComponentDto {
  taxRateId: string;
  order: number;
  isCompound?: boolean;
}

export interface UpsertTaxCodeDto {
  name: string;
  description?: string | null;
  isActive?: boolean;
  components: UpsertTaxCodeComponentDto[];
}

// ── P2: documents ───────────────────────────────────────────────

export interface FinanceDocumentBaseDto {
  id: string;
  number?: string | null;
  status: FinanceDocumentStatus;
  docDate: string;
  currency: string;
  exchangeRate: number;
  subTotal: number;
  taxTotal: number;
  total: number;
  baseTotal: number;
  memo?: string | null;
  journalEntryId?: string | null;
  voidJournalEntryId?: string | null;
  creationTime: string;
}

export interface SalesDocLineDto {
  id: string;
  lineNumber: number;
  itemId?: string | null;
  description?: string | null;
  accountId?: string | null;
  quantity: number;
  unitPrice: number;
  amount: number;
  taxCodeId?: string | null;
}

export interface CreateSalesDocLineDto {
  itemId?: string | null;
  description?: string | null;
  accountId?: string | null;
  quantity: number;
  unitPrice: number;
  taxCodeId?: string | null;
}

export interface InvoiceDto extends FinanceDocumentBaseDto {
  customerId: string;
  customerName?: string | null;
  dueDate?: string | null;
  appliedTotal: number;
  lines: SalesDocLineDto[];
}

export interface CreateInvoiceDto {
  customerId: string;
  docDate: string;
  dueDate?: string | null;
  currency?: string | null;
  exchangeRate?: number | null;
  memo?: string | null;
  lines: CreateSalesDocLineDto[];
}

export interface InvoiceQueryDto extends PagedQueryDto {
  keyword?: string;
  status?: FinanceDocumentStatus;
  customerId?: string;
  dateFrom?: string;
  dateTo?: string;
}

export interface BillDto extends FinanceDocumentBaseDto {
  vendorId: string;
  vendorName?: string | null;
  dueDate?: string | null;
  appliedTotal: number;
  lines: SalesDocLineDto[];
}

export interface CreateBillDto {
  vendorId: string;
  docDate: string;
  dueDate?: string | null;
  currency?: string | null;
  exchangeRate?: number | null;
  memo?: string | null;
  lines: CreateSalesDocLineDto[];
}

export interface BillQueryDto extends PagedQueryDto {
  keyword?: string;
  status?: FinanceDocumentStatus;
  vendorId?: string;
  dateFrom?: string;
  dateTo?: string;
}

export interface CreditMemoDto extends FinanceDocumentBaseDto {
  customerId: string;
  customerName?: string | null;
  appliedTotal: number;
  lines: SalesDocLineDto[];
}

export interface CreateCreditMemoDto {
  customerId: string;
  docDate: string;
  currency?: string | null;
  exchangeRate?: number | null;
  memo?: string | null;
  lines: CreateSalesDocLineDto[];
}

export type CreditMemoQueryDto = InvoiceQueryDto;

export interface ExpenseLineDto {
  id: string;
  lineNumber: number;
  description?: string | null;
  accountId: string;
  amount: number;
  taxCodeId?: string | null;
}

export interface CreateExpenseLineDto {
  description?: string | null;
  accountId: string;
  amount: number;
  taxCodeId?: string | null;
}

export interface ExpenseDto extends FinanceDocumentBaseDto {
  vendorId?: string | null;
  vendorName?: string | null;
  paidFromAccountId: string;
  /** Resolved paid-from account name (detail only; null in list projection). */
  paidFromAccountName?: string | null;
  /** Settlement instrument (free-form; suggested values in PAYMENT_METHODS). */
  paymentMethod?: string | null;
  lines: ExpenseLineDto[];
}

export interface CreateExpenseDto {
  vendorId?: string | null;
  paidFromAccountId: string;
  paymentMethod?: string | null;
  docDate: string;
  currency?: string | null;
  exchangeRate?: number | null;
  memo?: string | null;
  lines: CreateExpenseLineDto[];
}

export interface ExpenseQueryDto extends BillQueryDto {
  paymentMethod?: string;
}

export interface PaymentEntryDto {
  id: string;
  number?: string | null;
  status: FinanceDocumentStatus;
  direction: PaymentDirection;
  partyType: FinancePartyType;
  partyId: string;
  partyName?: string | null;
  docDate: string;
  currency: string;
  exchangeRate: number;
  amount: number;
  baseAmount: number;
  appliedTotal: number;
  depositToAccountId?: string | null;
  /** Settlement instrument (free-form; suggested values in PAYMENT_METHODS). */
  paymentMethod?: string | null;
  reference?: string | null;
  memo?: string | null;
  sourceType?: string | null;
  sourceId?: string | null;
  journalEntryId?: string | null;
  voidJournalEntryId?: string | null;
  creationTime: string;
}

export interface CreatePaymentEntryDto {
  direction: PaymentDirection;
  partyType: FinancePartyType;
  partyId: string;
  docDate: string;
  currency?: string | null;
  exchangeRate?: number | null;
  amount: number;
  depositToAccountId?: string | null;
  paymentMethod?: string | null;
  reference?: string | null;
  memo?: string | null;
}

export interface PaymentEntryQueryDto extends PagedQueryDto {
  keyword?: string;
  status?: FinanceDocumentStatus;
  direction?: PaymentDirection;
  paymentMethod?: string;
  partyId?: string;
  dateFrom?: string;
  dateTo?: string;
}

export interface ExternalPaymentIngestDto {
  sourceType: string;
  sourceId: string;
  customerId: string;
  docDate: string;
  amount: number;
  currency?: string | null;
  exchangeRate?: number | null;
  depositToAccountId?: string | null;
  paymentMethod?: string | null;
  reference?: string | null;
  memo?: string | null;
  autoPost?: boolean;
}

// ── P2: settlement + aging ──────────────────────────────────────

export interface PaymentApplicationDto {
  id: string;
  sourceType: SettlementDocType;
  sourceId: string;
  sourceNumber?: string | null;
  targetType: SettlementDocType;
  targetId: string;
  targetNumber?: string | null;
  appliedAmount: number;
  realizedFxJournalEntryId?: string | null;
  creationTime: string;
}

export interface ApplySettlementTargetDto {
  targetType: SettlementDocType;
  targetId: string;
  amount: number;
}

export interface ApplySettlementDto {
  sourceType: SettlementDocType;
  sourceId: string;
  targets: ApplySettlementTargetDto[];
}

export interface OpenDocumentDto {
  docType: SettlementDocType;
  docId: string;
  number?: string | null;
  docDate: string;
  dueDate?: string | null;
  currency: string;
  total: number;
  appliedTotal: number;
  outstanding: number;
}

/** Batch settlement (Pay Bills / Receive Payments): one payment per (party, currency) group, posted and applied atomically. */
export interface BatchPaymentTargetDto {
  docType: SettlementDocType;
  docId: string;
  amount: number;
}

export interface BatchPaymentDto {
  docDate: string;
  /** Paid-from account for bills (required); deposit-to account for invoices (falls back to undeposited funds). */
  fundsAccountId?: string | null;
  paymentMethod?: string | null;
  reference?: string | null;
  memo?: string | null;
  targets: BatchPaymentTargetDto[];
}

export interface BatchPaymentResultDto {
  payments: PaymentEntryDto[];
  applications: PaymentApplicationDto[];
}

export interface AgingBucketsDto {
  current: number;
  days1To30: number;
  days31To60: number;
  days61To90: number;
  over90: number;
  total: number;
}

export interface AgingRowDto extends AgingBucketsDto {
  partyId: string;
  partyName: string;
}

export interface AgingReportDto {
  asOf: string;
  baseCurrency: string;
  rows: AgingRowDto[];
  totals: AgingBucketsDto;
}

// ── P3a: banking domain (transfers + reconciliation) ────────────

/** Funds transfer document (bank/cash account to account) */
export interface TransferDto {
  id: string;
  number?: string | null;
  status: FinanceDocumentStatus;
  fromAccountId: string;
  /** Filled by the service ("code name") */
  fromAccountName?: string | null;
  toAccountId: string;
  toAccountName?: string | null;
  transferDate: string;
  currency: string;
  exchangeRate: number;
  amount: number;
  baseAmount: number;
  reference?: string | null;
  memo?: string | null;
  journalEntryId?: string | null;
  voidJournalEntryId?: string | null;
  concurrencyStamp: string;
  creationTime: string;
}

export interface CreateTransferDto {
  fromAccountId: string;
  toAccountId: string;
  transferDate: string;
  /** Null = base currency */
  currency?: string | null;
  exchangeRate?: number | null;
  amount: number;
  reference?: string | null;
  memo?: string | null;
}

export interface TransferQueryDto extends PagedQueryDto {
  status?: FinanceDocumentStatus;
  accountId?: string;
  from?: string;
  to?: string;
}

/** Bank reconciliation header */
export interface ReconciliationDto {
  id: string;
  accountId: string;
  accountName?: string | null;
  statementDate: string;
  statementEndingBalance: number;
  status: ReconciliationStatus;
  completedTime?: string | null;
  note?: string | null;
  lineCount: number;
  /** Cumulative cleared net amount for the account (all reconciliations) */
  clearedBalance: number;
  /** statementEndingBalance - clearedBalance; must be 0 to complete */
  difference: number;
  concurrencyStamp: string;
  creationTime: string;
}

export interface CreateReconciliationDto {
  accountId: string;
  statementDate: string;
  statementEndingBalance: number;
  note?: string | null;
}

export interface ReconciliationQueryDto extends PagedQueryDto {
  accountId?: string;
  status?: ReconciliationStatus;
}

/** Candidate/selected ledger line on the reconciliation worksheet */
export interface ReconciliationCandidateLineDto {
  journalLineId: string;
  journalEntryId: string;
  entryNumber?: string | null;
  postingDate: string;
  memo?: string | null;
  debit: number;
  credit: number;
  isSelected: boolean;
}

export interface ReconciliationWorksheetDto {
  reconciliationId: string;
  statementEndingBalance: number;
  clearedBalance: number;
  difference: number;
  lines: ReconciliationCandidateLineDto[];
}

export interface SetReconciliationLinesDto {
  journalLineIds: string[];
}
