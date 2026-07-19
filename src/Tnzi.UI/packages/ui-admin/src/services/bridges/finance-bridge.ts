/**
 * Finance bridge — thin adapter over `@tnzi/core`'s admin finance APIs
 * (`/admin/finance/*` exposed by the Tnzi.Finance module's five
 * `DefaultFinance*AdminController`s: chart of accounts, journal entries,
 * exchange rates, fiscal years, and financial reports).
 *
 * Pages import DTO types from this bridge (never from `@tnzi/core/services/*`
 * directly) so integration tests can `vi.mock` the whole module.
 */
import type { HttpClient } from '@tnzi/core/http'
import {
  useAdminFinanceAccountApi,
  useAdminJournalEntryApi,
  useAdminExchangeRateApi,
  useAdminFiscalYearApi,
  useAdminFinanceReportApi,
  useAdminFinanceCustomerApi,
  useAdminFinanceVendorApi,
  useAdminFinanceItemApi,
  useAdminFinanceTaxApi,
  useAdminFinanceInvoiceApi,
  useAdminFinanceBillApi,
  useAdminFinanceExpenseApi,
  useAdminFinanceCreditMemoApi,
  useAdminFinancePaymentApi,
  useAdminFinanceSettlementApi,
  useAdminFinanceTransferApi,
  useAdminFinanceReconciliationApi,
  useAdminFinanceRevaluationApi,
  useAdminFinanceBankAccountApi,
  useAdminFinancePartyBankAccountApi,
  useAdminFinanceBankFeedApi,
  useAdminFinanceCheckApi,
  useAdminFinanceEftBatchApi,
  useAdminFinanceReceiptApi,
  useAdminFinanceBalanceSummaryApi,
  type AccountDto as CoreAccountDto,
  type AccountTreeDto as CoreAccountTreeDto,
  type AccountBalanceDto as CoreAccountBalanceDto,
  type CreateAccountDto as CoreCreateAccountDto,
  type UpdateAccountDto as CoreUpdateAccountDto,
  type JournalEntryDto as CoreJournalEntryDto,
  type JournalLineDto as CoreJournalLineDto,
  type CreateJournalEntryDto as CoreCreateJournalEntryDto,
  type ReverseJournalEntryDto as CoreReverseJournalEntryDto,
  type ExchangeRateDto as CoreExchangeRateDto,
  type UpsertExchangeRateDto as CoreUpsertExchangeRateDto,
  type FiscalYearDto as CoreFiscalYearDto,
  type CreateFiscalYearDto as CoreCreateFiscalYearDto,
  type TrialBalanceReportDto as CoreTrialBalanceReportDto,
  type TrialBalanceRowDto as CoreTrialBalanceRowDto,
  type ReportAccountRowDto as CoreReportAccountRowDto,
  type BalanceSheetReportDto as CoreBalanceSheetReportDto,
  type ProfitAndLossReportDto as CoreProfitAndLossReportDto,
  type GeneralLedgerReportDto as CoreGeneralLedgerReportDto,
  type GeneralLedgerLineDto as CoreGeneralLedgerLineDto,
  type CustomerDto as CoreCustomerDto,
  type CreateCustomerDto as CoreCreateCustomerDto,
  type UpdateCustomerDto as CoreUpdateCustomerDto,
  type VendorDto as CoreVendorDto,
  type CreateVendorDto as CoreCreateVendorDto,
  type UpdateVendorDto as CoreUpdateVendorDto,
  type ItemDto as CoreItemDto,
  type CreateItemDto as CoreCreateItemDto,
  type UpdateItemDto as CoreUpdateItemDto,
  type TaxAgencyDto as CoreTaxAgencyDto,
  type UpsertTaxAgencyDto as CoreUpsertTaxAgencyDto,
  type TaxRateDto as CoreTaxRateDto,
  type UpsertTaxRateDto as CoreUpsertTaxRateDto,
  type TaxCodeDto as CoreTaxCodeDto,
  type UpsertTaxCodeDto as CoreUpsertTaxCodeDto,
  type InvoiceDto as CoreInvoiceDto,
  type CreateInvoiceDto as CoreCreateInvoiceDto,
  type BillDto as CoreBillDto,
  type CreateBillDto as CoreCreateBillDto,
  type ExpenseDto as CoreExpenseDto,
  type CreateExpenseDto as CoreCreateExpenseDto,
  type CreditMemoDto as CoreCreditMemoDto,
  type CreateCreditMemoDto as CoreCreateCreditMemoDto,
  type PaymentEntryDto as CorePaymentEntryDto,
  type CreatePaymentEntryDto as CoreCreatePaymentEntryDto,
  type SalesDocLineDto as CoreSalesDocLineDto,
  type CreateSalesDocLineDto as CoreCreateSalesDocLineDto,
  type ExpenseLineDto as CoreExpenseLineDto,
  type PaymentApplicationDto as CorePaymentApplicationDto,
  type ApplySettlementDto as CoreApplySettlementDto,
  type OpenDocumentDto as CoreOpenDocumentDto,
  type BatchPaymentDto as CoreBatchPaymentDto,
  type BatchPaymentResultDto as CoreBatchPaymentResultDto,
  type AgingReportDto as CoreAgingReportDto,
  type AgingRowDto as CoreAgingRowDto,
  type TaxSummaryReportDto as CoreTaxSummaryReportDto,
  type TaxSummaryRowDto as CoreTaxSummaryRowDto,
  type CashFlowReportDto as CoreCashFlowReportDto,
  type TransferDto as CoreTransferDto,
  type CreateTransferDto as CoreCreateTransferDto,
  type ReconciliationDto as CoreReconciliationDto,
  type CreateReconciliationDto as CoreCreateReconciliationDto,
  type ReconciliationWorksheetDto as CoreReconciliationWorksheetDto,
  type ReconciliationCandidateLineDto as CoreReconciliationCandidateLineDto,
  type RunRevaluationDto as CoreRunRevaluationDto,
  type RevaluationPreviewDto as CoreRevaluationPreviewDto,
  type RevaluationRowDto as CoreRevaluationRowDto,
  type BankAccountDto as CoreBankAccountDto,
  type BankAccountCapabilitiesDto as CoreBankAccountCapabilitiesDto,
  type CreateBankAccountDto as CoreCreateBankAccountDto,
  type UpdateBankAccountDto as CoreUpdateBankAccountDto,
  type SetNextCheckNumberDto as CoreSetNextCheckNumberDto,
  type PartyBankAccountDto as CorePartyBankAccountDto,
  type SavePartyBankAccountDto as CoreSavePartyBankAccountDto,
  type BankTransactionDto as CoreBankTransactionDto,
  type CsvMappingDto as CoreCsvMappingDto,
  type BankImportResultDto as CoreBankImportResultDto,
  type BankSuggestResultDto as CoreBankSuggestResultDto,
  type ConfirmBankMatchDto as CoreConfirmBankMatchDto,
  type BankMatchCandidateDto as CoreBankMatchCandidateDto,
  type CreateBankDocumentDto as CoreCreateBankDocumentDto,
  type BankDocumentResultDto as CoreBankDocumentResultDto,
  type BankImportBatchDto as CoreBankImportBatchDto,
  type BankTransactionSource as CoreBankTransactionSource,
  type BankCheckDto as CoreBankCheckDto,
  type CheckQueueItemDto as CoreCheckQueueItemDto,
  type PrintChecksDto as CorePrintChecksDto,
  type RegisterManualCheckDto as CoreRegisterManualCheckDto,
  type VoidCheckDto as CoreVoidCheckDto,
  type SpoilCheckDto as CoreSpoilCheckDto,
  type EftBatchDto as CoreEftBatchDto,
  type EftBatchLineDto as CoreEftBatchLineDto,
  type EftQueueItemDto as CoreEftQueueItemDto,
  type CreateEftBatchDto as CoreCreateEftBatchDto,
  type VoidEftBatchDto as CoreVoidEftBatchDto,
  type ReceiptDto as CoreReceiptDto,
  type CreateReceiptDto as CoreCreateReceiptDto,
  type UpdateReceiptExtractionDto as CoreUpdateReceiptExtractionDto,
  type ConvertReceiptDto as CoreConvertReceiptDto,
  type ReceiptConvertResultDto as CoreReceiptConvertResultDto,
  type BalanceSummaryRebuildDto as CoreBalanceSummaryRebuildDto,
  type BalanceSummaryVerifyDto as CoreBalanceSummaryVerifyDto,
  type BalanceSummaryDifferenceDto as CoreBalanceSummaryDifferenceDto,
} from '@tnzi/core/services/finance'
import type { PagedList } from '@tnzi/core'
import type { FinancePartyType, SettlementDocType } from '@tnzi/core/services/finance'
import { ensureOk, unwrapResult as unwrap, pagedResult } from '../_mappers'

