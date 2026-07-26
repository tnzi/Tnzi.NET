/**
 * Finance Module API - Chart of accounts, journal entries, exchange rates,
 * fiscal years, and financial reports (admin endpoints of Tnzi.Finance)
 */

import type { HttpClient } from '../../http/http';
import type { PagedList } from '../../types/pagination';
import type {
  AccountDto,
  AccountTreeDto,
  AccountBalanceDto,
  GetAccountBalancesDto,
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
  PartyLedgerSummaryDto,
  PartyLedgerEntryDto,
  PartyLedgerQuery,
  EstimateDto,
  CreateEstimateDto,
  EstimateQueryDto,
  PurchaseOrderDto,
  CreatePurchaseOrderDto,
  PurchaseOrderQueryDto,
  ConvertOfferDto,
  ConvertOfferResultDto,
  BankRuleDto,
  CreateBankRuleDto,
  BankRuleQuery,
  ReorderBankRulesDto,
  TestBankRuleDto,
  BankRuleTestResultDto,
  DocumentAttachmentDto,
  CreateDocumentAttachmentDto,
  DocumentCommentDto,
  CreateDocumentCommentDto,
  LedgerLockDto,
  SetLedgerLockDto,
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
  RunRevaluationDto,
  RevaluationPreviewDto,
  BankAccountDto,
  BankAccountCapabilitiesDto,
  CreateBankAccountDto,
  UpdateBankAccountDto,
  SetNextCheckNumberDto,
  BankAccountQueryDto,
  PartyBankAccountDto,
  SavePartyBankAccountDto,
  PartyBankAccountQueryDto,
  BankTransactionDto,
  BankTransactionQueryDto,
  CsvMappingDto,
  BankImportResultDto,
  PullBankFeedDto,
  BankSuggestResultDto,
  ConfirmBankMatchDto,
  BankMatchCandidateDto,
  CreateBankDocumentDto,
  BankDocumentResultDto,
  BankImportBatchDto,
  BankImportBatchQueryDto,
  BankCheckDto,
  CheckQueueItemDto,
  PrintChecksDto,
  RegisterManualCheckDto,
  VoidCheckDto,
  SpoilCheckDto,
  CheckQueryDto,
  EftBatchDto,
  EftQueueItemDto,
  CreateEftBatchDto,
  VoidEftBatchDto,
  EftBatchQueryDto,
  ReceiptDto,
  CreateReceiptDto,
  UpdateReceiptExtractionDto,
  ConvertReceiptDto,
  ReceiptConvertResultDto,
  ReceiptQueryDto,
  BalanceSummaryRebuildDto,
  BalanceSummaryVerifyDto,
  CustomerStatementDto,
  CustomerStatementQueryDto,
  DunningCandidateDto,
  TaxReturnDto,
  TaxReturnFormDto,
  RecurringDocumentDto,
  CreateRecurringDocumentDto,
  UpdateRecurringDocumentDto,
  RecurringDocumentQueryDto,
  RecurringRunDto,
  RecurringRunQueryDto,
  RecurrencePreviewDto,
  RecurringSweepResultDto,
} from './types';
import type { SettlementDocType, FinancePartyType, BankTransactionSource } from './metadata';

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

    /**
     * Read base-currency balances for a set of accounts, as of end of `asOf`
     * (posted lines only; omit `asOf` for today).
     *
     * POST carries the id list in the body - a page of account GUIDs overflows a
     * URL. Shares the reports' aggregation path, so the as-of bound matches the
     * balance sheet exactly (future-dated postings are excluded) and the
     * `Finance:Reports:UseBalanceSummary` fast path applies. Group accounts are
     * always 0 (only leaves carry lines).
     */
    getBalances: (data: GetAccountBalancesDto) =>
      client.post<AccountBalanceDto[]>(`${ADMIN_ACCOUNT_BASE}/balances`, data),

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

    /**
     * Read the rolling closing date (books closed through), plus whether a
     * password guards changing it.
     */
    getClosingDate: () =>
      client.get<LedgerLockDto>(`${ADMIN_FISCAL_BASE}/closing-date`),

    /** Set / advance / clear the closing date (password required once set). */
    setClosingDate: (data: SetLedgerLockDto) =>
      client.put<LedgerLockDto>(`${ADMIN_FISCAL_BASE}/closing-date`, data),

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
    /**
     * General-ledger detail for one account.
     *
     * `filter` mirrors the backend's optional `GeneralLedgerFilterDto`:
     * `keyword` (entry/line memo, entry number, payment reference, issued
     * cheque number, party name) and `sourceType` push down to SQL, so paging
     * and `totalCount` are the filtered figures. When a filter is active the
     * backend sets `isFiltered` and zeroes the running/opening/closing
     * balances - a filtered row chain has no continuous balance, and reporting
     * one would be a lie rather than a rounding issue. `descending` flips to
     * newest-first (online-banking order) without changing any row's balance.
     */
    getGeneralLedger: (
      accountId: string,
      from: string,
      to: string,
      pageIndex = 1,
      pageSize = 20,
      filter?: { keyword?: string; sourceType?: string; descending?: boolean },
    ) =>
      client.get<GeneralLedgerReportDto>(`${ADMIN_REPORT_BASE}/general-ledger/${accountId}`, {
        params: {
          from,
          to,
          pageIndex,
          pageSize,
          ...(filter?.keyword ? { keyword: filter.keyword } : {}),
          ...(filter?.sourceType ? { sourceType: filter.sourceType } : {}),
          ...(filter?.descending ? { descending: true } : {}),
        },
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
const ADMIN_DOC_COLLAB_BASE = '/admin/finance/documents';
const ADMIN_BANK_RULE_BASE = '/admin/finance/bank-rules';
const ADMIN_ESTIMATE_BASE = '/admin/finance/estimates';
const ADMIN_PURCHASE_ORDER_BASE = '/admin/finance/purchase-orders';
const ADMIN_INVOICE_BASE = '/admin/finance/invoices';
const ADMIN_BILL_BASE = '/admin/finance/bills';
const ADMIN_EXPENSE_BASE = '/admin/finance/expenses';
const ADMIN_CREDIT_MEMO_BASE = '/admin/finance/credit-memos';
const ADMIN_PAYMENT_BASE = '/admin/finance/payments';
const ADMIN_SETTLEMENT_BASE = '/admin/finance/settlements';
const ADMIN_TRANSFER_BASE = '/admin/finance/transfers';
const ADMIN_RECONCILIATION_BASE = '/admin/finance/reconciliations';
const ADMIN_REVALUATION_BASE = '/admin/finance/revaluations';

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
    /**
     * Work-surface summary: open balance, overdue, aging buckets, period total.
     * Ties out with the aging report by construction.
     */
    getSummary: (id: string, params?: { asOf?: string; from?: string; to?: string }) =>
      client.get<PartyLedgerSummaryDto>(`${ADMIN_CUSTOMER_BASE}/${id}/summary`, { params }),

    /** Transaction ledger across document types, newest first. */
    getTransactions: (id: string, params?: PartyLedgerQuery) =>
      client.get<PagedList<PartyLedgerEntryDto>>(`${ADMIN_CUSTOMER_BASE}/${id}/transactions`, { params }),

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
    /**
     * Work-surface summary: open balance, overdue, aging buckets, period total.
     * Ties out with the aging report by construction.
     */
    getSummary: (id: string, params?: { asOf?: string; from?: string; to?: string }) =>
      client.get<PartyLedgerSummaryDto>(`${ADMIN_VENDOR_BASE}/${id}/summary`, { params }),

    /** Transaction ledger across document types, newest first. */
    getTransactions: (id: string, params?: PartyLedgerQuery) =>
      client.get<PagedList<PartyLedgerEntryDto>>(`${ADMIN_VENDOR_BASE}/${id}/transactions`, { params }),

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

/**
 * Shared surface of the two non-posting documents.
 *
 * The lifecycle verbs are business words (send / accept / decline / close), not
 * `post` / `void`: these documents never reach the ledger, and borrowing the
 * accounting verbs would suggest they do.
 */
function offerApi<TDto, TCreate, TQuery>(client: HttpClient, basePath: string, convertPath: string) {
  return {
    getList: (params?: TQuery) => client.get<PagedList<TDto>>(basePath, { params: params as Record<string, unknown> | undefined }),
    get: (id: string) => client.get<TDto>(`${basePath}/${id}`),
    createDraft: (data: TCreate) => client.post<TDto>(basePath, data),
    update: (id: string, data: TCreate) => client.put<TDto>(`${basePath}/${id}`, data),
    deleteDraft: (id: string) => client.delete<void>(`${basePath}/${id}`),
    /** Allocates the document number - this is the moment it becomes a fact. */
    send: (id: string) => client.post<TDto>(`${basePath}/${id}/send`),
    accept: (id: string) => client.post<TDto>(`${basePath}/${id}/accept`),
    decline: (id: string) => client.post<TDto>(`${basePath}/${id}/decline`),
    close: (id: string) => client.post<TDto>(`${basePath}/${id}/close`),
    /** Produces a DRAFT invoice / bill; posting stays a separate human decision. */
    convert: (id: string, data: ConvertOfferDto) =>
      client.post<ConvertOfferResultDto>(`${basePath}/${id}/${convertPath}`, data),
  };
}

/**
 * Attachments and discussion on any finance document.
 *
 * Addressed polymorphically by `{docType}/{docId}` using the same source-token
 * vocabulary as the ledger, so a consuming app's own document types work
 * without a line of extra code.
 */
export function useAdminFinanceDocumentCollaborationApi(client: HttpClient) {
  return {
    listAttachments: (docType: string, docId: string) =>
      client.get<DocumentAttachmentDto[]>(`${ADMIN_DOC_COLLAB_BASE}/${docType}/${docId}/attachments`),
    attach: (docType: string, docId: string, data: CreateDocumentAttachmentDto) =>
      client.post<DocumentAttachmentDto>(`${ADMIN_DOC_COLLAB_BASE}/${docType}/${docId}/attachments`, data),
    removeAttachment: (id: string) =>
      client.delete<void>(`${ADMIN_DOC_COLLAB_BASE}/attachments/${id}`),
    /** Paperclip badges for a whole list page in one call. */
    attachmentCounts: (docType: string, docIds: string[]) =>
      client.post<Record<string, number>>(`${ADMIN_DOC_COLLAB_BASE}/${docType}/attachment-counts`, docIds),

    listComments: (docType: string, docId: string) =>
      client.get<DocumentCommentDto[]>(`${ADMIN_DOC_COLLAB_BASE}/${docType}/${docId}/comments`),
    postComment: (docType: string, docId: string, data: CreateDocumentCommentDto) =>
      client.post<DocumentCommentDto>(`${ADMIN_DOC_COLLAB_BASE}/${docType}/${docId}/comments`, data),
    deleteComment: (id: string) =>
      client.delete<void>(`${ADMIN_DOC_COLLAB_BASE}/comments/${id}`),
  };
}

/** Admin Bank Rule API */
export function useAdminFinanceBankRuleApi(client: HttpClient) {
  return {
    getList: (params?: BankRuleQuery) => client.get<PagedList<BankRuleDto>>(ADMIN_BANK_RULE_BASE, { params }),
    get: (id: string) => client.get<BankRuleDto>(`${ADMIN_BANK_RULE_BASE}/${id}`),
    create: (data: CreateBankRuleDto) => client.post<BankRuleDto>(ADMIN_BANK_RULE_BASE, data),
    update: (id: string, data: CreateBankRuleDto) => client.put<BankRuleDto>(`${ADMIN_BANK_RULE_BASE}/${id}`, data),
    delete: (id: string) => client.delete<void>(`${ADMIN_BANK_RULE_BASE}/${id}`),
    reorder: (data: ReorderBankRulesDto) => client.post<void>(`${ADMIN_BANK_RULE_BASE}/reorder`, data),
    /** Dry run: which pending lines this rule would take, and who actually wins them. */
    test: (id: string, data: TestBankRuleDto) =>
      client.post<BankRuleTestResultDto>(`${ADMIN_BANK_RULE_BASE}/${id}/test`, data),
  };
}

/** Admin Estimate (quote) API */
export function useAdminFinanceEstimateApi(client: HttpClient) {
  return offerApi<EstimateDto, CreateEstimateDto, EstimateQueryDto>(client, ADMIN_ESTIMATE_BASE, 'convert-to-invoice');
}

/** Admin Purchase Order API */
export function useAdminFinancePurchaseOrderApi(client: HttpClient) {
  return offerApi<PurchaseOrderDto, CreatePurchaseOrderDto, PurchaseOrderQueryDto>(client, ADMIN_PURCHASE_ORDER_BASE, 'convert-to-bill');
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

/** Admin unrealized FX revaluation API (period-end delta-to-target) */
export function useAdminFinanceRevaluationApi(client: HttpClient) {
  return {
    /** Preview the revaluation (no posting) */
    preview: (data: RunRevaluationDto) =>
      client.post<RevaluationPreviewDto>(`${ADMIN_REVALUATION_BASE}/preview`, data),
    /** Run the revaluation (posts a summary voucher; no-op when the increment is 0) */
    run: (data: RunRevaluationDto) =>
      client.post<RevaluationPreviewDto>(`${ADMIN_REVALUATION_BASE}/run`, data),
  };
}

const ADMIN_BANK_ACCOUNT_BASE = '/admin/finance/bank-accounts';
const ADMIN_PARTY_BANK_BASE = '/admin/finance/party-bank-accounts';
const ADMIN_BANK_FEED_BASE = '/admin/finance/bank-feed';

/** Admin Bank Account API (bank account profile CRUD + check-number reset) */
export function useAdminFinanceBankAccountApi(client: HttpClient) {
  return {
    /** Deployment capabilities (whether account numbers can be stored at all). */
    getCapabilities: () =>
      client.get<BankAccountCapabilitiesDto>(`${ADMIN_BANK_ACCOUNT_BASE}/capabilities`),
    getList: (params?: BankAccountQueryDto) =>
      client.get<PagedList<BankAccountDto>>(ADMIN_BANK_ACCOUNT_BASE, { params }),
    get: (id: string) => client.get<BankAccountDto>(`${ADMIN_BANK_ACCOUNT_BASE}/${id}`),
    create: (data: CreateBankAccountDto) => client.post<BankAccountDto>(ADMIN_BANK_ACCOUNT_BASE, data),
    update: (id: string, data: UpdateBankAccountDto) =>
      client.put<BankAccountDto>(`${ADMIN_BANK_ACCOUNT_BASE}/${id}`, data),
    /** Set the next check number (jump = new check book). */
    setNextCheckNumber: (id: string, data: SetNextCheckNumberDto) =>
      client.put<BankAccountDto>(`${ADMIN_BANK_ACCOUNT_BASE}/${id}/next-check-number`, data),
    delete: (id: string) => client.delete<void>(`${ADMIN_BANK_ACCOUNT_BASE}/${id}`),
  };
}

/** Admin Party Bank Account API (remit-to CRUD + by-party + set-default) */
export function useAdminFinancePartyBankAccountApi(client: HttpClient) {
  return {
    getList: (params?: PartyBankAccountQueryDto) =>
      client.get<PagedList<PartyBankAccountDto>>(ADMIN_PARTY_BANK_BASE, { params }),
    /** List a party's bank accounts (default first). */
    getByParty: (partyType: FinancePartyType, partyId: string) =>
      client.get<PartyBankAccountDto[]>(`${ADMIN_PARTY_BANK_BASE}/by-party`, { params: { partyType, partyId } }),
    get: (id: string) => client.get<PartyBankAccountDto>(`${ADMIN_PARTY_BANK_BASE}/${id}`),
    create: (data: SavePartyBankAccountDto) => client.post<PartyBankAccountDto>(ADMIN_PARTY_BANK_BASE, data),
    update: (id: string, data: SavePartyBankAccountDto) =>
      client.put<PartyBankAccountDto>(`${ADMIN_PARTY_BANK_BASE}/${id}`, data),
    /** Mark this account as the party's default (clears the previous default). */
    setDefault: (id: string) => client.post<PartyBankAccountDto>(`${ADMIN_PARTY_BANK_BASE}/${id}/default`),
    delete: (id: string) => client.delete<void>(`${ADMIN_PARTY_BANK_BASE}/${id}`),
  };
}

/** Admin Bank Feed API (statement import, matching, and reconciliation hand-off) */
export function useAdminFinanceBankFeedApi(client: HttpClient) {
  return {
    /** Paged bank transactions. */
    getTransactions: (params?: BankTransactionQueryDto) =>
      client.get<PagedList<BankTransactionDto>>(`${ADMIN_BANK_FEED_BASE}/transactions`, { params }),
    /**
     * Import a statement file (OFX / CSV) as multipart:
     * file + accountId + source (+ optional CSV column mapping as JSON).
     */
    import: (accountId: string, source: BankTransactionSource, file: File, mapping?: CsvMappingDto) =>
      client.upload<BankImportResultDto>(`${ADMIN_BANK_FEED_BASE}/import`, file, {
        additionalData: {
          accountId,
          source,
          ...(mapping ? { mapping: JSON.stringify(mapping) } : {}),
        },
      }),
    /** Pull transactions from the registered bank feed provider (400 when none). */
    pull: (data: PullBankFeedDto) =>
      client.post<BankImportResultDto>(`${ADMIN_BANK_FEED_BASE}/pull`, data),
    /** Run the match engine over all pending transactions for the account. */
    suggest: (accountId: string) =>
      client.post<BankSuggestResultDto>(`${ADMIN_BANK_FEED_BASE}/suggest`, undefined, { params: { accountId } }),
    /** List match candidates for a transaction. */
    getCandidates: (id: string) =>
      client.get<BankMatchCandidateDto[]>(`${ADMIN_BANK_FEED_BASE}/transactions/${id}/candidates`),
    /** Confirm a match (generates the current draft reconciliation's cleared line). */
    confirm: (id: string, data: ConfirmBankMatchDto) =>
      client.post<BankTransactionDto>(`${ADMIN_BANK_FEED_BASE}/transactions/${id}/confirm`, data),
    /** Undo a match. */
    unmatch: (id: string) =>
      client.post<BankTransactionDto>(`${ADMIN_BANK_FEED_BASE}/transactions/${id}/unmatch`),
    /** Exclude a transaction. */
    exclude: (id: string) =>
      client.post<BankTransactionDto>(`${ADMIN_BANK_FEED_BASE}/transactions/${id}/exclude`),
    /** Restore an excluded transaction. */
    restore: (id: string) =>
      client.post<BankTransactionDto>(`${ADMIN_BANK_FEED_BASE}/transactions/${id}/restore`),
    /** Create a draft document from a transaction (permission = finance.document.create). */
    createDocument: (id: string, data: CreateBankDocumentDto) =>
      client.post<BankDocumentResultDto>(`${ADMIN_BANK_FEED_BASE}/transactions/${id}/create-document`, data),
    /** Paged import batches. */
    getBatches: (params?: BankImportBatchQueryDto) =>
      client.get<PagedList<BankImportBatchDto>>(`${ADMIN_BANK_FEED_BASE}/batches`, { params }),
    /** Undo an import batch (only when it has no matched lines). */
    deleteBatch: (id: string) => client.delete<void>(`${ADMIN_BANK_FEED_BASE}/batches/${id}`),
  };
}

const ADMIN_CHECK_BASE = '/admin/finance/checks';
const ADMIN_EFT_BATCH_BASE = '/admin/finance/eft-batches';
const ADMIN_RECEIPT_BASE = '/admin/finance/receipts';

/** Admin Check API (print queue, register book, print / void / spoil / reprint). */
export function useAdminFinanceCheckApi(client: HttpClient) {
  return {
    /** Print queue (posted outbound check payments awaiting print). */
    getQueue: (bankAccountId?: string) =>
      client.get<CheckQueueItemDto[]>(`${ADMIN_CHECK_BASE}/queue`, {
        params: bankAccountId ? { bankAccountId } : undefined,
      }),
    /** Paged check register. */
    getList: (params?: CheckQueryDto) =>
      client.get<PagedList<BankCheckDto>>(ADMIN_CHECK_BASE, { params }),
    /** Print checks (merged PDF blob). */
    print: (data: PrintChecksDto) =>
      client.download(`${ADMIN_CHECK_BASE}/print`, { method: 'POST', body: data }),
    /** Register a hand-written check. */
    register: (data: RegisterManualCheckDto) =>
      client.post<BankCheckDto>(`${ADMIN_CHECK_BASE}/register`, data),
    /** Reprint a check (void the original + new check; merged PDF blob). */
    reprint: (id: string) =>
      client.download(`${ADMIN_CHECK_BASE}/${id}/reprint`, { method: 'POST' }),
    /** Void a check. */
    void: (id: string, data: VoidCheckDto) =>
      client.post<BankCheckDto>(`${ADMIN_CHECK_BASE}/${id}/void`, data),
    /** Register a spoiled check. */
    spoil: (data: SpoilCheckDto) =>
      client.post<BankCheckDto>(`${ADMIN_CHECK_BASE}/spoil`, data),
    /** Alignment calibration test page (PDF blob). */
    calibration: (bankAccountId: string) =>
      client.download(`${ADMIN_CHECK_BASE}/calibration/${bankAccountId}`),
    /** Positive-pay issued-check file (CSV blob) for a bank account over an issue-date window. */
    exportPositivePay: (bankAccountId: string, from: string, to: string) =>
      client.download(`${ADMIN_CHECK_BASE}/positive-pay/${bankAccountId}/export`, { params: { from, to } }),
  };
}

/** Admin EFT Batch API (queue, batch CRUD, generate / download / void). */
export function useAdminFinanceEftBatchApi(client: HttpClient) {
  return {
    /** Batchable queue (posted outbound bank-transfer payments). */
    getQueue: () => client.get<EftQueueItemDto[]>(`${ADMIN_EFT_BATCH_BASE}/queue`),
    /** Paged batches. */
    getList: (params?: EftBatchQueryDto) =>
      client.get<PagedList<EftBatchDto>>(ADMIN_EFT_BATCH_BASE, { params }),
    /** Get a batch (with lines). */
    get: (id: string) => client.get<EftBatchDto>(`${ADMIN_EFT_BATCH_BASE}/${id}`),
    /** Create a draft batch. */
    create: (data: CreateEftBatchDto) => client.post<EftBatchDto>(ADMIN_EFT_BATCH_BASE, data),
    /** Generate the file (Draft → Generated; immutable afterwards). */
    generate: (id: string) => client.post<EftBatchDto>(`${ADMIN_EFT_BATCH_BASE}/${id}/generate`),
    /** Void a batch. */
    void: (id: string, data: VoidEftBatchDto) =>
      client.post<EftBatchDto>(`${ADMIN_EFT_BATCH_BASE}/${id}/void`, data),
    /**
     * Download the generated EFT file (plaintext blob). Carries full cleartext
     * account numbers, so the backend gates it on the separate
     * `finance.eft.download` permission.
     */
    download: (id: string) => client.download(`${ADMIN_EFT_BATCH_BASE}/${id}/download`),
  };
}

/** Admin Receipt Capture API (register / extract / correct / convert). */
export function useAdminFinanceReceiptApi(client: HttpClient) {
  return {
    getList: (params?: ReceiptQueryDto) =>
      client.get<PagedList<ReceiptDto>>(ADMIN_RECEIPT_BASE, { params }),
    get: (id: string) => client.get<ReceiptDto>(`${ADMIN_RECEIPT_BASE}/${id}`),
    /** Register a receipt after upload (fileId). */
    create: (data: CreateReceiptDto) => client.post<ReceiptDto>(ADMIN_RECEIPT_BASE, data),
    /** Extract fields (501 when no IReceiptExtractor is registered; load Tnzi.Finance.Ai for the default). */
    extract: (id: string) => client.post<ReceiptDto>(`${ADMIN_RECEIPT_BASE}/${id}/extract`),
    /** Manually correct the extracted fields. */
    update: (id: string, data: UpdateReceiptExtractionDto) =>
      client.put<ReceiptDto>(`${ADMIN_RECEIPT_BASE}/${id}`, data),
    /** Convert into an expense / bill draft (permission = finance.document.create). */
    convert: (id: string, data: ConvertReceiptDto) =>
      client.post<ReceiptConvertResultDto>(`${ADMIN_RECEIPT_BASE}/${id}/convert`, data),
    /** Delete a receipt (Converted rejected). */
    delete: (id: string) => client.delete<void>(`${ADMIN_RECEIPT_BASE}/${id}`),
  };
}

const ADMIN_BALANCE_SUMMARY_BASE = '/admin/finance/balance-summary';
const ADMIN_STATEMENT_BASE = '/admin/finance/statements';
const ADMIN_TAX_RETURN_BASE = '/admin/finance/tax-returns';
const ADMIN_RECURRING_BASE = '/admin/finance/recurring';

/**
 * Admin balance-summary maintenance API (Batch F). Both endpoints are POST
 * (side effects: verify takes a row lock, rebuild rewrites the buckets).
 */
export function useAdminFinanceBalanceSummaryApi(client: HttpClient) {
  return {
    /** Verify the buckets against the ledger (read-only diagnosis; Missing/Extra/Mismatch). */
    verify: () => client.post<BalanceSummaryVerifyDto>(`${ADMIN_BALANCE_SUMMARY_BASE}/verify`),
    /** Fully rebuild the current tenant's summary buckets from the ledger. */
    rebuild: () => client.post<BalanceSummaryRebuildDto>(`${ADMIN_BALANCE_SUMMARY_BASE}/rebuild`),
  };
}

/**
 * Admin customer / vendor statement API (P4-5).
 *
 * The figures reuse the aging report's own calculation, so what you post out
 * ties to what your own books say - the worst failure mode here is a statement
 * that disagrees with your aging by a few cents.
 */
export function useAdminFinanceStatementApi(client: HttpClient) {
  return {
    get: (partyType: FinancePartyType, partyId: string, params?: CustomerStatementQueryDto) =>
      client.get<CustomerStatementDto>(`${ADMIN_STATEMENT_BASE}/${partyType}/${partyId}`, { params }),
    /** Printable statement. 501 when no IStatementRenderer is registered. */
    download: (partyType: FinancePartyType, partyId: string, params?: CustomerStatementQueryDto) =>
      client.download(`${ADMIN_STATEMENT_BASE}/${partyType}/${partyId}/document`, { params }),
    /** Collections worklist, worst first. */
    dunning: (partyType: FinancePartyType, asOf?: string) =>
      client.get<DunningCandidateDto[]>(`${ADMIN_STATEMENT_BASE}/${partyType}/dunning`, { params: { asOf } }),
  };
}

/**
 * Admin tax return API (P4-7).
 *
 * Returns line amounts, not a filing file: electronic formats change by year
 * and need registration, so the framework stops at the numbers.
 */
export function useAdminFinanceTaxReturnApi(client: HttpClient) {
  return {
    /** Which forms this deployment can produce (empty = no country pack loaded). */
    forms: () => client.get<TaxReturnFormDto[]>(`${ADMIN_TAX_RETURN_BASE}/forms`),
    get: (country: string, formCode: string, from: string, to: string) =>
      client.get<TaxReturnDto>(`${ADMIN_TAX_RETURN_BASE}/${country}/${formCode}`, { params: { from, to } }),
  };
}

/** Admin recurring document API (P4-6) */
export function useAdminFinanceRecurringApi(client: HttpClient) {
  return {
    getList: (params?: RecurringDocumentQueryDto) =>
      client.get<PagedList<RecurringDocumentDto>>(ADMIN_RECURRING_BASE, { params }),
    get: (id: string) => client.get<RecurringDocumentDto>(`${ADMIN_RECURRING_BASE}/${id}`),
    create: (data: CreateRecurringDocumentDto) => client.post<RecurringDocumentDto>(ADMIN_RECURRING_BASE, data),
    update: (id: string, data: UpdateRecurringDocumentDto) =>
      client.put<RecurringDocumentDto>(`${ADMIN_RECURRING_BASE}/${id}`, data),
    delete: (id: string) => client.delete<void>(`${ADMIN_RECURRING_BASE}/${id}`),

    pause: (id: string) => client.post<RecurringDocumentDto>(`${ADMIN_RECURRING_BASE}/${id}/pause`),
    resume: (id: string) => client.post<RecurringDocumentDto>(`${ADMIN_RECURRING_BASE}/${id}/resume`),
    end: (id: string) => client.post<RecurringDocumentDto>(`${ADMIN_RECURRING_BASE}/${id}/end`),

    /** Dates only, nothing written - so the anchor rules can be checked before saving. */
    preview: (id: string, count = 6) =>
      client.get<RecurrencePreviewDto>(`${ADMIN_RECURRING_BASE}/${id}/preview`, { params: { count } }),
    previewSchedule: (data: CreateRecurringDocumentDto, count = 6) =>
      client.post<RecurrencePreviewDto>(`${ADMIN_RECURRING_BASE}/preview`, data, { params: { count } }),

    runs: (params?: RecurringRunQueryDto) =>
      client.get<PagedList<RecurringRunDto>>(`${ADMIN_RECURRING_BASE}/runs`, { params }),
    /** Generate this template's due periods now. Repeat clicks are safe. */
    run: (id: string, asOf?: string) =>
      client.post<RecurringSweepResultDto>(`${ADMIN_RECURRING_BASE}/${id}/run`, undefined, { params: { asOf } }),
    runDue: (asOf?: string) =>
      client.post<RecurringSweepResultDto>(`${ADMIN_RECURRING_BASE}/run-due`, undefined, { params: { asOf } }),
  };
}
