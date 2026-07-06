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
  lines: ExpenseLineDto[];
}

export interface CreateExpenseDto {
  vendorId?: string | null;
  paidFromAccountId: string;
  docDate: string;
  currency?: string | null;
  exchangeRate?: number | null;
  memo?: string | null;
  lines: CreateExpenseLineDto[];
}

export type ExpenseQueryDto = BillQueryDto;

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
  reference?: string | null;
  memo?: string | null;
}

export interface PaymentEntryQueryDto extends PagedQueryDto {
  keyword?: string;
  status?: FinanceDocumentStatus;
  direction?: PaymentDirection;
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