// Re-export the DTO types under bridge names consumed by pages/configs.
export type AccountDto = CoreAccountDto
export type AccountTreeDto = CoreAccountTreeDto
export type AccountBalanceDto = CoreAccountBalanceDto
export type CreateAccountDto = CoreCreateAccountDto
export type UpdateAccountDto = CoreUpdateAccountDto
export type JournalEntryDto = CoreJournalEntryDto
export type JournalLineDto = CoreJournalLineDto
export type CreateJournalEntryDto = CoreCreateJournalEntryDto
export type ReverseJournalEntryDto = CoreReverseJournalEntryDto
export type ExchangeRateDto = CoreExchangeRateDto
export type UpsertExchangeRateDto = CoreUpsertExchangeRateDto
export type FiscalYearDto = CoreFiscalYearDto
export type CreateFiscalYearDto = CoreCreateFiscalYearDto
export type TrialBalanceReportDto = CoreTrialBalanceReportDto
export type TrialBalanceRowDto = CoreTrialBalanceRowDto
export type ReportAccountRowDto = CoreReportAccountRowDto
export type BalanceSheetReportDto = CoreBalanceSheetReportDto
export type ProfitAndLossReportDto = CoreProfitAndLossReportDto
export type GeneralLedgerReportDto = CoreGeneralLedgerReportDto
export type GeneralLedgerLineDto = CoreGeneralLedgerLineDto
export type CustomerDto = CoreCustomerDto
export type CreateCustomerDto = CoreCreateCustomerDto
export type UpdateCustomerDto = CoreUpdateCustomerDto
export type VendorDto = CoreVendorDto
export type CreateVendorDto = CoreCreateVendorDto
export type UpdateVendorDto = CoreUpdateVendorDto
export type ItemDto = CoreItemDto
export type CreateItemDto = CoreCreateItemDto
export type UpdateItemDto = CoreUpdateItemDto
export type TaxAgencyDto = CoreTaxAgencyDto
export type UpsertTaxAgencyDto = CoreUpsertTaxAgencyDto
export type TaxRateDto = CoreTaxRateDto
export type UpsertTaxRateDto = CoreUpsertTaxRateDto
export type TaxCodeDto = CoreTaxCodeDto
export type UpsertTaxCodeDto = CoreUpsertTaxCodeDto
export type InvoiceDto = CoreInvoiceDto
export type CreateInvoiceDto = CoreCreateInvoiceDto
export type BillDto = CoreBillDto
export type CreateBillDto = CoreCreateBillDto
export type ExpenseDto = CoreExpenseDto
export type CreateExpenseDto = CoreCreateExpenseDto
export type CreditMemoDto = CoreCreditMemoDto
export type CreateCreditMemoDto = CoreCreateCreditMemoDto
export type PaymentEntryDto = CorePaymentEntryDto
export type CreatePaymentEntryDto = CoreCreatePaymentEntryDto
export type SalesDocLineDto = CoreSalesDocLineDto
export type CreateSalesDocLineDto = CoreCreateSalesDocLineDto
export type ExpenseLineDto = CoreExpenseLineDto
export type PaymentApplicationDto = CorePaymentApplicationDto
export type ApplySettlementDto = CoreApplySettlementDto
export type OpenDocumentDto = CoreOpenDocumentDto
export type BatchPaymentDto = CoreBatchPaymentDto
export type BatchPaymentResultDto = CoreBatchPaymentResultDto
export type AgingReportDto = CoreAgingReportDto
export type AgingRowDto = CoreAgingRowDto
export type TaxSummaryReportDto = CoreTaxSummaryReportDto
export type TaxSummaryRowDto = CoreTaxSummaryRowDto
export type CashFlowReportDto = CoreCashFlowReportDto
export type TransferDto = CoreTransferDto
export type CreateTransferDto = CoreCreateTransferDto
export type ReconciliationDto = CoreReconciliationDto
export type CreateReconciliationDto = CoreCreateReconciliationDto
export type ReconciliationWorksheetDto = CoreReconciliationWorksheetDto
export type ReconciliationCandidateLineDto = CoreReconciliationCandidateLineDto
export type RunRevaluationDto = CoreRunRevaluationDto
export type RevaluationPreviewDto = CoreRevaluationPreviewDto
export type RevaluationRowDto = CoreRevaluationRowDto
export type BankAccountDto = CoreBankAccountDto
export type BankAccountCapabilitiesDto = CoreBankAccountCapabilitiesDto
export type CreateBankAccountDto = CoreCreateBankAccountDto
export type UpdateBankAccountDto = CoreUpdateBankAccountDto
export type SetNextCheckNumberDto = CoreSetNextCheckNumberDto
export type PartyBankAccountDto = CorePartyBankAccountDto
export type SavePartyBankAccountDto = CoreSavePartyBankAccountDto
export type BankTransactionDto = CoreBankTransactionDto
export type CsvMappingDto = CoreCsvMappingDto
export type BankImportResultDto = CoreBankImportResultDto
export type BankSuggestResultDto = CoreBankSuggestResultDto
export type ConfirmBankMatchDto = CoreConfirmBankMatchDto
export type BankMatchCandidateDto = CoreBankMatchCandidateDto
export type CreateBankDocumentDto = CoreCreateBankDocumentDto
export type BankDocumentResultDto = CoreBankDocumentResultDto
export type BankImportBatchDto = CoreBankImportBatchDto
export type BankCheckDto = CoreBankCheckDto
export type CheckQueueItemDto = CoreCheckQueueItemDto
export type PrintChecksDto = CorePrintChecksDto
export type RegisterManualCheckDto = CoreRegisterManualCheckDto
export type VoidCheckDto = CoreVoidCheckDto
export type SpoilCheckDto = CoreSpoilCheckDto
export type EftBatchDto = CoreEftBatchDto
export type EftBatchLineDto = CoreEftBatchLineDto
export type EftQueueItemDto = CoreEftQueueItemDto
export type CreateEftBatchDto = CoreCreateEftBatchDto
export type VoidEftBatchDto = CoreVoidEftBatchDto
export type ReceiptDto = CoreReceiptDto
export type CreateReceiptDto = CoreCreateReceiptDto
export type UpdateReceiptExtractionDto = CoreUpdateReceiptExtractionDto
export type ConvertReceiptDto = CoreConvertReceiptDto
export type ReceiptConvertResultDto = CoreReceiptConvertResultDto
export type BalanceSummaryRebuildDto = CoreBalanceSummaryRebuildDto
export type BalanceSummaryVerifyDto = CoreBalanceSummaryVerifyDto
export type BalanceSummaryDifferenceDto = CoreBalanceSummaryDifferenceDto

