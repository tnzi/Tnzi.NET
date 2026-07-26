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
  FinanceOfferStatus,
  BankRuleField,
  BankRuleOperator,
  BankRuleMatchMode,
  BankRuleDirection,
  PaymentDirection,
  FinancePartyType,
  SettlementDocType,
  ItemType,
  ReconciliationStatus,
  BankNumberScheme,
  CheckStockType,
  CheckLayout,
  BankAccountType,
  BankTransactionSource,
  BankTransactionStatus,
  BankFeedDocType,
  CheckStatus,
  EftFileFormat,
  EftBatchStatus,
  ReceiptStatus,
  ReceiptDocType,
  BalanceSummaryDifferenceKind,
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

export interface GetAccountBalancesDto {
  /** Account ids (de-duplicated; max 500 per request - batch beyond that). */
  accountIds: string[];
  /** As-of date, inclusive. Omit for today (UTC). */
  asOf?: string | null;
}

/** Base-currency account balance as of end of `asOf` (posted lines only). */
export interface AccountBalanceDto {
  accountId: string;
  asOf: string;
  debit: number;
  credit: number;
  /**
   * Signed balance (debit - credit). Not sign-normalised: liability/equity/income
   * accounts are naturally negative - flip by rootType at the presentation layer
   * if you want them positive.
   */
  balance: number;
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
  /** Base-currency debit total. 0 while draft - use txnTotalDebit. */
  totalDebit: number;
  /** Base-currency credit total. 0 while draft - use txnTotalCredit. */
  totalCredit: number;
  /**
   * Transaction-currency debit total; the only total a draft has.
   * totalDebit is base-currency and by design is only filled at posting (a draft has no
   * exchange rate yet, so its base-currency amount does not exist). Do not fall back to
   * totalDebit: the two are different currencies and swapping them would silently restate
   * a foreign-currency entry in another currency.
   */
  txnTotalDebit: number;
  /** Transaction-currency credit total. See txnTotalDebit. */
  txnTotalCredit: number;
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
  reopenedTime?: string | null;
  reopenedById?: string | null;
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
  /**
   * A keyword / source-type filter was applied.
   *
   * When true the backend zeroes `openingBalance`, `closingBalance` and every
   * row's `runningBalance`: a running balance accumulates down an unbroken row
   * chain, and filtering out the middle breaks it. The flag says "no answer",
   * NOT "the balance is zero" - presentation MUST hide the balance columns
   * rather than render the zeroes.
   */
  isFiltered?: boolean;
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
  /** Purchase tax recoverable (input-tax credit). false = non-recoverable, booked as cost. */
  isRecoverable: boolean;
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
  /** Purchase tax recoverable (default true). false = non-recoverable purchase tax booked as cost. */
  isRecoverable?: boolean;
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

export interface BillLineDto extends SalesDocLineDto {
  /** Manual tax amount override (null = computed by rate). */
  taxAmount?: number | null;
}

export interface CreateBillLineDto extends CreateSalesDocLineDto {
  /** Manual tax amount override (requires taxCodeId, must be >= 0; null = computed by rate). */
  taxAmount?: number | null;
}

export interface BillDto extends FinanceDocumentBaseDto {
  vendorId: string;
  vendorName?: string | null;
  dueDate?: string | null;
  appliedTotal: number;
  lines: BillLineDto[];
}

export interface CreateBillDto {
  vendorId: string;
  docDate: string;
  dueDate?: string | null;
  currency?: string | null;
  exchangeRate?: number | null;
  memo?: string | null;
  lines: CreateBillLineDto[];
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
  /** Manual tax amount override (null = computed by rate). */
  taxAmount?: number | null;
}

export interface CreateExpenseLineDto {
  description?: string | null;
  accountId: string;
  amount: number;
  taxCodeId?: string | null;
  /** Manual tax amount override (requires taxCodeId, must be >= 0; null = computed by rate). */
  taxAmount?: number | null;
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
  /** Cross-currency mode: non-null and != currency */
  targetCurrency?: string | null;
  /** Cross-currency mode: transaction-currency amount received */
  targetAmount?: number | null;
  /** Target-side captured rate (posted) */
  targetExchangeRate: number;
  /** Target-side base amount (posted) */
  targetBaseAmount: number;
  reference?: string | null;
  memo?: string | null;
  journalEntryId?: string | null;
  /** Target-currency voucher (cross-currency mode) */
  targetJournalEntryId?: string | null;
  /** Base-currency residual FX voucher (cross-currency mode) */
  fxJournalEntryId?: string | null;
  voidJournalEntryId?: string | null;
  concurrencyStamp: string;
  creationTime: string;
}

export interface CreateTransferDto {
  fromAccountId: string;
  toAccountId: string;
  transferDate: string;
  /** Null = base currency; the source-side currency */
  currency?: string | null;
  exchangeRate?: number | null;
  amount: number;
  /** Null or == currency = same-currency mode; otherwise cross-currency mode */
  targetCurrency?: string | null;
  /** Required and > 0 in cross-currency mode */
  targetAmount?: number | null;
  /** Null = resolve from the rate table at posting */
  targetExchangeRate?: number | null;
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
  /** Derived reconciliation currency (account currency ?? base) */
  currency: string;
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
  /** Reconciliation-currency debit (base account = base amount; foreign-restricted = transaction amount) */
  debit: number;
  /** Reconciliation-currency credit */
  credit: number;
  isSelected: boolean;
  /**
   * True when an imported bank transaction is matched to this line. The selection
   * cannot be dropped from the worksheet (the server 409s): release it via unmatch
   * on the bank feed screen, which frees the transaction and the line atomically.
   * Presentation layers must disable the checkbox for these rows.
   */
  isStatementMatched: boolean;
}

export interface ReconciliationWorksheetDto {
  reconciliationId: string;
  /** Derived reconciliation currency (account currency ?? base) */
  currency: string;
  statementEndingBalance: number;
  clearedBalance: number;
  difference: number;
  lines: ReconciliationCandidateLineDto[];
}

export interface SetReconciliationLinesDto {
  journalLineIds: string[];
}

// ── Multi-currency: unrealized FX revaluation ───────────────────

/** Period-end revaluation request (preview and run share this shape) */
export interface RunRevaluationDto {
  /** Revaluation cut-off date (balances are revalued to this date) */
  asOf: string;
  /** Restrict to a subset of accounts; null = all eligible foreign-currency accounts */
  accountIds?: string[] | null;
  /** Voucher memo (run only; null = auto-generated) */
  memo?: string | null;
}

/** Per-account revaluation row */
export interface RevaluationRowDto {
  accountId: string;
  code: string;
  name: string;
  currency: string;
  /** Transaction-currency balance (Σ TxnDebit − TxnCredit where line currency == account currency) */
  txnBalance: number;
  /** Revaluation rate (currency → base, at asOf) */
  rate: number;
  /** Target base value (round(txnBalance × rate)) */
  targetBase: number;
  /** Book base balance (Σ Debit − Credit, incl. prior revaluations) */
  bookBase: number;
  /** targetBase − bookBase (positive = increase base value) */
  adjustment: number;
  /** Non-null = not posted (e.g. account inactive) */
  skipReason?: string | null;
}

/** Period-end revaluation preview/result (run sets journalEntryId when an increment posts) */
export interface RevaluationPreviewDto {
  asOf: string;
  baseCurrency: string;
  /** Set only when a run posts an increment */
  journalEntryId?: string | null;
  rows: RevaluationRowDto[];
  /** Net adjustment (base currency; sum of postable adjustments) */
  totalAdjustment: number;
}

// ── P3 Phase 0: bank account profile ────────────────────────────

/** Deployment capabilities of the bank-account surface (config-driven, not per-record). */
export interface BankAccountCapabilitiesDto {
  /**
   * Whether account numbers can be stored (`Finance:Encryption:EncryptionKey` is set).
   * When false the backend rejects writes carrying an account number - disable the
   * field and explain, rather than failing after the user has typed it. Cheque
   * printing on pre-printed stock does not need the number; EFT does.
   */
  canStoreAccountNumber: boolean;
}

/** Bank account profile (1:1 on a CashEquivalent funds account; account number returns masked only). */
export interface BankAccountDto {
  id: string;
  accountId: string;
  /** Resolved funds-account name (service-filled). */
  accountName?: string | null;
  name: string;
  bankName?: string | null;
  scheme: BankNumberScheme;
  routingNumber?: string | null;
  institutionNumber?: string | null;
  transitNumber?: string | null;
  /** Masked account number (last 4; never the plaintext/ciphertext). */
  accountNumberMasked?: string | null;
  currency?: string | null;
  nextCheckNumber: number;
  checkStockType: CheckStockType;
  checkLayout: CheckLayout;
  offsetXMm: number;
  offsetYMm: number;
  feedProviderKey?: string | null;
  externalAccountId?: string | null;
  lastFeedSyncTime?: string | null;
  eftOriginatorId?: string | null;
  eftOriginatorName?: string | null;
  eftFileCreationNumber: number;
  concurrencyStamp: string;
  creationTime: string;
}

export interface CreateBankAccountDto {
  accountId: string;
  name: string;
  bankName?: string | null;
  scheme?: BankNumberScheme;
  routingNumber?: string | null;
  institutionNumber?: string | null;
  transitNumber?: string | null;
  /** Account plaintext (write-only: stored encrypted, returned masked). */
  accountNumber?: string | null;
  currency?: string | null;
  /** Starting check number (may be set manually; default 1). */
  nextCheckNumber?: number;
  checkStockType?: CheckStockType;
  checkLayout?: CheckLayout;
  offsetXMm?: number;
  offsetYMm?: number;
  feedProviderKey?: string | null;
  externalAccountId?: string | null;
  eftOriginatorId?: string | null;
  eftOriginatorName?: string | null;
}

/** Full update; the mounted account and check number cannot change here. */
export interface UpdateBankAccountDto {
  name: string;
  bankName?: string | null;
  scheme?: BankNumberScheme;
  routingNumber?: string | null;
  institutionNumber?: string | null;
  transitNumber?: string | null;
  /** Account plaintext (leave blank to keep the current number). */
  accountNumber?: string | null;
  currency?: string | null;
  checkStockType?: CheckStockType;
  checkLayout?: CheckLayout;
  offsetXMm?: number;
  offsetYMm?: number;
  feedProviderKey?: string | null;
  externalAccountId?: string | null;
  eftOriginatorId?: string | null;
  eftOriginatorName?: string | null;
}

/** Set the next check number (jump = new check book; no gap guarantee). */
export interface SetNextCheckNumberDto {
  nextCheckNumber: number;
}

export interface BankAccountQueryDto extends PagedQueryDto {
  accountId?: string | null;
  keyword?: string | null;
}

/** Party bank account (remit-to; account number returns masked only). */
export interface PartyBankAccountDto {
  id: string;
  partyType: FinancePartyType;
  partyId: string;
  label?: string | null;
  bankName?: string | null;
  scheme: BankNumberScheme;
  routingNumber?: string | null;
  institutionNumber?: string | null;
  transitNumber?: string | null;
  /** Masked account number (last 4). */
  accountNumberMasked?: string | null;
  accountType: BankAccountType;
  currency?: string | null;
  isDefault: boolean;
  isActive: boolean;
  notes?: string | null;
  creationTime: string;
}

export interface SavePartyBankAccountDto {
  partyType: FinancePartyType;
  partyId: string;
  label?: string | null;
  bankName?: string | null;
  scheme?: BankNumberScheme;
  routingNumber?: string | null;
  institutionNumber?: string | null;
  transitNumber?: string | null;
  /** Account plaintext (write-only; leave blank on update to keep the current number). */
  accountNumber?: string | null;
  accountType?: BankAccountType;
  currency?: string | null;
  isDefault?: boolean;
  isActive?: boolean;
  notes?: string | null;
}

export interface PartyBankAccountQueryDto extends PagedQueryDto {
  partyType?: FinancePartyType | null;
  partyId?: string | null;
  isActive?: boolean | null;
}

// ── P3 Phase 1: bank statement import + matching ────────────────

/** Imported bank transaction (signed amount: positive = deposit = GL debit). */
export interface BankTransactionDto {
  id: string;
  accountId: string;
  importBatchId: string;
  txnDate: string;
  amount: number;
  currency: string;
  description?: string | null;
  payee?: string | null;
  reference?: string | null;
  externalId: string;
  source: BankTransactionSource;
  status: BankTransactionStatus;
  matchedJournalLineId?: string | null;
  reconciliationLineId?: string | null;
  suggestedJournalLineId?: string | null;
  matchConfidence?: number | null;
  matchRule?: string | null;
  /** The bank rule that explains this line when the ledger has no counterpart. */
  suggestedRuleId?: string | null;
  suggestedRuleName?: string | null;
  suggestedDocType?: BankFeedDocType | null;
  suggestedCounterAccountId?: string | null;
  suggestedCounterAccountName?: string | null;
  suggestedPartyId?: string | null;
  suggestedPaymentMethod?: string | null;
  balanceAfter?: number | null;
  createdDocType?: string | null;
  createdDocId?: string | null;
  creationTime: string;
}

export interface BankTransactionQueryDto extends PagedQueryDto {
  accountId?: string | null;
  importBatchId?: string | null;
  status?: BankTransactionStatus | null;
  dateFrom?: string | null;
  dateTo?: string | null;
  keyword?: string | null;
}

/**
 * CSV column mapping (sent with the import request, never persisted; the UI
 * remembers it per account in localStorage). Column indexes are 0-based;
 * `amountColumn` and (`debitColumn` + `creditColumn`) are mutually exclusive.
 */
export interface CsvMappingDto {
  hasHeader?: boolean;
  delimiter?: string;
  dateColumn: number;
  /** Date format (e.g. "yyyy-MM-dd"; blank = lenient parse). */
  dateFormat?: string | null;
  /** Signed single amount column (or use debit + credit). */
  amountColumn?: number | null;
  /** Withdrawal (debit) column index. */
  debitColumn?: number | null;
  /** Deposit (credit) column index. */
  creditColumn?: number | null;
  descriptionColumn?: number | null;
  referenceColumn?: number | null;
  /** Rows to skip before the data (excluding the header). */
  skipRows?: number;
  /** Decimal separator ("," = European; thousands stripped first). */
  decimalSeparator?: string | null;
  /** Statement currency (blank = bank account profile currency / base). */
  currency?: string | null;
}

export interface BankImportResultDto {
  batchId: string;
  importedCount: number;
  skippedCount: number;
}

export interface PullBankFeedDto {
  accountId: string;
}

/** Match-suggestion run result. */
export interface BankSuggestResultDto {
  evaluated: number;
  suggested: number;
  autoConfirmed: number;
}

/** Confirm a match (blank journalLineId adopts the engine suggestion). */
export interface ConfirmBankMatchDto {
  journalLineId?: string | null;
}

/** Match candidate (for the user to pick when there are several). */
export interface BankMatchCandidateDto {
  journalLineId: string;
  journalEntryId: string;
  entryNumber?: string | null;
  postingDate: string;
  memo?: string | null;
  /** Line net amount (debit positive, base currency; same sign as the transaction). */
  amount: number;
}

/** Create a draft document from a bank transaction (pre-filled by sign). */
export interface CreateBankDocumentDto {
  docType: BankFeedDocType;
  /** Counter account (Expense's expense account / Transfer's other funds account). */
  counterAccountId?: string | null;
  /** Party (required for PaymentEntry). */
  partyId?: string | null;
  paymentMethod?: string | null;
  /**
   * One step: create the draft, post it, and confirm the match against the
   * journal line the posting produced. Default `false` (draft only).
   *
   * Preconditions are identical to `confirm` (base-currency account + an open
   * Draft reconciliation) and are checked BEFORE anything is written, so a
   * rejected call leaves no orphan draft behind.
   */
  postAndMatch?: boolean;
}

export interface BankDocumentResultDto {
  docType: string;
  docId: string;
  /** True when `postAndMatch` posted the document. */
  posted?: boolean;
  /** True when the bank line was confirmed against the posted journal line. */
  matched?: boolean;
  journalEntryId?: string | null;
}

/** Bank statement import batch. */
export interface BankImportBatchDto {
  id: string;
  accountId: string;
  accountName?: string | null;
  source: BankTransactionSource;
  fileName?: string | null;
  periodFrom?: string | null;
  periodTo?: string | null;
  importedCount: number;
  skippedCount: number;
  statementEndBalance?: number | null;
  /** Matched-line count in the batch (>0 = cannot delete). */
  matchedCount: number;
  creationTime: string;
}

export interface BankImportBatchQueryDto extends PagedQueryDto {
  accountId?: string | null;
}

// ── P3 Phase 2: check printing ──────────────────────────────────

/** Check register record (Issued / Void / Spoiled all reserve the number). */
export interface BankCheckDto {
  id: string;
  bankAccountId: string;
  /** Bank account profile name (service-filled). */
  bankAccountName?: string | null;
  checkNumber: number;
  status: CheckStatus;
  paymentEntryId?: string | null;
  /** Linked payment number (service-filled). */
  paymentNumber?: string | null;
  payeeName?: string | null;
  amount?: number | null;
  currency?: string | null;
  issueDate: string;
  printedTime?: string | null;
  isManual: boolean;
  voidReason?: string | null;
  /** Reprint chain (the new check that replaced this voided one). */
  replacedByCheckId?: string | null;
  concurrencyStamp: string;
  creationTime: string;
}

/** Print-queue item (a posted outbound check payment awaiting print). */
export interface CheckQueueItemDto {
  paymentEntryId: string;
  paymentNumber?: string | null;
  bankAccountId: string;
  bankAccountName?: string | null;
  /** Payee (vendor name). */
  payeeName?: string | null;
  docDate: string;
  currency: string;
  amount: number;
  memo?: string | null;
  reference?: string | null;
}

/** Print checks (allocate a number per check and merge into one PDF). */
export interface PrintChecksDto {
  /** Payment ids to print (all must be in the queue and share one bank account). */
  paymentEntryIds: string[];
  /** Issue date (null = the payment date). */
  issueDate?: string | null;
}

/** Register a hand-written check (explicit number, conflict = 409). */
export interface RegisterManualCheckDto {
  bankAccountId: string;
  checkNumber: number;
  payeeName?: string | null;
  amount?: number | null;
  currency?: string | null;
  issueDate: string;
  /** Optional linked payment. */
  paymentEntryId?: string | null;
}

/** Void a check. */
export interface VoidCheckDto {
  reason?: string | null;
}

/** Register a spoiled check (damaged / misaligned blank; reserves the number). */
export interface SpoilCheckDto {
  bankAccountId: string;
  checkNumber: number;
  reason?: string | null;
}

export interface CheckQueryDto extends PagedQueryDto {
  bankAccountId?: string | null;
  status?: CheckStatus | null;
  /** Keyword (payee / void reason). */
  keyword?: string | null;
}

// ── P3 Phase 3: EFT output ──────────────────────────────────────

/** EFT batch line. */
export interface EftBatchLineDto {
  id: string;
  paymentEntryId: string;
  paymentNumber?: string | null;
  partyBankAccountId: string;
  partyBankAccountMasked?: string | null;
  amount: number;
  payeeName?: string | null;
}

/** EFT batch header. */
export interface EftBatchDto {
  id: string;
  number?: string | null;
  status: EftBatchStatus;
  bankAccountId: string;
  /** Originating bank account name (service-filled). */
  bankAccountName?: string | null;
  format: EftFileFormat;
  currency: string;
  effectiveDate: string;
  fileCreationNumber?: number | null;
  totalCount: number;
  totalAmount: number;
  fileName?: string | null;
  generatedTime?: string | null;
  voidReason?: string | null;
  concurrencyStamp: string;
  creationTime: string;
  /** Batch lines (returned in the detail). */
  lines: EftBatchLineDto[];
}

/** EFT queue item (a posted outbound bank-transfer payment ready to batch). */
export interface EftQueueItemDto {
  paymentEntryId: string;
  paymentNumber?: string | null;
  partyType: FinancePartyType;
  partyId: string;
  payeeName?: string | null;
  docDate: string;
  currency: string;
  amount: number;
  /** Payee's default bank account. */
  partyBankAccountId: string;
  partyBankAccountMasked?: string | null;
  partyScheme: BankNumberScheme;
}

/** Create a draft EFT batch. */
export interface CreateEftBatchDto {
  bankAccountId: string;
  format: EftFileFormat;
  effectiveDate: string;
  /** Payment ids to include (all batchable, currency/scheme matching the format). */
  paymentEntryIds: string[];
}

/** Void an EFT batch. */
export interface VoidEftBatchDto {
  reason?: string | null;
}

export interface EftBatchQueryDto extends PagedQueryDto {
  bankAccountId?: string | null;
  status?: EftBatchStatus | null;
  format?: EftFileFormat | null;
}

// ── P3 Phase 4: receipt capture ─────────────────────────────────

/** Receipt capture record. */
export interface ReceiptDto {
  id: string;
  fileId: string;
  originalFileName?: string | null;
  status: ReceiptStatus;
  vendorName?: string | null;
  docDate?: string | null;
  currency?: string | null;
  subtotal?: number | null;
  taxAmount?: number | null;
  total?: number | null;
  reference?: string | null;
  lineItemsJson?: string | null;
  confidence?: number | null;
  matchedVendorId?: string | null;
  matchedVendorName?: string | null;
  convertedDocType?: string | null;
  convertedDocId?: string | null;
  failReason?: string | null;
  concurrencyStamp: string;
  creationTime: string;
}

/** Register a receipt after upload (fileId from the user upload). */
export interface CreateReceiptDto {
  fileId: string;
  fileName?: string | null;
  /** Currency hint (optional). */
  currency?: string | null;
}

/** Manually correct the extracted fields. */
export interface UpdateReceiptExtractionDto {
  vendorName?: string | null;
  docDate?: string | null;
  currency?: string | null;
  subtotal?: number | null;
  taxAmount?: number | null;
  total?: number | null;
  reference?: string | null;
  /** Pin a vendor (overrides the match suggestion). */
  matchedVendorId?: string | null;
}

/** Convert a receipt into a document draft. */
export interface ConvertReceiptDto {
  /** Target document type (Expense | Bill). */
  docType: ReceiptDocType;
  /** Vendor (falls back to matchedVendorId; 400 when still unresolved). */
  vendorId?: string | null;
  /** Expense / cost account for the single draft line. */
  accountId?: string | null;
  /** Paid-from account (expense conversion only). */
  paidFromAccountId?: string | null;
}

/** Receipt conversion result. */
export interface ReceiptConvertResultDto {
  docType: string;
  docId: string;
}

export interface ReceiptQueryDto extends PagedQueryDto {
  status?: ReceiptStatus | null;
  /** Keyword (vendor / file name / reference). */
  keyword?: string | null;
}

// ── Batch F: account period-balance summary (report acceleration) ─

/** Balance-summary full rebuild result (current tenant). */
export interface BalanceSummaryRebuildDto {
  /** Number of buckets written after the rebuild. */
  buckets: number;
  /** Posted ledger lines that fed the aggregation. */
  lines: number;
  /** Rebuild duration in milliseconds. */
  durationMs: number;
}

/** One balance-summary difference (expected = ledger aggregation, stored = bucket). */
export interface BalanceSummaryDifferenceDto {
  accountId: string;
  /** Accounting period (yyyyMM). */
  period: number;
  currency: string;
  kind: BalanceSummaryDifferenceKind;
  expectedDebit: number;
  expectedCredit: number;
  storedDebit: number;
  storedCredit: number;
}

/** Balance-summary verify result (diagnoses bucket vs ledger consistency; no repair). */
export interface BalanceSummaryVerifyDto {
  /** True when there is no difference at all. */
  isConsistent: boolean;
  /** Buckets checked (the ledger-expected count). */
  checkedBuckets: number;
  /** Total differences (may exceed the truncation cap on `differences`). */
  totalDifferences: number;
  /** Difference detail (first 100 only, guards against huge responses). */
  differences: BalanceSummaryDifferenceDto[];
}

/**
 * Rolling closing date ("books are closed through this date").
 *
 * Orthogonal to fiscal-year close: a fiscal year locks a whole RANGE, this
 * locks everything up to and including a date that the bookkeeper advances
 * each month. Either lock rejects a posting with 409.
 */
export interface LedgerLockDto {
  /** Inclusive. `null` = not closed. */
  closingDate?: string | null;
  /** Whether a password is required to change it. The hash never leaves the server. */
  isPasswordProtected: boolean;
  note?: string | null;
  lastChangedTime?: string | null;
  lastChangedBy?: string | null;
}

/** Set / advance / clear the closing date. */
export interface SetLedgerLockDto {
  /** `null` clears the lock. */
  closingDate?: string | null;
  /** Required (and must match) once a password is set, else 403. */
  password?: string | null;
  /** `null` = leave unchanged; `''` = clear; a value = set. */
  newPassword?: string | null;
  note?: string | null;
}

/**
 * Party work-surface summary (customer or vendor).
 *
 * `openBalance` / `buckets` come from the SAME calculation the aging report
 * uses, so the figure on a customer page is penny-for-penny the figure on the
 * A/R aging report (and therefore ties out to the GL control account). Never
 * re-derive it by summing a page of documents in the client: that only adds up
 * the current page and produces a number that looks right and is not.
 */
export interface PartyLedgerSummaryDto {
  partyId: string;
  partyName: string;
  partyType: FinancePartyType;
  baseCurrency: string;
  /** Positive = they owe us (customer) / we owe them (vendor). */
  openBalance: number;
  /** The past-due portion (total minus the current bucket). */
  overdue: number;
  buckets: AgingBucketsDto;
  /** Sales (customer) or spend (vendor) posted in the period. */
  periodTotal: number;
  periodFrom: string;
  periodTo: string;
  openDocumentCount: number;
  lastTransactionDate?: string | null;
}

/**
 * One row of a party's transaction ledger, across document types.
 *
 * `amount` is SIGNED: positive increases what they owe (invoice / bill),
 * negative reduces it (payment / credit memo). Read the sign rather than
 * branching on `docType`.
 */
export interface PartyLedgerEntryDto {
  /** Source token (see FINANCE_SOURCE_TYPES) - drives the drill-through target. */
  docType: string;
  docId: string;
  number?: string | null;
  docDate: string;
  dueDate?: string | null;
  currency: string;
  amount: number;
  /** Unsettled amount; only meaningful on invoices / bills, else 0. */
  outstanding: number;
  status: FinanceDocumentStatus;
  /** Positive only while the document is still open and past its due date. */
  overdueDays: number;
}

export interface PartyLedgerQueryDto extends PagedQueryDto {
  from?: string;
  to?: string;
  /** Only documents that still have an unsettled balance. */
  openOnly?: boolean;
}

/**
 * What a caller supplies: the page it wants plus the filters. `skip` / `take`
 * on the wire DTO are derived server-side, so requiring the UI to compute them
 * is noise it would only get wrong.
 */
export type PartyLedgerQuery = Omit<PartyLedgerQueryDto, 'skip' | 'take'>;

// ── Estimates and purchase orders (non-posting documents) ─────────────

/** One line of an estimate / purchase order (the two share a line shape). */
export interface OfferLineDto {
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

export interface CreateOfferLineDto {
  itemId?: string | null;
  description?: string | null;
  accountId?: string | null;
  quantity: number;
  unitPrice: number;
  taxCodeId?: string | null;
}

/**
 * An estimate (US / QuickBooks) or quote (Xero / Commonwealth) - the same
 * document. It never posts: there is no journal entry, no captured rate and no
 * base-currency amount, because a promise is not a fact.
 */
export interface EstimateDto {
  id: string;
  number?: string | null;
  status: FinanceOfferStatus;
  customerId: string;
  customerName?: string | null;
  docDate: string;
  /** Quote valid until; whether it has lapsed is read off the date, not a status. */
  expiryDate?: string | null;
  currency: string;
  subTotal: number;
  taxTotal: number;
  total: number;
  memo?: string | null;
  /** Internal note; not part of what the customer receives. */
  internalNote?: string | null;
  /** Source token of what this became (see FINANCE_SOURCE_TYPES). */
  convertedToDocType?: string | null;
  convertedToDocId?: string | null;
  creationTime: string;
  lines: OfferLineDto[];
}

/** A purchase order - the mirror image of an estimate, aimed at a vendor. */
export interface PurchaseOrderDto {
  id: string;
  number?: string | null;
  status: FinanceOfferStatus;
  vendorId: string;
  vendorName?: string | null;
  docDate: string;
  expectedDate?: string | null;
  currency: string;
  subTotal: number;
  taxTotal: number;
  total: number;
  memo?: string | null;
  internalNote?: string | null;
  shipTo?: string | null;
  convertedToDocType?: string | null;
  convertedToDocId?: string | null;
  creationTime: string;
  lines: OfferLineDto[];
}

export interface CreateEstimateDto {
  customerId: string;
  docDate: string;
  expiryDate?: string | null;
  currency?: string | null;
  memo?: string | null;
  internalNote?: string | null;
  lines: CreateOfferLineDto[];
}

export interface CreatePurchaseOrderDto {
  vendorId: string;
  docDate: string;
  expectedDate?: string | null;
  currency?: string | null;
  memo?: string | null;
  internalNote?: string | null;
  shipTo?: string | null;
  lines: CreateOfferLineDto[];
}

/** Convert an estimate to an invoice / a purchase order to a bill. */
export interface ConvertOfferDto {
  /** Target document date; omitted = today. */
  docDate?: string | null;
  /** Target due date; omitted = derived from the party's payment terms. */
  dueDate?: string | null;
}

export interface ConvertOfferResultDto {
  sourceId: string;
  sourceNumber?: string | null;
  /** Source token of the created document. */
  docType: string;
  /** The created document is a DRAFT - posting it stays a human decision. */
  docId: string;
}

export interface EstimateQueryDto extends PagedQueryDto {
  keyword?: string;
  status?: FinanceOfferStatus;
  customerId?: string;
  dateFrom?: string;
  dateTo?: string;
  /** Only documents still in play (draft / sent / accepted). */
  openOnly?: boolean;
}

export interface PurchaseOrderQueryDto extends PagedQueryDto {
  keyword?: string;
  status?: FinanceOfferStatus;
  vendorId?: string;
  dateFrom?: string;
  dateTo?: string;
  openOnly?: boolean;
}

// ── Bank rules ────────────────────────────────────────────────────────

export interface BankRuleConditionDto {
  id: string;
  lineNumber: number;
  field: BankRuleField;
  operator: BankRuleOperator;
  value: string;
}

/**
 * A bank rule answers the half of the question the match engine cannot: the
 * ledger has no counterpart for this line, but we know what it is.
 *
 * Rules are ordered and the FIRST match wins (QuickBooks semantics) - results
 * are never merged, because two rules naming different accounts would produce a
 * number nobody can explain.
 */
export interface BankRuleDto {
  id: string;
  name: string;
  priority: number;
  isEnabled: boolean;
  /** null = applies to every bank account. */
  accountId?: string | null;
  accountName?: string | null;
  direction: BankRuleDirection;
  matchMode: BankRuleMatchMode;
  docType: BankFeedDocType;
  counterAccountId?: string | null;
  counterAccountName?: string | null;
  partyId?: string | null;
  paymentMethod?: string | null;
  /** Books the document without asking. Per-rule, and off by default. */
  autoApply: boolean;
  creationTime: string;
  conditions: BankRuleConditionDto[];
}

export interface CreateBankRuleConditionDto {
  field: BankRuleField;
  operator: BankRuleOperator;
  value: string;
}

export interface CreateBankRuleDto {
  name: string;
  /** Omitted on create = appended to the end of the order. */
  priority?: number | null;
  isEnabled: boolean;
  accountId?: string | null;
  direction: BankRuleDirection;
  matchMode: BankRuleMatchMode;
  docType: BankFeedDocType;
  counterAccountId?: string | null;
  partyId?: string | null;
  paymentMethod?: string | null;
  autoApply: boolean;
  conditions: CreateBankRuleConditionDto[];
}

export interface ReorderBankRulesDto {
  ruleIds: string[];
}

export interface TestBankRuleDto {
  accountId?: string | null;
  sample?: number;
}

export interface BankRuleTestRowDto {
  transactionId: string;
  txnDate: string;
  amount: number;
  description?: string | null;
  payee?: string | null;
  /**
   * Which rule actually takes this line. Different from the rule under test
   * means a higher-priority rule got there first - the thing the operator needs
   * to see BEFORE saving.
   */
  winningRuleId: string;
  winningRuleName: string;
}

export interface BankRuleTestResultDto {
  evaluated: number;
  matched: number;
  rows: BankRuleTestRowDto[];
}

export interface BankRuleQueryDto extends PagedQueryDto {
  keyword?: string;
  accountId?: string;
  isEnabled?: boolean;
}

/**
 * What a caller supplies (page + filters). `skip` / `take` are derived
 * server-side; making the UI compute them is noise it would only get wrong.
 */
export type BankRuleQuery = Omit<BankRuleQueryDto, 'skip' | 'take'>;

// ── Document attachments and discussion ───────────────────────────────

/**
 * A file attached to a finance document.
 *
 * Finance never touches Storage: the file is uploaded through the Storage API
 * first, and only its id is linked here. `fileName` / `fileSize` are a snapshot
 * taken at attach time so a list does not need a Storage round-trip per row.
 */
export interface DocumentAttachmentDto {
  id: string;
  sourceType: string;
  sourceId: string;
  fileId: string;
  fileName: string;
  contentType?: string | null;
  fileSize: number;
  caption?: string | null;
  creatorId?: string | null;
  creationTime: string;
}

export interface CreateDocumentAttachmentDto {
  fileId: string;
  fileName?: string | null;
  contentType?: string | null;
  fileSize: number;
  caption?: string | null;
}

/** One internal comment on a document. Not part of what the party receives. */
export interface DocumentCommentDto {
  id: string;
  sourceType: string;
  sourceId: string;
  body: string;
  creatorId?: string | null;
  creatorName?: string | null;
  creationTime: string;
  /** Decided server-side: author, or holder of finance.comment.delete. */
  canDelete: boolean;
}

export interface CreateDocumentCommentDto {
  body: string;
}


// ── Customer statements & dunning (P4-5) ─────────────────────

/**
 * Two statement shapes. Open Item lists what is still unpaid (what most North
 * American businesses send); Activity lists everything that moved in a period
 * with a running balance (what a bank statement looks like).
 */
export type StatementStyle = 'OpenItem' | 'Activity';

/** How hard to chase. `None` means nothing is overdue enough to act on. */
export type DunningLevel = 'None' | 'Reminder' | 'Overdue' | 'FinalNotice';

export interface StatementLineDto {
  docDate: string;
  dueDate?: string | null;
  docType: string;
  docId: string;
  number?: string | null;
  charge: number;
  payment: number;
  outstanding: number;
  overdueDays: number;
  /** Open Item: the outstanding amount. Activity: the running balance. */
  balance: number;
}

export interface CustomerStatementDto {
  partyId: string;
  partyName?: string | null;
  partyType: FinancePartyType;
  style: StatementStyle;
  currency: string;
  periodFrom: string;
  periodTo: string;
  openingBalance: number;
  closingBalance: number;
  overdue: number;
  dunningLevel: DunningLevel;
  buckets: AgingBucketsDto;
  lines: StatementLineDto[];
}

export interface CustomerStatementQueryDto {
  style?: StatementStyle;
  from?: string;
  to?: string;
}

/** One row of the collections worklist: who to chase, worst first. */
export interface DunningCandidateDto {
  partyId: string;
  partyName?: string | null;
  openBalance: number;
  overdue: number;
  oldestOverdueDays: number;
  level: DunningLevel;
  buckets: AgingBucketsDto;
}

// ── Tax returns (P4-7) ───────────────────────────────────────

/** One line of a filing form. `isCalculated` lines are derived, not entered. */
export interface TaxReturnLineDto {
  /** Form line number, e.g. `101`. Wire name is `line` (not `code`). */
  line: string;
  label: string;
  amount: number;
  isCalculated: boolean;
}

export interface TaxReturnDto {
  formCode: string;
  formName: string;
  country: string;
  periodFrom: string;
  periodTo: string;
  currency: string;
  netTax: number;
  lines: TaxReturnLineDto[];
}

export interface TaxReturnFormDto {
  country: string;
  formCode: string;
}

// ── Recurring documents (P4-6) ───────────────────────────────

export type RecurrenceFrequency = 'Daily' | 'Weekly' | 'Monthly' | 'Quarterly' | 'Yearly';
export type RecurringDocKind = 'Invoice' | 'Bill' | 'Expense';
export type RecurringStatus = 'Active' | 'Paused' | 'Ended';
export type RecurringRunStatus = 'Generated' | 'Skipped' | 'Failed';

export interface RecurringLineDto {
  id: string;
  lineNumber: number;
  itemId?: string | null;
  description?: string | null;
  accountId?: string | null;
  quantity: number;
  unitPrice: number;
  taxCodeId?: string | null;
  amount: number;
}

export interface CreateRecurringLineDto {
  itemId?: string | null;
  description?: string | null;
  accountId?: string | null;
  quantity: number;
  unitPrice: number;
  taxCodeId?: string | null;
}

export interface RecurringDocumentDto {
  id: string;
  name: string;
  kind: RecurringDocKind;
  status: RecurringStatus;
  partyId: string;
  partyName?: string | null;
  paidFromAccountId?: string | null;
  currency?: string | null;
  paymentMethod?: string | null;
  memo?: string | null;
  frequency: RecurrenceFrequency;
  interval: number;
  /** Month day (1-31, clamped to month end) or ISO weekday (1=Mon). */
  anchorDay?: number | null;
  startDate: string;
  endDate?: string | null;
  maxOccurrences?: number | null;
  dueDays?: number | null;
  /** null = follow the global default. */
  autoPost?: boolean | null;
  /** The global default already resolved in, for display. */
  effectiveAutoPost: boolean;
  nextRunDate: string;
  lastRunDate?: string | null;
  occurrenceCount: number;
  estimatedTotal: number;
  lines: RecurringLineDto[];
  concurrencyStamp: string;
}

export interface CreateRecurringDocumentDto {
  name: string;
  kind: RecurringDocKind;
  partyId: string;
  paidFromAccountId?: string | null;
  currency?: string | null;
  paymentMethod?: string | null;
  memo?: string | null;
  frequency: RecurrenceFrequency;
  interval: number;
  anchorDay?: number | null;
  startDate: string;
  endDate?: string | null;
  maxOccurrences?: number | null;
  dueDays?: number | null;
  autoPost?: boolean | null;
  lines: CreateRecurringLineDto[];
}

export interface UpdateRecurringDocumentDto extends Omit<CreateRecurringDocumentDto, 'kind'> {
  concurrencyStamp: string;
}

export interface RecurringDocumentQueryDto extends PagedQueryDto {
  keyword?: string;
  kind?: RecurringDocKind;
  status?: RecurringStatus;
  partyId?: string;
  dueBefore?: string;
}

export interface RecurringRunDto {
  id: string;
  recurringDocumentId: string;
  recurringDocumentName?: string | null;
  periodDate: string;
  status: RecurringRunStatus;
  docType?: string | null;
  docId?: string | null;
  docNumber?: string | null;
  posted: boolean;
  failReason?: string | null;
  creationTime: string;
}

export interface RecurringRunQueryDto extends PagedQueryDto {
  recurringDocumentId?: string;
  status?: RecurringRunStatus;
  from?: string;
  to?: string;
}

export interface RecurrencePreviewDto {
  dates: string[];
}

export interface RecurringSweepResultDto {
  templatesDue: number;
  generated: number;
  skipped: number;
  failed: number;
  runs: RecurringRunDto[];
}
