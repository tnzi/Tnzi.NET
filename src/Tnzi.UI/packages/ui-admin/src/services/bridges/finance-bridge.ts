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
  type AccountDto as CoreAccountDto,
  type AccountTreeDto as CoreAccountTreeDto,
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
  type AgingReportDto as CoreAgingReportDto,
  type AgingRowDto as CoreAgingRowDto,
} from '@tnzi/core/services/finance'
import type { PagedList } from '@tnzi/core'
import type { FinancePartyType, SettlementDocType } from '@tnzi/core/services/finance'
import { unwrapResult as unwrap, pagedResult } from '../_mappers'

// Re-export the DTO types under bridge names consumed by pages/configs.
export type AccountDto = CoreAccountDto
export type AccountTreeDto = CoreAccountTreeDto
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
export type AgingReportDto = CoreAgingReportDto
export type AgingRowDto = CoreAgingRowDto

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
  settlements: {
    applications(docType: SettlementDocType, docId: string): Promise<PaymentApplicationDto[]>
    openDocuments(partyType: FinancePartyType, partyId: string): Promise<OpenDocumentDto[]>
    apply(data: CoreApplySettlementDto): Promise<PaymentApplicationDto[]>
    unapply(applicationId: string): Promise<void>
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
      settlements: section as never,
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
          unwrap<void>((await api.delete(id)) as never)
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
        unwrap<void>((await api.deleteDraft(id)) as never)
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
      create: async (data) => unwrap<AccountDto>(await accountApi.create(data)),
      update: async (id, data) => unwrap<AccountDto>(await accountApi.update(id, data)),
      delete: async (ids) => {
        for (const id of ids) {
          unwrap<void>(await accountApi.delete(id))
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
        unwrap<void>(await journalApi.deleteDraft(id))
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
          unwrap<void>(await rateApi.delete(id))
        }
      },
      refresh: async () => unwrap<number>(await rateApi.refresh()),
    },

    fiscalYears: {
      list: async () => unwrap<FiscalYearDto[]>(await fiscalApi.getList()) ?? [],
      create: async (data) => unwrap<FiscalYearDto>(await fiscalApi.create(data)),
      close: async (id) => {
        unwrap<void>(await fiscalApi.close(id))
      },
      reopen: async (id) => {
        unwrap<void>(await fiscalApi.reopen(id))
      },
      delete: async (ids) => {
        for (const id of ids) {
          unwrap<void>(await fiscalApi.delete(id))
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
    },

    customers: crudSection<CustomerDto, CoreCreateCustomerDto, CoreUpdateCustomerDto>(customerApi),
    vendors: crudSection<VendorDto, CoreCreateVendorDto, CoreUpdateVendorDto>(vendorApi),
    items: crudSection<ItemDto, CoreCreateItemDto, CoreUpdateItemDto>(itemApi),

    taxes: {
      agencies: async () => unwrap<TaxAgencyDto[]>(await taxApi.getAgencies()) ?? [],
      createAgency: async (data) => unwrap<TaxAgencyDto>(await taxApi.createAgency(data)),
      updateAgency: async (id, data) => unwrap<TaxAgencyDto>(await taxApi.updateAgency(id, data)),
      deleteAgency: async (id) => {
        unwrap<void>(await taxApi.deleteAgency(id))
      },
      rates: async (agencyId) => unwrap<TaxRateDto[]>(await taxApi.getRates(agencyId)) ?? [],
      createRate: async (data) => unwrap<TaxRateDto>(await taxApi.createRate(data)),
      updateRate: async (id, data) => unwrap<TaxRateDto>(await taxApi.updateRate(id, data)),
      deleteRate: async (id) => {
        unwrap<void>(await taxApi.deleteRate(id))
      },
      codes: async () => unwrap<TaxCodeDto[]>(await taxApi.getCodes()) ?? [],
      createCode: async (data) => unwrap<TaxCodeDto>(await taxApi.createCode(data)),
      updateCode: async (id, data) => unwrap<TaxCodeDto>(await taxApi.updateCode(id, data)),
      deleteCode: async (id) => {
        unwrap<void>(await taxApi.deleteCode(id))
      },
    },

    invoices: docSection<InvoiceDto, CoreCreateInvoiceDto>(useAdminFinanceInvoiceApi(client)),
    bills: docSection<BillDto, CoreCreateBillDto>(useAdminFinanceBillApi(client)),
    expenses: docSection<ExpenseDto, CoreCreateExpenseDto>(useAdminFinanceExpenseApi(client)),
    creditMemos: docSection<CreditMemoDto, CoreCreateCreditMemoDto>(useAdminFinanceCreditMemoApi(client)),
    payments: docSection<PaymentEntryDto, CoreCreatePaymentEntryDto>(paymentApi),

    settlements: {
      applications: async (docType, docId) =>
        unwrap<PaymentApplicationDto[]>(await settlementApi.getApplications(docType, docId)) ?? [],
      openDocuments: async (partyType, partyId) =>
        unwrap<OpenDocumentDto[]>(await settlementApi.getOpenDocuments(partyType, partyId)) ?? [],
      apply: async (data) => unwrap<PaymentApplicationDto[]>(await settlementApi.apply(data)),
      unapply: async (applicationId) => {
        unwrap<void>(await settlementApi.unapply(applicationId))
      },
    },
  }
}