export {
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
  PAYMENT_METHODS,
  FINANCE_SOURCE_TYPES,
} from '@tnzi/core/services/finance'

/** Page-facing paged query (CrudPageQuery-compatible subset). */
export interface FinancePagedQuery {
  pageIndex: number
  pageSize: number
  searchText?: string
  filters?: Record<string, unknown>
}

export type FinancePagedResult<T> = PagedList<T>

export interface FinanceBridgeDeps {
  client?: HttpClient
}

export interface FinanceBridge {
  accounts: {
    fetch(query: FinancePagedQuery): Promise<FinancePagedResult<AccountDto>>
    tree(includeInactive?: boolean): Promise<AccountTreeDto[]>
    /** Base-currency balances as of end of `asOf` (default today); posted lines only. */
    balances(accountIds: string[], asOf?: string): Promise<AccountBalanceDto[]>
    create(data: CoreCreateAccountDto): Promise<AccountDto>
    update(id: string, data: CoreUpdateAccountDto): Promise<AccountDto>
    delete(ids: string[]): Promise<void>
    seedDefault(): Promise<number>
  }
  journals: {
    fetch(query: FinancePagedQuery): Promise<FinancePagedResult<JournalEntryDto>>
    getById(id: string): Promise<JournalEntryDto | null>
    createDraft(data: CoreCreateJournalEntryDto): Promise<JournalEntryDto>
    updateDraft(id: string, data: CoreCreateJournalEntryDto): Promise<JournalEntryDto>
    deleteDraft(id: string): Promise<void>
    post(id: string): Promise<JournalEntryDto>
    reverse(id: string, data?: CoreReverseJournalEntryDto): Promise<JournalEntryDto>
  }
  rates: {
    fetch(query: FinancePagedQuery): Promise<FinancePagedResult<ExchangeRateDto>>
    upsert(data: CoreUpsertExchangeRateDto): Promise<ExchangeRateDto>
    delete(ids: string[]): Promise<void>
    refresh(): Promise<number>
  }
  fiscalYears: {
    list(): Promise<FiscalYearDto[]>
    create(data: CoreCreateFiscalYearDto): Promise<FiscalYearDto>
    close(id: string): Promise<void>
    reopen(id: string): Promise<void>
    delete(ids: string[]): Promise<void>
  }
  reports: {
    trialBalance(from: string, to: string): Promise<TrialBalanceReportDto>
    balanceSheet(asOf: string): Promise<BalanceSheetReportDto>
    profitAndLoss(from: string, to: string): Promise<ProfitAndLossReportDto>
    generalLedger(accountId: string, from: string, to: string, pageIndex?: number, pageSize?: number): Promise<GeneralLedgerReportDto>
    arAging(asOf: string): Promise<AgingReportDto>
    apAging(asOf: string): Promise<AgingReportDto>
    taxSummary(from: string, to: string): Promise<TaxSummaryReportDto>
    cashFlow(from: string, to: string): Promise<CashFlowReportDto>
    exportTrialBalanceCsv(from: string, to: string): Promise<Blob>
    exportBalanceSheetCsv(asOf: string): Promise<Blob>
    exportProfitAndLossCsv(from: string, to: string): Promise<Blob>
    exportGeneralLedgerCsv(accountId: string, from: string, to: string): Promise<Blob>
    exportArAgingCsv(asOf: string): Promise<Blob>
    exportApAgingCsv(asOf: string): Promise<Blob>
    exportTaxSummaryCsv(from: string, to: string): Promise<Blob>
    exportCashFlowCsv(from: string, to: string): Promise<Blob>
  }
  customers: {
    fetch(query: FinancePagedQuery): Promise<FinancePagedResult<CustomerDto>>
    create(data: CoreCreateCustomerDto): Promise<CustomerDto>
    update(id: string, data: CoreUpdateCustomerDto): Promise<CustomerDto>
    delete(ids: string[]): Promise<void>
  }
  vendors: {
    fetch(query: FinancePagedQuery): Promise<FinancePagedResult<VendorDto>>
    create(data: CoreCreateVendorDto): Promise<VendorDto>
    update(id: string, data: CoreUpdateVendorDto): Promise<VendorDto>
    delete(ids: string[]): Promise<void>
  }
  items: {
    fetch(query: FinancePagedQuery): Promise<FinancePagedResult<ItemDto>>
    create(data: CoreCreateItemDto): Promise<ItemDto>
    update(id: string, data: CoreUpdateItemDto): Promise<ItemDto>
    delete(ids: string[]): Promise<void>
  }
  taxes: {
    agencies(): Promise<TaxAgencyDto[]>
    createAgency(data: CoreUpsertTaxAgencyDto): Promise<TaxAgencyDto>
    updateAgency(id: string, data: CoreUpsertTaxAgencyDto): Promise<TaxAgencyDto>
    deleteAgency(id: string): Promise<void>
    rates(agencyId?: string): Promise<TaxRateDto[]>
    createRate(data: CoreUpsertTaxRateDto): Promise<TaxRateDto>
    updateRate(id: string, data: CoreUpsertTaxRateDto): Promise<TaxRateDto>
    deleteRate(id: string): Promise<void>
    codes(): Promise<TaxCodeDto[]>
    createCode(data: CoreUpsertTaxCodeDto): Promise<TaxCodeDto>
    updateCode(id: string, data: CoreUpsertTaxCodeDto): Promise<TaxCodeDto>
    deleteCode(id: string): Promise<void>
  }
  invoices: FinanceDocSection<InvoiceDto, CoreCreateInvoiceDto>
  bills: FinanceDocSection<BillDto, CoreCreateBillDto>
  expenses: FinanceDocSection<ExpenseDto, CoreCreateExpenseDto>
  creditMemos: FinanceDocSection<CreditMemoDto, CoreCreateCreditMemoDto>
  payments: FinanceDocSection<PaymentEntryDto, CoreCreatePaymentEntryDto>
  transfers: FinanceDocSection<TransferDto, CoreCreateTransferDto>
  reconciliations: {
    fetch(query: FinancePagedQuery): Promise<FinancePagedResult<ReconciliationDto>>
    getById(id: string): Promise<ReconciliationDto | null>
    create(data: CoreCreateReconciliationDto): Promise<ReconciliationDto>
    update(id: string, data: CoreCreateReconciliationDto): Promise<ReconciliationDto>
    delete(id: string): Promise<void>
    worksheet(id: string): Promise<ReconciliationWorksheetDto>
    setLines(id: string, journalLineIds: string[]): Promise<ReconciliationWorksheetDto>
    complete(id: string): Promise<ReconciliationDto>
  }
  revaluations: {
    /** Preview the period-end revaluation (no posting) */
    preview(data: RunRevaluationDto): Promise<RevaluationPreviewDto>
    /** Run the revaluation (posts a summary voucher; no-op when the increment is 0) */
    run(data: RunRevaluationDto): Promise<RevaluationPreviewDto>
  }
  settlements: {
    applications(docType: SettlementDocType, docId: string): Promise<PaymentApplicationDto[]>
    openDocuments(partyType: FinancePartyType, partyId: string): Promise<OpenDocumentDto[]>
    apply(data: CoreApplySettlementDto): Promise<PaymentApplicationDto[]>
    unapply(applicationId: string): Promise<void>
    /** Batch settlement (Pay Bills / Receive Payments); atomic on the backend. */
    pay(data: CoreBatchPaymentDto): Promise<BatchPaymentResultDto>
  }
  bankAccounts: {
    /** Deployment capabilities (whether account numbers can be stored at all). */
    capabilities(): Promise<BankAccountCapabilitiesDto>
    fetch(query: FinancePagedQuery): Promise<FinancePagedResult<BankAccountDto>>
    getById(id: string): Promise<BankAccountDto | null>
    create(data: CoreCreateBankAccountDto): Promise<BankAccountDto>
    update(id: string, data: CoreUpdateBankAccountDto): Promise<BankAccountDto>
    /** Set the next check number (jump = new check book). */
    setNextCheckNumber(id: string, nextCheckNumber: number): Promise<BankAccountDto>
    delete(ids: string[]): Promise<void>
  }
  partyBankAccounts: {
    /** List a party's bank accounts (default first). */
    byParty(partyType: FinancePartyType, partyId: string): Promise<PartyBankAccountDto[]>
    save(data: CoreSavePartyBankAccountDto): Promise<PartyBankAccountDto>
    update(id: string, data: CoreSavePartyBankAccountDto): Promise<PartyBankAccountDto>
    /** Mark this account as the party's default. */
    setDefault(id: string): Promise<PartyBankAccountDto>
    delete(id: string): Promise<void>
  }
  bankFeed: {
    transactions(query: FinancePagedQuery): Promise<FinancePagedResult<BankTransactionDto>>
    /** Import an OFX / CSV statement (multipart). */
    import(accountId: string, source: CoreBankTransactionSource, file: File, mapping?: CoreCsvMappingDto): Promise<BankImportResultDto>
    /** Pull from the registered bank feed provider (400 when none). */
    pull(accountId: string): Promise<BankImportResultDto>
    /** Run the match engine over all pending transactions for the account. */
    suggest(accountId: string): Promise<BankSuggestResultDto>
    candidates(transactionId: string): Promise<BankMatchCandidateDto[]>
    /** Confirm a match (generates a draft-reconciliation cleared line; 400 when no draft). */
    confirm(transactionId: string, data: CoreConfirmBankMatchDto): Promise<BankTransactionDto>
    unmatch(transactionId: string): Promise<BankTransactionDto>
    exclude(transactionId: string): Promise<BankTransactionDto>
    restore(transactionId: string): Promise<BankTransactionDto>
    /** Create a draft document from a transaction (pre-filled by sign). */
    createDocument(transactionId: string, data: CoreCreateBankDocumentDto): Promise<BankDocumentResultDto>
    batches(query: FinancePagedQuery): Promise<FinancePagedResult<BankImportBatchDto>>
    deleteBatch(id: string): Promise<void>
  }
  checks: {
    /** Print queue (posted outbound check payments awaiting print). */
    queue(bankAccountId?: string): Promise<CheckQueueItemDto[]>
    /** Paged check register. */
    fetch(query: FinancePagedQuery): Promise<FinancePagedResult<BankCheckDto>>
    /** Print checks → merged PDF blob. */
    print(data: CorePrintChecksDto): Promise<Blob>
    register(data: CoreRegisterManualCheckDto): Promise<BankCheckDto>
    /** Reprint (void the original + new check) → merged PDF blob. */
    reprint(id: string): Promise<Blob>
    voidCheck(id: string, data: CoreVoidCheckDto): Promise<BankCheckDto>
    spoil(data: CoreSpoilCheckDto): Promise<BankCheckDto>
    /** Alignment calibration test page → PDF blob. */
    calibration(bankAccountId: string): Promise<Blob>
    /** Positive-pay issued-check file (CSV blob) over an issue-date window. */
    exportPositivePay(bankAccountId: string, from: string, to: string): Promise<Blob>
  }
  eftBatches: {
    /** Batchable queue (posted outbound bank-transfer payments). */
    queue(): Promise<EftQueueItemDto[]>
    fetch(query: FinancePagedQuery): Promise<FinancePagedResult<EftBatchDto>>
    getById(id: string): Promise<EftBatchDto | null>
    create(data: CoreCreateEftBatchDto): Promise<EftBatchDto>
    /** Generate the file (Draft → Generated; immutable afterwards). */
    generate(id: string): Promise<EftBatchDto>
    voidBatch(id: string, data: CoreVoidEftBatchDto): Promise<EftBatchDto>
    /** Download the generated EFT file → plaintext blob (gated on finance.eft.download). */
    download(id: string): Promise<Blob>
  }
  receipts: {
    fetch(query: FinancePagedQuery): Promise<FinancePagedResult<ReceiptDto>>
    getById(id: string): Promise<ReceiptDto | null>
    create(data: CoreCreateReceiptDto): Promise<ReceiptDto>
    /** Extract fields (501 when no IReceiptExtractor is registered; load Tnzi.Finance.Ai for the default). */
    extract(id: string): Promise<ReceiptDto>
    update(id: string, data: CoreUpdateReceiptExtractionDto): Promise<ReceiptDto>
    convert(id: string, data: CoreConvertReceiptDto): Promise<ReceiptConvertResultDto>
    delete(id: string): Promise<void>
  }
  balanceSummary: {
    /** Verify the summary buckets against the ledger (read-only; Missing/Extra/Mismatch). */
    verify(): Promise<BalanceSummaryVerifyDto>
    /** Fully rebuild the current tenant's summary buckets from the ledger. */
    rebuild(): Promise<BalanceSummaryRebuildDto>
  }
}

