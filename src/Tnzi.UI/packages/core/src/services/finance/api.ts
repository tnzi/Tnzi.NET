/**
 * Finance Module API - Chart of accounts, journal entries, exchange rates,
 * fiscal years, and financial reports (admin endpoints of Tnzi.Finance)
 */

import type { HttpClient } from '../../http/http';
import type { PagedList } from '../../types/pagination';
import type {
  AccountDto,
  AccountTreeDto,
  CreateAccountDto,
  UpdateAccountDto,
  AccountQueryDto,
  JournalEntryDto,
  CreateJournalEntryDto,
  ReverseJournalEntryDto,
  JournalEntryQueryDto,
  ExchangeRateDto,
  UpsertExchangeRateDto,
  ExchangeRateQueryDto,
  FiscalYearDto,
  CreateFiscalYearDto,
  TrialBalanceReportDto,
  BalanceSheetReportDto,
  ProfitAndLossReportDto,
  GeneralLedgerReportDto,
  CustomerDto,
  CreateCustomerDto,
  UpdateCustomerDto,
  CustomerQueryDto,
  VendorDto,
  CreateVendorDto,
  UpdateVendorDto,
  VendorQueryDto,
  ItemDto,
  CreateItemDto,
  UpdateItemDto,
  ItemQueryDto,
  TaxAgencyDto,
  UpsertTaxAgencyDto,
  TaxRateDto,
  UpsertTaxRateDto,
  TaxCodeDto,
  UpsertTaxCodeDto,
  InvoiceDto,
  CreateInvoiceDto,
  InvoiceQueryDto,
  BillDto,
  CreateBillDto,
  BillQueryDto,
  ExpenseDto,
  CreateExpenseDto,
  ExpenseQueryDto,
  CreditMemoDto,
  CreateCreditMemoDto,
  CreditMemoQueryDto,
  PaymentEntryDto,
  CreatePaymentEntryDto,
  PaymentEntryQueryDto,
  ExternalPaymentIngestDto,
  PaymentApplicationDto,
  ApplySettlementDto,
  OpenDocumentDto,
  BatchPaymentDto,
  BatchPaymentResultDto,
  AgingReportDto,
  TaxSummaryReportDto,
  CashFlowReportDto,
  TransferDto,
  CreateTransferDto,
  TransferQueryDto,
  ReconciliationDto,
  CreateReconciliationDto,
  ReconciliationQueryDto,
  ReconciliationWorksheetDto,
  SetReconciliationLinesDto,
} from './types';
import type { SettlementDocType, FinancePartyType } from './metadata';

const ADMIN_ACCOUNT_BASE = '/admin/finance/accounts';
const ADMIN_JOURNAL_BASE = '/admin/finance/journal-entries';
const ADMIN_RATE_BASE = '/admin/finance/exchange-rates';
const ADMIN_FISCAL_BASE = '/admin/finance/fiscal-years';
const ADMIN_REPORT_BASE = '/admin/finance/reports';

/**
 * Admin Chart of Accounts API
 */
export function useAdminFinanceAccountApi(client: HttpClient) {
  return {
    /** Get paged account list */
    getList: (params?: AccountQueryDto) =>
      client.get<PagedList<AccountDto>>(ADMIN_ACCOUNT_BASE, { params }),

    /** Get the full account tree */
    getTree: (includeInactive = false) =>
      client.get<AccountTreeDto[]>(`${ADMIN_ACCOUNT_BASE}/tree`, { params: { includeInactive } }),

    /** Get account by ID */
    get: (id: string) =>
      client.get<AccountDto>(`${ADMIN_ACCOUNT_BASE}/${id}`),

    /** Create account */
    create: (data: CreateAccountDto) =>
      client.post<AccountDto>(ADMIN_ACCOUNT_BASE, data),

    /** Update account (rootType is immutable) */
    update: (id: string, data: UpdateAccountDto) =>
      client.put<AccountDto>(`${ADMIN_ACCOUNT_BASE}/${id}`, data),

    /** Delete account (rejected when it has children or journal lines) */
    delete: (id: string) =>
      client.delete<void>(`${ADMIN_ACCOUNT_BASE}/${id}`),

    /** Seed the default chart-of-accounts template (only into an empty chart) */
    seedDefault: () =>
      client.post<number>(`${ADMIN_ACCOUNT_BASE}/seed-default`),
  };
}