/** Shared shape for the five document sections (draft workflow + post + void). */
export interface FinanceDocSection<TDto, TCreate> {
  fetch(query: FinancePagedQuery): Promise<FinancePagedResult<TDto>>
  getById(id: string): Promise<TDto | null>
  createDraft(data: TCreate): Promise<TDto>
  updateDraft(id: string, data: TCreate): Promise<TDto>
  deleteDraft(id: string): Promise<void>
  post(id: string): Promise<TDto>
  voidDoc(id: string): Promise<TDto>
}

function toPaged<T>(result: { items?: T[]; totalCount?: number; pageIndex?: number; pageSize?: number }, query: FinancePagedQuery): FinancePagedResult<T> {
  return pagedResult({
    items: result.items ?? [],
    totalCount: result.totalCount ?? 0,
    pageIndex: result.pageIndex ?? query.pageIndex,
    pageSize: result.pageSize ?? query.pageSize,
  })
}

export function createFinanceBridge(deps: FinanceBridgeDeps = {}): FinanceBridge {
  const { client } = deps

  if (!client) {
    const noOp = () => Promise.reject(new Error('createFinanceBridge: no HttpClient provided'))
    const section = new Proxy({}, { get: () => noOp })
    return {
      accounts: section as never,
      journals: section as never,
      rates: section as never,
      fiscalYears: section as never,
      reports: section as never,
      customers: section as never,
      vendors: section as never,
      items: section as never,
      taxes: section as never,
      invoices: section as never,
      bills: section as never,
      expenses: section as never,
      creditMemos: section as never,
      payments: section as never,
      transfers: section as never,
      reconciliations: section as never,
      revaluations: section as never,
      settlements: section as never,
      bankAccounts: section as never,
      partyBankAccounts: section as never,
      bankFeed: section as never,
      checks: section as never,
      eftBatches: section as never,
      receipts: section as never,
      balanceSummary: section as never,
    }
  }

  const accountApi = useAdminFinanceAccountApi(client)
  const journalApi = useAdminJournalEntryApi(client)
  const rateApi = useAdminExchangeRateApi(client)
  const fiscalApi = useAdminFiscalYearApi(client)
  const reportApi = useAdminFinanceReportApi(client)
  const customerApi = useAdminFinanceCustomerApi(client)
  const vendorApi = useAdminFinanceVendorApi(client)
  const itemApi = useAdminFinanceItemApi(client)
  const taxApi = useAdminFinanceTaxApi(client)
  const settlementApi = useAdminFinanceSettlementApi(client)

  /** Standard party/catalog CRUD section over a core list API. */
  function crudSection<TDto, TCreate, TUpdate>(api: {
    getList(params?: unknown): Promise<unknown>
    create(data: TCreate): Promise<unknown>
    update(id: string, data: TUpdate): Promise<unknown>
    delete(id: string): Promise<unknown>
  }) {
    return {
      fetch: async (query: FinancePagedQuery) => {
        const filters = query.filters ?? {}
        const result = unwrap<FinancePagedResult<TDto>>(
          (await api.getList({
            pageIndex: query.pageIndex,
            pageSize: query.pageSize,
            keyword: query.searchText || undefined,
            ...filters,
          })) as never,
        )
        return toPaged(result, query)
      },
      create: async (data: TCreate) => unwrap<TDto>((await api.create(data)) as never),
      update: async (id: string, data: TUpdate) => unwrap<TDto>((await api.update(id, data)) as never),
      delete: async (ids: string[]) => {
        for (const id of ids) {
          ensureOk((await api.delete(id)) as never)
        }
      },
    }
  }

  /** Document section (draft workflow + post + void) over a core document API. */
  function docSection<TDto, TCreate>(api: {
    getList(params?: unknown): Promise<unknown>
    get(id: string): Promise<unknown>
    createDraft(data: TCreate): Promise<unknown>
    updateDraft(id: string, data: TCreate): Promise<unknown>
    deleteDraft(id: string): Promise<unknown>
    post(id: string): Promise<unknown>
    void(id: string): Promise<unknown>
  }): FinanceDocSection<TDto, TCreate> {
    return {
      fetch: async (query) => {
        const filters = query.filters ?? {}
        const result = unwrap<FinancePagedResult<TDto>>(
          (await api.getList({
            pageIndex: query.pageIndex,
            pageSize: query.pageSize,
            keyword: query.searchText || undefined,
            ...filters,
          })) as never,
        )
        return toPaged(result, query)
      },
      getById: async (id) => unwrap<TDto | null>((await api.get(id)) as never),
      createDraft: async (data) => unwrap<TDto>((await api.createDraft(data)) as never),
      updateDraft: async (id, data) => unwrap<TDto>((await api.updateDraft(id, data)) as never),
      deleteDraft: async (id) => {
        ensureOk((await api.deleteDraft(id)) as never)
      },
      post: async (id) => unwrap<TDto>((await api.post(id)) as never),
      voidDoc: async (id) => unwrap<TDto>((await api.void(id)) as never),
    }
  }

  const paymentApi = useAdminFinancePaymentApi(client)

  return {
    accounts: {
      fetch: async (query) => {
        const filters = query.filters ?? {}
        const result = unwrap<FinancePagedResult<AccountDto>>(
          await accountApi.getList({
            pageIndex: query.pageIndex,
            pageSize: query.pageSize,
            keyword: query.searchText || undefined,
            rootType: (filters.rootType as number | undefined) ?? undefined,
            isActive: (filters.isActive as boolean | undefined) ?? undefined,
          } as never),
        )
        return toPaged(result, query)
      },
      tree: async (includeInactive = false) =>
        unwrap<AccountTreeDto[]>(await accountApi.getTree(includeInactive)) ?? [],
      balances: async (accountIds, asOf) =>
        accountIds.length === 0
          ? []
          : unwrap<AccountBalanceDto[]>(await accountApi.getBalances({ accountIds, asOf: asOf ?? null })) ?? [],
      create: async (data) => unwrap<AccountDto>(await accountApi.create(data)),
      update: async (id, data) => unwrap<AccountDto>(await accountApi.update(id, data)),
      delete: async (ids) => {
        for (const id of ids) {
          ensureOk(await accountApi.delete(id))
        }
      },
      seedDefault: async () => unwrap<number>(await accountApi.seedDefault()),
    },

    journals: {
      fetch: async (query) => {
        const filters = query.filters ?? {}
        const result = unwrap<FinancePagedResult<JournalEntryDto>>(
          await journalApi.getList({
            pageIndex: query.pageIndex,
            pageSize: query.pageSize,
            keyword: query.searchText || undefined,
            status: (filters.status as number | undefined) ?? undefined,
            dateFrom: (filters.dateFrom as string | undefined) ?? undefined,
            dateTo: (filters.dateTo as string | undefined) ?? undefined,
            sourceType: (filters.sourceType as string | undefined) ?? undefined,
          } as never),
        )
        return toPaged(result, query)
      },
      getById: async (id) => unwrap<JournalEntryDto | null>(await journalApi.get(id)),
      createDraft: async (data) => unwrap<JournalEntryDto>(await journalApi.createDraft(data)),
      updateDraft: async (id, data) => unwrap<JournalEntryDto>(await journalApi.updateDraft(id, data)),
      deleteDraft: async (id) => {
        ensureOk(await journalApi.deleteDraft(id))
      },
      post: async (id) => unwrap<JournalEntryDto>(await journalApi.post(id)),
      reverse: async (id, data) => unwrap<JournalEntryDto>(await journalApi.reverse(id, data)),
    },

    rates: {
      fetch: async (query) => {
        const filters = query.filters ?? {}
        const result = unwrap<FinancePagedResult<ExchangeRateDto>>(
          await rateApi.getList({
            pageIndex: query.pageIndex,
            pageSize: query.pageSize,
            fromCurrency: (filters.fromCurrency as string | undefined) ?? (query.searchText || undefined),
            toCurrency: (filters.toCurrency as string | undefined) ?? undefined,
          } as never),
        )
        return toPaged(result, query)
      },
      upsert: async (data) => unwrap<ExchangeRateDto>(await rateApi.upsert(data)),
      delete: async (ids) => {
        for (const id of ids) {
          ensureOk(await rateApi.delete(id))
        }
      },
      refresh: async () => unwrap<number>(await rateApi.refresh()),
    },

    fiscalYears: {
      list: async () => unwrap<FiscalYearDto[]>(await fiscalApi.getList()) ?? [],
      create: async (data) => unwrap<FiscalYearDto>(await fiscalApi.create(data)),
      close: async (id) => {
        ensureOk(await fiscalApi.close(id))
      },
      reopen: async (id) => {
        ensureOk(await fiscalApi.reopen(id))
      },
      delete: async (ids) => {
        for (const id of ids) {
          ensureOk(await fiscalApi.delete(id))
        }
      },
    },

    reports: {
      trialBalance: async (from, to) =>
        unwrap<TrialBalanceReportDto>(await reportApi.getTrialBalance(from, to)),
      balanceSheet: async (asOf) =>
        unwrap<BalanceSheetReportDto>(await reportApi.getBalanceSheet(asOf)),
      profitAndLoss: async (from, to) =>
        unwrap<ProfitAndLossReportDto>(await reportApi.getProfitAndLoss(from, to)),
      generalLedger: async (accountId, from, to, pageIndex = 1, pageSize = 20) =>
        unwrap<GeneralLedgerReportDto>(await reportApi.getGeneralLedger(accountId, from, to, pageIndex, pageSize)),
      arAging: async (asOf) => unwrap<AgingReportDto>(await reportApi.getArAging(asOf)),
      apAging: async (asOf) => unwrap<AgingReportDto>(await reportApi.getApAging(asOf)),
      taxSummary: async (from, to) =>
        unwrap<TaxSummaryReportDto>(await reportApi.getTaxSummary(from, to)),
      cashFlow: async (from, to) =>
        unwrap<CashFlowReportDto>(await reportApi.getCashFlow(from, to)),
      exportTrialBalanceCsv: async (from, to) =>
        unwrap<Blob>(await reportApi.exportTrialBalanceCsv(from, to)),
      exportBalanceSheetCsv: async (asOf) =>
        unwrap<Blob>(await reportApi.exportBalanceSheetCsv(asOf)),
      exportProfitAndLossCsv: async (from, to) =>
        unwrap<Blob>(await reportApi.exportProfitAndLossCsv(from, to)),
      exportGeneralLedgerCsv: async (accountId, from, to) =>
        unwrap<Blob>(await reportApi.exportGeneralLedgerCsv(accountId, from, to)),
      exportArAgingCsv: async (asOf) => unwrap<Blob>(await reportApi.exportArAgingCsv(asOf)),
      exportApAgingCsv: async (asOf) => unwrap<Blob>(await reportApi.exportApAgingCsv(asOf)),
      exportTaxSummaryCsv: async (from, to) =>
        unwrap<Blob>(await reportApi.exportTaxSummaryCsv(from, to)),
      exportCashFlowCsv: async (from, to) =>
        unwrap<Blob>(await reportApi.exportCashFlowCsv(from, to)),
    },

    customers: crudSection<CustomerDto, CoreCreateCustomerDto, CoreUpdateCustomerDto>(customerApi),
    vendors: crudSection<VendorDto, CoreCreateVendorDto, CoreUpdateVendorDto>(vendorApi),
    items: crudSection<ItemDto, CoreCreateItemDto, CoreUpdateItemDto>(itemApi),

    taxes: {
      agencies: async () => unwrap<TaxAgencyDto[]>(await taxApi.getAgencies()) ?? [],
      createAgency: async (data) => unwrap<TaxAgencyDto>(await taxApi.createAgency(data)),
      updateAgency: async (id, data) => unwrap<TaxAgencyDto>(await taxApi.updateAgency(id, data)),
      deleteAgency: async (id) => {
        ensureOk(await taxApi.deleteAgency(id))
      },
      rates: async (agencyId) => unwrap<TaxRateDto[]>(await taxApi.getRates(agencyId)) ?? [],
      createRate: async (data) => unwrap<TaxRateDto>(await taxApi.createRate(data)),
      updateRate: async (id, data) => unwrap<TaxRateDto>(await taxApi.updateRate(id, data)),
      deleteRate: async (id) => {
        ensureOk(await taxApi.deleteRate(id))
      },
      codes: async () => unwrap<TaxCodeDto[]>(await taxApi.getCodes()) ?? [],
      createCode: async (data) => unwrap<TaxCodeDto>(await taxApi.createCode(data)),
      updateCode: async (id, data) => unwrap<TaxCodeDto>(await taxApi.updateCode(id, data)),
      deleteCode: async (id) => {
        ensureOk(await taxApi.deleteCode(id))
      },
    },

    invoices: docSection<InvoiceDto, CoreCreateInvoiceDto>(useAdminFinanceInvoiceApi(client)),
    bills: docSection<BillDto, CoreCreateBillDto>(useAdminFinanceBillApi(client)),
    expenses: docSection<ExpenseDto, CoreCreateExpenseDto>(useAdminFinanceExpenseApi(client)),
    creditMemos: docSection<CreditMemoDto, CoreCreateCreditMemoDto>(useAdminFinanceCreditMemoApi(client)),
    payments: docSection<PaymentEntryDto, CoreCreatePaymentEntryDto>(paymentApi),
    transfers: docSection<TransferDto, CoreCreateTransferDto>(useAdminFinanceTransferApi(client)),

    reconciliations: (() => {
      const api = useAdminFinanceReconciliationApi(client)
      return {
        fetch: async (query: FinancePagedQuery) => {
          const filters = query.filters ?? {}
          const result = unwrap<FinancePagedResult<ReconciliationDto>>(
            (await api.getList({
              pageIndex: query.pageIndex,
              pageSize: query.pageSize,
              ...filters,
            } as never)) as never,
          )
          return toPaged(result, query)
        },
        getById: async (id: string) => unwrap<ReconciliationDto | null>(await api.get(id)),
        create: async (data: CoreCreateReconciliationDto) => unwrap<ReconciliationDto>(await api.create(data)),
        update: async (id: string, data: CoreCreateReconciliationDto) => unwrap<ReconciliationDto>(await api.update(id, data)),
        delete: async (id: string) => {
          ensureOk(await api.delete(id))
        },
        worksheet: async (id: string) => unwrap<ReconciliationWorksheetDto>(await api.getWorksheet(id)),
        setLines: async (id: string, journalLineIds: string[]) =>
          unwrap<ReconciliationWorksheetDto>(await api.setLines(id, { journalLineIds })),
        complete: async (id: string) => unwrap<ReconciliationDto>(await api.complete(id)),
      }
    })(),

    revaluations: (() => {
      const api = useAdminFinanceRevaluationApi(client)
      return {
        preview: async (data: RunRevaluationDto) => unwrap<RevaluationPreviewDto>(await api.preview(data)),
        run: async (data: RunRevaluationDto) => unwrap<RevaluationPreviewDto>(await api.run(data)),
      }
    })(),

    settlements: {
      applications: async (docType, docId) =>
        unwrap<PaymentApplicationDto[]>(await settlementApi.getApplications(docType, docId)) ?? [],
      openDocuments: async (partyType, partyId) =>
        unwrap<OpenDocumentDto[]>(await settlementApi.getOpenDocuments(partyType, partyId)) ?? [],
      apply: async (data) => unwrap<PaymentApplicationDto[]>(await settlementApi.apply(data)),
      unapply: async (applicationId) => {
        ensureOk(await settlementApi.unapply(applicationId))
      },
      pay: async (data) => unwrap<BatchPaymentResultDto>(await settlementApi.pay(data)),
    },

    bankAccounts: (() => {
      const api = useAdminFinanceBankAccountApi(client)
      return {
        capabilities: async () =>
          unwrap<BankAccountCapabilitiesDto>(await api.getCapabilities()),
        fetch: async (query: FinancePagedQuery) => {
          const filters = query.filters ?? {}
          const result = unwrap<FinancePagedResult<BankAccountDto>>(
            await api.getList({
              pageIndex: query.pageIndex,
              pageSize: query.pageSize,
              keyword: query.searchText || undefined,
              accountId: (filters.accountId as string | undefined) ?? undefined,
            } as never),
          )
          return toPaged(result, query)
        },
        getById: async (id: string) => unwrap<BankAccountDto | null>(await api.get(id)),
        create: async (data: CoreCreateBankAccountDto) => unwrap<BankAccountDto>(await api.create(data)),
        update: async (id: string, data: CoreUpdateBankAccountDto) => unwrap<BankAccountDto>(await api.update(id, data)),
        setNextCheckNumber: async (id: string, nextCheckNumber: number) =>
          unwrap<BankAccountDto>(await api.setNextCheckNumber(id, { nextCheckNumber })),
        delete: async (ids: string[]) => {
          for (const id of ids) ensureOk(await api.delete(id))
        },
      }
    })(),

    partyBankAccounts: (() => {
      const api = useAdminFinancePartyBankAccountApi(client)
      return {
        byParty: async (partyType, partyId) =>
          unwrap<PartyBankAccountDto[]>(await api.getByParty(partyType, partyId)) ?? [],
        save: async (data: CoreSavePartyBankAccountDto) => unwrap<PartyBankAccountDto>(await api.create(data)),
        update: async (id: string, data: CoreSavePartyBankAccountDto) => unwrap<PartyBankAccountDto>(await api.update(id, data)),
        setDefault: async (id: string) => unwrap<PartyBankAccountDto>(await api.setDefault(id)),
        delete: async (id: string) => {
          ensureOk(await api.delete(id))
        },
      }
    })(),

    bankFeed: (() => {
      const api = useAdminFinanceBankFeedApi(client)
      return {
        transactions: async (query: FinancePagedQuery) => {
          const filters = query.filters ?? {}
          const result = unwrap<FinancePagedResult<BankTransactionDto>>(
            await api.getTransactions({
              pageIndex: query.pageIndex,
              pageSize: query.pageSize,
              keyword: query.searchText || undefined,
              accountId: (filters.accountId as string | undefined) ?? undefined,
              importBatchId: (filters.importBatchId as string | undefined) ?? undefined,
              status: (filters.status as never) ?? undefined,
              dateFrom: (filters.dateFrom as string | undefined) ?? undefined,
              dateTo: (filters.dateTo as string | undefined) ?? undefined,
            } as never),
          )
          return toPaged(result, query)
        },
        import: async (accountId, source, file, mapping) =>
          unwrap<BankImportResultDto>(await api.import(accountId, source, file, mapping)),
        pull: async (accountId) => unwrap<BankImportResultDto>(await api.pull({ accountId })),
        suggest: async (accountId) => unwrap<BankSuggestResultDto>(await api.suggest(accountId)),
        candidates: async (transactionId) =>
          unwrap<BankMatchCandidateDto[]>(await api.getCandidates(transactionId)) ?? [],
        confirm: async (transactionId, data) => unwrap<BankTransactionDto>(await api.confirm(transactionId, data)),
        unmatch: async (transactionId) => unwrap<BankTransactionDto>(await api.unmatch(transactionId)),
        exclude: async (transactionId) => unwrap<BankTransactionDto>(await api.exclude(transactionId)),
        restore: async (transactionId) => unwrap<BankTransactionDto>(await api.restore(transactionId)),
        createDocument: async (transactionId, data) =>
          unwrap<BankDocumentResultDto>(await api.createDocument(transactionId, data)),
        batches: async (query: FinancePagedQuery) => {
          const filters = query.filters ?? {}
          const result = unwrap<FinancePagedResult<BankImportBatchDto>>(
            await api.getBatches({
              pageIndex: query.pageIndex,
              pageSize: query.pageSize,
              accountId: (filters.accountId as string | undefined) ?? undefined,
            } as never),
          )
          return toPaged(result, query)
        },
        deleteBatch: async (id: string) => {
          ensureOk(await api.deleteBatch(id))
        },
      }
    })(),

    checks: (() => {
      const api = useAdminFinanceCheckApi(client)
      return {
        queue: async (bankAccountId?: string) =>
          unwrap<CheckQueueItemDto[]>(await api.getQueue(bankAccountId)) ?? [],
        fetch: async (query: FinancePagedQuery) => {
          const filters = query.filters ?? {}
          const result = unwrap<FinancePagedResult<BankCheckDto>>(
            await api.getList({
              pageIndex: query.pageIndex,
              pageSize: query.pageSize,
              keyword: query.searchText || undefined,
              bankAccountId: (filters.bankAccountId as string | undefined) ?? undefined,
              status: (filters.status as never) ?? undefined,
            } as never),
          )
          return toPaged(result, query)
        },
        print: async (data: CorePrintChecksDto) => unwrap<Blob>(await api.print(data)),
        register: async (data: CoreRegisterManualCheckDto) => unwrap<BankCheckDto>(await api.register(data)),
        reprint: async (id: string) => unwrap<Blob>(await api.reprint(id)),
        voidCheck: async (id: string, data: CoreVoidCheckDto) => unwrap<BankCheckDto>(await api.void(id, data)),
        spoil: async (data: CoreSpoilCheckDto) => unwrap<BankCheckDto>(await api.spoil(data)),
        calibration: async (bankAccountId: string) => unwrap<Blob>(await api.calibration(bankAccountId)),
        exportPositivePay: async (bankAccountId: string, from: string, to: string) =>
          unwrap<Blob>(await api.exportPositivePay(bankAccountId, from, to)),
      }
    })(),

    eftBatches: (() => {
      const api = useAdminFinanceEftBatchApi(client)
      return {
        queue: async () => unwrap<EftQueueItemDto[]>(await api.getQueue()) ?? [],
        fetch: async (query: FinancePagedQuery) => {
          const filters = query.filters ?? {}
          const result = unwrap<FinancePagedResult<EftBatchDto>>(
            await api.getList({
              pageIndex: query.pageIndex,
              pageSize: query.pageSize,
              bankAccountId: (filters.bankAccountId as string | undefined) ?? undefined,
              status: (filters.status as never) ?? undefined,
              format: (filters.format as never) ?? undefined,
            } as never),
          )
          return toPaged(result, query)
        },
        getById: async (id: string) => unwrap<EftBatchDto | null>(await api.get(id)),
        create: async (data: CoreCreateEftBatchDto) => unwrap<EftBatchDto>(await api.create(data)),
        generate: async (id: string) => unwrap<EftBatchDto>(await api.generate(id)),
        voidBatch: async (id: string, data: CoreVoidEftBatchDto) => unwrap<EftBatchDto>(await api.void(id, data)),
        download: async (id: string) => unwrap<Blob>(await api.download(id)),
      }
    })(),

    receipts: (() => {
      const api = useAdminFinanceReceiptApi(client)
      return {
        fetch: async (query: FinancePagedQuery) => {
          const filters = query.filters ?? {}
          const result = unwrap<FinancePagedResult<ReceiptDto>>(
            await api.getList({
              pageIndex: query.pageIndex,
              pageSize: query.pageSize,
              keyword: query.searchText || undefined,
              status: (filters.status as never) ?? undefined,
            } as never),
          )
          return toPaged(result, query)
        },
        getById: async (id: string) => unwrap<ReceiptDto | null>(await api.get(id)),
        create: async (data: CoreCreateReceiptDto) => unwrap<ReceiptDto>(await api.create(data)),
        extract: async (id: string) => unwrap<ReceiptDto>(await api.extract(id)),
        update: async (id: string, data: CoreUpdateReceiptExtractionDto) => unwrap<ReceiptDto>(await api.update(id, data)),
        convert: async (id: string, data: CoreConvertReceiptDto) => unwrap<ReceiptConvertResultDto>(await api.convert(id, data)),
        delete: async (id: string) => {
          ensureOk(await api.delete(id))
        },
      }
    })(),

    balanceSummary: (() => {
      const api = useAdminFinanceBalanceSummaryApi(client)
      return {
        verify: async () => unwrap<BalanceSummaryVerifyDto>(await api.verify()),
        rebuild: async () => unwrap<BalanceSummaryRebuildDto>(await api.rebuild()),
      }
    })(),
  }
}