/**
 * Admin Journal Entry API
 */
export function useAdminJournalEntryApi(client: HttpClient) {
  return {
    /** Get paged journal entry list (headers only) */
    getList: (params?: JournalEntryQueryDto) =>
      client.get<PagedList<JournalEntryDto>>(ADMIN_JOURNAL_BASE, { params }),

    /** Get journal entry with lines */
    get: (id: string) =>
      client.get<JournalEntryDto>(`${ADMIN_JOURNAL_BASE}/${id}`),

    /** Create a draft entry */
    createDraft: (data: CreateJournalEntryDto) =>
      client.post<JournalEntryDto>(ADMIN_JOURNAL_BASE, data),

    /** Replace a draft entry (header + lines) */
    updateDraft: (id: string, data: CreateJournalEntryDto) =>
      client.put<JournalEntryDto>(`${ADMIN_JOURNAL_BASE}/${id}`, data),

    /** Delete a draft entry */
    deleteDraft: (id: string) =>
      client.delete<void>(`${ADMIN_JOURNAL_BASE}/${id}`),

    /** Post a draft entry to the general ledger */
    post: (id: string) =>
      client.post<JournalEntryDto>(`${ADMIN_JOURNAL_BASE}/${id}/post`),

    /** Reverse a posted entry (creates a mirrored entry) */
    reverse: (id: string, data?: ReverseJournalEntryDto) =>
      client.post<JournalEntryDto>(`${ADMIN_JOURNAL_BASE}/${id}/reverse`, data ?? {}),
  };
}

/**
 * Admin Exchange Rate API
 */
export function useAdminExchangeRateApi(client: HttpClient) {
  return {
    /** Get paged exchange rate list */
    getList: (params?: ExchangeRateQueryDto) =>
      client.get<PagedList<ExchangeRateDto>>(ADMIN_RATE_BASE, { params }),

    /** Upsert a rate (idempotent by currency pair + date) */
    upsert: (data: UpsertExchangeRateDto) =>
      client.post<ExchangeRateDto>(ADMIN_RATE_BASE, data),

    /** Delete a rate */
    delete: (id: string) =>
      client.delete<void>(`${ADMIN_RATE_BASE}/${id}`),

    /** Refresh latest rates from the registered IExchangeRateProvider (501 when none) */
    refresh: () =>
      client.post<number>(`${ADMIN_RATE_BASE}/refresh`),
  };
}

/**
 * Admin Fiscal Year API
 */
export function useAdminFiscalYearApi(client: HttpClient) {
  return {
    /** Get all fiscal years */
    getList: () =>
      client.get<FiscalYearDto[]>(ADMIN_FISCAL_BASE),

    /** Create a fiscal year (ranges must not overlap) */
    create: (data: CreateFiscalYearDto) =>
      client.post<FiscalYearDto>(ADMIN_FISCAL_BASE, data),

    /** Close a fiscal year (locks posting within its range) */
    close: (id: string) =>
      client.post<void>(`${ADMIN_FISCAL_BASE}/${id}/close`),

    /** Reopen a fiscal year */
    reopen: (id: string) =>
      client.post<void>(`${ADMIN_FISCAL_BASE}/${id}/reopen`),

    /** Delete a fiscal year */
    delete: (id: string) =>
      client.delete<void>(`${ADMIN_FISCAL_BASE}/${id}`),
  };
}

/**
 * Admin Financial Report API
 */
export function useAdminFinanceReportApi(client: HttpClient) {
  return {
    /** Trial balance for a date range */
    getTrialBalance: (from: string, to: string) =>
      client.get<TrialBalanceReportDto>(`${ADMIN_REPORT_BASE}/trial-balance`, { params: { from, to } }),

    /** Balance sheet as of a date */
    getBalanceSheet: (asOf: string) =>
      client.get<BalanceSheetReportDto>(`${ADMIN_REPORT_BASE}/balance-sheet`, { params: { asOf } }),

    /** Profit and loss for a date range */
    getProfitAndLoss: (from: string, to: string) =>
      client.get<ProfitAndLossReportDto>(`${ADMIN_REPORT_BASE}/profit-and-loss`, { params: { from, to } }),

    /** General ledger detail for one account (paged lines) */
    getGeneralLedger: (accountId: string, from: string, to: string, pageIndex = 1, pageSize = 20) =>
      client.get<GeneralLedgerReportDto>(`${ADMIN_REPORT_BASE}/general-ledger/${accountId}`, {
        params: { from, to, pageIndex, pageSize },
      }),

    /** AR aging as of a date */
    getArAging: (asOf: string) =>
      client.get<AgingReportDto>(`${ADMIN_REPORT_BASE}/ar-aging`, { params: { asOf } }),

    /** AP aging as of a date */
    getApAging: (asOf: string) =>
      client.get<AgingReportDto>(`${ADMIN_REPORT_BASE}/ap-aging`, { params: { asOf } }),

    /** Tax filing summary for a date range (output/input/net per agency and rate) */
    getTaxSummary: (from: string, to: string) =>
      client.get<TaxSummaryReportDto>(`${ADMIN_REPORT_BASE}/tax-summary`, { params: { from, to } }),

    /** Indirect-method cash flow statement (with identity check row) */
    getCashFlow: (from: string, to: string) =>
      client.get<CashFlowReportDto>(`${ADMIN_REPORT_BASE}/cash-flow`, { params: { from, to } }),

    /** Trial balance CSV export (UTF-8 BOM blob) */
    exportTrialBalanceCsv: (from: string, to: string) =>
      client.download(`${ADMIN_REPORT_BASE}/trial-balance/export`, { params: { from, to } }),

    /** Balance sheet CSV export */
    exportBalanceSheetCsv: (asOf: string) =>
      client.download(`${ADMIN_REPORT_BASE}/balance-sheet/export`, { params: { asOf } }),

    /** Profit and loss CSV export */
    exportProfitAndLossCsv: (from: string, to: string) =>
      client.download(`${ADMIN_REPORT_BASE}/profit-and-loss/export`, { params: { from, to } }),

    /** General ledger CSV export (full period with running balance; 400 when over the row cap) */
    exportGeneralLedgerCsv: (accountId: string, from: string, to: string) =>
      client.download(`${ADMIN_REPORT_BASE}/general-ledger/${accountId}/export`, { params: { from, to } }),

    /** AR aging CSV export */
    exportArAgingCsv: (asOf: string) =>
      client.download(`${ADMIN_REPORT_BASE}/ar-aging/export`, { params: { asOf } }),

    /** AP aging CSV export */
    exportApAgingCsv: (asOf: string) =>
      client.download(`${ADMIN_REPORT_BASE}/ap-aging/export`, { params: { asOf } }),

    /** Tax summary CSV export */
    exportTaxSummaryCsv: (from: string, to: string) =>
      client.download(`${ADMIN_REPORT_BASE}/tax-summary/export`, { params: { from, to } }),

    /** Cash flow statement CSV export */
    exportCashFlowCsv: (from: string, to: string) =>
      client.download(`${ADMIN_REPORT_BASE}/cash-flow/export`, { params: { from, to } }),
  };
}

const ADMIN_CUSTOMER_BASE = '/admin/finance/customers';
const ADMIN_VENDOR_BASE = '/admin/finance/vendors';
const ADMIN_ITEM_BASE = '/admin/finance/items';
const ADMIN_TAX_BASE = '/admin/finance/taxes';
const ADMIN_INVOICE_BASE = '/admin/finance/invoices';
const ADMIN_BILL_BASE = '/admin/finance/bills';
const ADMIN_EXPENSE_BASE = '/admin/finance/expenses';
const ADMIN_CREDIT_MEMO_BASE = '/admin/finance/credit-memos';
const ADMIN_PAYMENT_BASE = '/admin/finance/payments';
const ADMIN_SETTLEMENT_BASE = '/admin/finance/settlements';
const ADMIN_TRANSFER_BASE = '/admin/finance/transfers';
const ADMIN_RECONCILIATION_BASE = '/admin/finance/reconciliations';

/**
 * Admin Customer API
 */
export function useAdminFinanceCustomerApi(client: HttpClient) {
  return {
    getList: (params?: CustomerQueryDto) => client.get<PagedList<CustomerDto>>(ADMIN_CUSTOMER_BASE, { params }),
    get: (id: string) => client.get<CustomerDto>(`${ADMIN_CUSTOMER_BASE}/${id}`),
    create: (data: CreateCustomerDto) => client.post<CustomerDto>(ADMIN_CUSTOMER_BASE, data),
    update: (id: string, data: UpdateCustomerDto) => client.put<CustomerDto>(`${ADMIN_CUSTOMER_BASE}/${id}`, data),
    delete: (id: string) => client.delete<void>(`${ADMIN_CUSTOMER_BASE}/${id}`),
  };
}

/**
 * Admin Vendor API
 */
export function useAdminFinanceVendorApi(client: HttpClient) {
  return {
    getList: (params?: VendorQueryDto) => client.get<PagedList<VendorDto>>(ADMIN_VENDOR_BASE, { params }),
    get: (id: string) => client.get<VendorDto>(`${ADMIN_VENDOR_BASE}/${id}`),
    create: (data: CreateVendorDto) => client.post<VendorDto>(ADMIN_VENDOR_BASE, data),
    update: (id: string, data: UpdateVendorDto) => client.put<VendorDto>(`${ADMIN_VENDOR_BASE}/${id}`, data),
    delete: (id: string) => client.delete<void>(`${ADMIN_VENDOR_BASE}/${id}`),
  };
}

/**
 * Admin Item API
 */
export function useAdminFinanceItemApi(client: HttpClient) {
  return {
    getList: (params?: ItemQueryDto) => client.get<PagedList<ItemDto>>(ADMIN_ITEM_BASE, { params }),
    get: (id: string) => client.get<ItemDto>(`${ADMIN_ITEM_BASE}/${id}`),
    create: (data: CreateItemDto) => client.post<ItemDto>(ADMIN_ITEM_BASE, data),
    update: (id: string, data: UpdateItemDto) => client.put<ItemDto>(`${ADMIN_ITEM_BASE}/${id}`, data),
    delete: (id: string) => client.delete<void>(`${ADMIN_ITEM_BASE}/${id}`),
  };
}

/**
 * Admin Tax API (agencies / rates / codes)
 */
export function useAdminFinanceTaxApi(client: HttpClient) {
  return {
    getAgencies: () => client.get<TaxAgencyDto[]>(`${ADMIN_TAX_BASE}/agencies`),
    createAgency: (data: UpsertTaxAgencyDto) => client.post<TaxAgencyDto>(`${ADMIN_TAX_BASE}/agencies`, data),
    updateAgency: (id: string, data: UpsertTaxAgencyDto) => client.put<TaxAgencyDto>(`${ADMIN_TAX_BASE}/agencies/${id}`, data),
    deleteAgency: (id: string) => client.delete<void>(`${ADMIN_TAX_BASE}/agencies/${id}`),

    getRates: (agencyId?: string) => client.get<TaxRateDto[]>(`${ADMIN_TAX_BASE}/rates`, { params: agencyId ? { agencyId } : undefined }),
    createRate: (data: UpsertTaxRateDto) => client.post<TaxRateDto>(`${ADMIN_TAX_BASE}/rates`, data),
    updateRate: (id: string, data: UpsertTaxRateDto) => client.put<TaxRateDto>(`${ADMIN_TAX_BASE}/rates/${id}`, data),
    deleteRate: (id: string) => client.delete<void>(`${ADMIN_TAX_BASE}/rates/${id}`),

    getCodes: () => client.get<TaxCodeDto[]>(`${ADMIN_TAX_BASE}/codes`),
    createCode: (data: UpsertTaxCodeDto) => client.post<TaxCodeDto>(`${ADMIN_TAX_BASE}/codes`, data),
    updateCode: (id: string, data: UpsertTaxCodeDto) => client.put<TaxCodeDto>(`${ADMIN_TAX_BASE}/codes/${id}`, data),
    deleteCode: (id: string) => client.delete<void>(`${ADMIN_TAX_BASE}/codes/${id}`),
  };
}

function documentApi<TDto, TCreate, TQuery>(client: HttpClient, basePath: string) {
  return {
    getList: (params?: TQuery) => client.get<PagedList<TDto>>(basePath, { params: params as Record<string, unknown> | undefined }),
    get: (id: string) => client.get<TDto>(`${basePath}/${id}`),
    createDraft: (data: TCreate) => client.post<TDto>(basePath, data),
    updateDraft: (id: string, data: TCreate) => client.put<TDto>(`${basePath}/${id}`, data),
    deleteDraft: (id: string) => client.delete<void>(`${basePath}/${id}`),
    post: (id: string) => client.post<TDto>(`${basePath}/${id}/post`),
    void: (id: string) => client.post<TDto>(`${basePath}/${id}/void`),
  };
}

/** Admin Invoice API */
export function useAdminFinanceInvoiceApi(client: HttpClient) {
  return documentApi<InvoiceDto, CreateInvoiceDto, InvoiceQueryDto>(client, ADMIN_INVOICE_BASE);
}

/** Admin Bill API */
export function useAdminFinanceBillApi(client: HttpClient) {
  return documentApi<BillDto, CreateBillDto, BillQueryDto>(client, ADMIN_BILL_BASE);
}

/** Admin Expense API */
export function useAdminFinanceExpenseApi(client: HttpClient) {
  return documentApi<ExpenseDto, CreateExpenseDto, ExpenseQueryDto>(client, ADMIN_EXPENSE_BASE);
}

/** Admin Credit Memo API */
export function useAdminFinanceCreditMemoApi(client: HttpClient) {
  return documentApi<CreditMemoDto, CreateCreditMemoDto, CreditMemoQueryDto>(client, ADMIN_CREDIT_MEMO_BASE);
}

/** Admin Payment API (documents + idempotent external ingestion) */
export function useAdminFinancePaymentApi(client: HttpClient) {
  return {
    ...documentApi<PaymentEntryDto, CreatePaymentEntryDto, PaymentEntryQueryDto>(client, ADMIN_PAYMENT_BASE),
    createFromExternal: (data: ExternalPaymentIngestDto) =>
      client.post<PaymentEntryDto>(`${ADMIN_PAYMENT_BASE}/external`, data),
  };
}

/** Admin Settlement API */
export function useAdminFinanceSettlementApi(client: HttpClient) {
  return {
    getApplications: (docType: SettlementDocType, docId: string) =>
      client.get<PaymentApplicationDto[]>(`${ADMIN_SETTLEMENT_BASE}/applications`, { params: { docType, docId } }),
    getOpenDocuments: (partyType: FinancePartyType, partyId: string) =>
      client.get<OpenDocumentDto[]>(`${ADMIN_SETTLEMENT_BASE}/open-documents`, { params: { partyType, partyId } }),
    apply: (data: ApplySettlementDto) =>
      client.post<PaymentApplicationDto[]>(`${ADMIN_SETTLEMENT_BASE}/apply`, data),
    unapply: (applicationId: string) =>
      client.delete<void>(`${ADMIN_SETTLEMENT_BASE}/applications/${applicationId}`),
    /** Batch settlement (Pay Bills / Receive Payments); atomic: any failure rolls back the whole batch. */
    pay: (data: BatchPaymentDto) =>
      client.post<BatchPaymentResultDto>(`${ADMIN_SETTLEMENT_BASE}/pay`, data),
  };
}

/** Admin Transfer API (funds transfer document workflow) */
export function useAdminFinanceTransferApi(client: HttpClient) {
  return documentApi<TransferDto, CreateTransferDto, TransferQueryDto>(client, ADMIN_TRANSFER_BASE);
}

/** Admin Bank Reconciliation API */
export function useAdminFinanceReconciliationApi(client: HttpClient) {
  return {
    getList: (params?: ReconciliationQueryDto) =>
      client.get<PagedList<ReconciliationDto>>(ADMIN_RECONCILIATION_BASE, { params }),
    get: (id: string) => client.get<ReconciliationDto>(`${ADMIN_RECONCILIATION_BASE}/${id}`),
    create: (data: CreateReconciliationDto) =>
      client.post<ReconciliationDto>(ADMIN_RECONCILIATION_BASE, data),
    update: (id: string, data: CreateReconciliationDto) =>
      client.put<ReconciliationDto>(`${ADMIN_RECONCILIATION_BASE}/${id}`, data),
    delete: (id: string) => client.delete<void>(`${ADMIN_RECONCILIATION_BASE}/${id}`),
    /** Worksheet: selected + candidate lines with the live difference */
    getWorksheet: (id: string) =>
      client.get<ReconciliationWorksheetDto>(`${ADMIN_RECONCILIATION_BASE}/${id}/worksheet`),
    /** Full replacement of the cleared-line selection (draft only) */
    setLines: (id: string, data: SetReconciliationLinesDto) =>
      client.put<ReconciliationWorksheetDto>(`${ADMIN_RECONCILIATION_BASE}/${id}/lines`, data),
    /** Lock the reconciliation (difference must be 0) */
    complete: (id: string) =>
      client.post<ReconciliationDto>(`${ADMIN_RECONCILIATION_BASE}/${id}/complete`),
  };
}
